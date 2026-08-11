from __future__ import annotations

import configparser
import json
import re
from pathlib import Path
from typing import Any

from .models import Expectation, ProtocolCommand, ProtocolProfile, SetupProfile
from .protocol import default_expectation, extract_model_name, normalize_command, validate_sequence
from .model_compiler import compile_legacy_model


class UnsupportedLegacyModel(ValueError):
    pass


def _read_text(path: Path) -> str:
    raw = path.read_bytes()
    for encoding in ("utf-8-sig", "utf-8", "cp949", "euc-kr", "cp1252"):
        try:
            return raw.decode(encoding)
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1", errors="replace")


def _expect_from_obj(obj: Any, command: str) -> Expectation:
    if obj is None:
        return default_expectation(command)
    if isinstance(obj, str):
        return Expectation("exact", obj)
    if not isinstance(obj, dict):
        raise ValueError(f"expect không hợp lệ cho {command}")
    return Expectation(
        mode=str(obj.get("mode", "exact")),
        value=str(obj["value"]),
        timeout=float(obj.get("timeout", 2.0)),
    )


def load_json_profile(path: Path) -> ProtocolProfile:
    data = json.loads(_read_text(path))
    if not isinstance(data, dict):
        raise ValueError("Profile JSON phải là object")
    rows = data.get("commands")
    if not isinstance(rows, list) or not rows:
        raise ValueError("Profile JSON thiếu commands")
    commands: list[ProtocolCommand] = []
    for index, row in enumerate(rows):
        if isinstance(row, str):
            tx = normalize_command(row)
            expect = default_expectation(tx)
            source = "model"
            note = ""
        elif isinstance(row, dict):
            tx = normalize_command(str(row["tx"]))
            expect = _expect_from_obj(row.get("expect"), tx)
            source = str(row.get("source", "model"))
            note = str(row.get("note", ""))
        else:
            raise ValueError(f"commands[{index}] không hợp lệ")
        commands.append(ProtocolCommand(tx, expect, source, note))
    warnings = validate_sequence([c.tx for c in commands])
    model_name = str(data.get("model_name") or extract_model_name([c.tx for c in commands]))
    return ProtocolProfile(
        model_name=model_name,
        commands=commands,
        source_path=path,
        metadata={k: v for k, v in data.items() if k != "commands"},
        warnings=warnings,
    )


def _extract_commands_from_transcript(text: str) -> list[str]:
    commands: list[str] = []
    for raw_line in text.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or line.startswith(";"):
            continue
        match = re.search(r"(?:^|\s)TX\s+(:[A-Z][^\r\n]*)$", line, re.IGNORECASE)
        if match:
            command = normalize_command(match.group(1))
        elif line.startswith(":"):
            command = normalize_command(line.split("=>", 1)[0].strip())
        else:
            continue
        family = command.lstrip(":").split(",", 1)[0].upper()
        if family in {
            "MODEL", "PINCOUNT", "PINDATA", "ARRAYCOUNT", "ARRAY",
            "CONCOUNT", "CON", "CONNECTORCOUNT", "CONNECTOR",
            "CONTESTCOUNT", "CONTESTDATA", "FINISH",
        }:
            commands.append(command)
    return commands


def load_transcript_profile(path: Path) -> ProtocolProfile:
    text = _read_text(path)
    command_texts = _extract_commands_from_transcript(text)
    if not command_texts:
        raise ValueError("Không tìm thấy command model trong file")
    warnings = validate_sequence(command_texts)
    commands = [
        ProtocolCommand(command, default_expectation(command), "model")
        for command in command_texts
    ]
    return ProtocolProfile(
        model_name=extract_model_name(command_texts),
        commands=commands,
        source_path=path,
        metadata={"format": "text-transcript"},
        warnings=warnings,
    )


def inspect_legacy_model(path: Path) -> dict[str, Any]:
    text = _read_text(path)
    parser = configparser.ConfigParser(interpolation=None, strict=False)
    parser.optionxform = str
    try:
        parser.read_string(text)
    except configparser.Error as exc:
        raise UnsupportedLegacyModel(f"Không đọc được legacy .model dạng INI: {exc}") from exc
    sections: dict[str, dict[str, str]] = {}
    for section in parser.sections():
        sections[section] = dict(parser.items(section))
    if not sections:
        raise UnsupportedLegacyModel("File không có section INI")
    return {
        "format": "legacy-ini-model",
        "sections": sections,
        "section_names": list(sections),
        "has_common": parser.has_section("Common"),
        "has_connector": parser.has_section("Connector"),
        "has_pin": parser.has_section("Pin"),
    }


def find_sidecar_profile(model_path: Path) -> Path | None:
    candidates = [
        model_path.with_suffix(".profile.json"),
        model_path.with_suffix(".json"),
        model_path.parent / (model_path.name + ".profile.json"),
        model_path.parent / (model_path.stem + ".uart.txt"),
        model_path.parent / (model_path.stem + ".protocol.txt"),
    ]
    for candidate in candidates:
        if candidate.exists() and candidate != model_path:
            return candidate
    return None


def load_model_profile(path: str | Path) -> ProtocolProfile:
    model_path = Path(path).expanduser().resolve()
    if not model_path.exists():
        raise FileNotFoundError(model_path)
    suffix = model_path.suffix.lower()
    if suffix == ".json":
        return load_json_profile(model_path)
    text = _read_text(model_path)
    if _extract_commands_from_transcript(text):
        return load_transcript_profile(model_path)
    if suffix == ".model" or "[Common]" in text or "[Pin]" in text:
        details = inspect_legacy_model(model_path)
        sidecar = find_sidecar_profile(model_path)
        if sidecar:
            profile = load_model_profile(sidecar)
            profile.metadata["legacy_model"] = str(model_path)
            profile.metadata["legacy_sections"] = details["section_names"]
            profile.warnings.append(
                f"Dùng protocol sidecar thật {sidecar.name}; không mô phỏng."
            )
            return profile
        profile, _summary = compile_legacy_model(model_path)
        return profile
    raise ValueError("Định dạng model/profile không được hỗ trợ")


def _setup_commands_from_json(data: dict[str, Any]) -> list[ProtocolCommand]:
    result: list[ProtocolCommand] = []
    for index, row in enumerate(data.get("board_commands", [])):
        if not isinstance(row, dict) or "tx" not in row or "expect" not in row:
            raise ValueError(
                f"setup board_commands[{index}] phải có tx và expect; không tự đoán ACK setup"
            )
        tx = normalize_command(str(row["tx"]))
        expect = _expect_from_obj(row["expect"], tx)
        result.append(ProtocolCommand(tx, expect, "setup", str(row.get("note", ""))))
    return result


def load_setup_profile(path: str | Path | None) -> SetupProfile:
    if not path:
        return SetupProfile(name="Không có setup")
    setup_path = Path(path).expanduser().resolve()
    if not setup_path.exists():
        raise FileNotFoundError(setup_path)
    text = _read_text(setup_path)
    warnings: list[str] = []
    if setup_path.suffix.lower() == ".json":
        data = json.loads(text)
        if not isinstance(data, dict):
            raise ValueError("Setup JSON phải là object")
        commands = _setup_commands_from_json(data)
        return SetupProfile(
            name=str(data.get("name") or setup_path.stem),
            values=dict(data.get("values") or {}),
            board_commands=commands,
            source_path=setup_path,
            warnings=warnings,
        )

    parser = configparser.ConfigParser(interpolation=None, strict=False)
    parser.optionxform = str
    try:
        parser.read_string(text)
        values = {section: dict(parser.items(section)) for section in parser.sections()}
    except configparser.Error:
        values = {"raw": {"text": text}}
    warnings.append(
        "Setup legacy được load cục bộ. Không có board_commands có expect đã xác nhận, "
        "nên file setup này không được gửi nguyên dạng xuống bo."
    )
    return SetupProfile(
        name=setup_path.stem,
        values=values,
        board_commands=[],
        source_path=setup_path,
        warnings=warnings,
    )


def merge_setup_before_finish(model: ProtocolProfile, setup: SetupProfile) -> ProtocolProfile:
    if not setup.board_commands:
        model.metadata["setup_name"] = setup.name
        model.metadata["setup_local_only"] = True
        model.warnings.extend(setup.warnings)
        return model
    merged: list[ProtocolCommand] = []
    inserted = False
    for command in model.commands:
        if command.tx == ":FINISH" and not inserted:
            merged.extend(setup.board_commands)
            inserted = True
        merged.append(command)
    if not inserted:
        raise ValueError("Model profile thiếu :FINISH")
    result = ProtocolProfile(
        model_name=model.model_name,
        commands=merged,
        source_path=model.source_path,
        metadata={**model.metadata, "setup_name": setup.name, "setup_local_only": False},
        warnings=[*model.warnings, *setup.warnings],
    )
    return result
