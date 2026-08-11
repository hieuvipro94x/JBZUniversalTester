from __future__ import annotations

import configparser
import math
import re
from dataclasses import dataclass, replace
from pathlib import Path

from .models import ProtocolCommand, ProtocolProfile
from .protocol import default_expectation, validate_sequence


class ModelCompileError(ValueError):
    pass


@dataclass(frozen=True)
class LegacyPin:
    row_index: int
    physical_pin: int
    connector: str
    local_pin: str
    line_name: str
    fields: tuple[str, ...]
    parent: str
    targets: tuple[int, ...]


@dataclass(frozen=True)
class CompiledModelSummary:
    model_name: str
    pin_rows: int
    source_records: int
    target_items: int
    connector_count: int
    array_packets: int
    con_packets: int
    connector_packets: int


def _read_text(path: Path) -> str:
    raw = path.read_bytes()
    for encoding in ("utf-8-sig", "utf-8", "cp949", "euc-kr", "cp1252"):
        try:
            return raw.decode(encoding)
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1", errors="replace")


def _chunks(values: list[int], size: int = 64) -> list[list[int]]:
    return [values[i:i + size] for i in range(0, len(values), size)]


def _parse_int(value: str, label: str) -> int:
    try:
        return int(value.strip())
    except ValueError as exc:
        raise ModelCompileError(f"{label} không phải số nguyên: {value!r}") from exc


def _load_ini(path: Path) -> configparser.ConfigParser:
    parser = configparser.ConfigParser(interpolation=None, strict=False)
    parser.optionxform = str
    try:
        parser.read_string(_read_text(path))
    except configparser.Error as exc:
        raise ModelCompileError(f"Không đọc được file .model: {exc}") from exc
    for section in ("Common", "Connector", "Pin"):
        if not parser.has_section(section):
            raise ModelCompileError(f"File .model thiếu section [{section}]")
    return parser


def compile_legacy_model(path: str | Path) -> tuple[ProtocolProfile, CompiledModelSummary]:
    model_path = Path(path).expanduser().resolve()
    parser = _load_ini(model_path)

    model_name = model_path.stem.strip()
    if not model_name:
        raise ModelCompileError("Không xác định được tên model từ tên file")

    connector_count = _parse_int(parser["Connector"].get("Count", ""), "Connector/Count")
    connector_names: list[str] = []
    connector_pin_counts: list[int] = []
    for index in range(1, connector_count + 1):
        key = f"C{index}"
        if key not in parser["Connector"]:
            raise ModelCompileError(f"Thiếu Connector/{key}")
        parts = parser["Connector"][key].split("|")
        if len(parts) < 2:
            raise ModelCompileError(f"Connector/{key} không hợp lệ")
        name = parts[0].strip()
        if name in connector_names:
            raise ModelCompileError(f"Tên connector bị trùng: {name}")
        connector_names.append(name)
        connector_pin_counts.append(_parse_int(parts[1], f"Connector/{key} pin count"))

    pin_count = _parse_int(parser["Pin"].get("Count", ""), "Pin/Count")
    if sum(connector_pin_counts) != pin_count:
        raise ModelCompileError(
            f"Tổng pin trong [Connector] là {sum(connector_pin_counts)} nhưng Pin/Count={pin_count}"
        )

    pins: list[LegacyPin] = []
    physical_seen: set[int] = set()
    connector_map = {name: idx for idx, name in enumerate(connector_names)}
    con_values: list[int] = []
    special_rows: list[str] = []
    compile_warnings: list[str] = []

    for row_index in range(1, pin_count + 1):
        key = f"P{row_index}"
        if key not in parser["Pin"]:
            raise ModelCompileError(f"Thiếu Pin/{key}")
        fields = tuple(parser["Pin"][key].split("|"))
        if len(fields) != 10:
            raise ModelCompileError(f"Pin/{key} phải có 10 trường, nhận {len(fields)}")
        physical = _parse_int(fields[0], f"Pin/{key} physical pin")
        if physical in physical_seen:
            raise ModelCompileError(f"Physical pin bị trùng: {physical}")
        physical_seen.add(physical)
        connector = fields[1].strip()
        if connector not in connector_map:
            raise ModelCompileError(f"Pin/{key} dùng connector không tồn tại: {connector}")
        con_values.append(connector_map[connector])
        parent = fields[8].strip()
        target_text = fields[9].strip()
        targets: tuple[int, ...]
        if target_text:
            targets = tuple(_parse_int(x, f"Pin/{key} target") for x in target_text.split("/") if x)
        else:
            targets = ()

        pins.append(LegacyPin(
            row_index=row_index,
            physical_pin=physical,
            connector=connector,
            local_pin=fields[2],
            line_name=fields[3],
            fields=fields,
            parent=parent,
            targets=targets,
        ))

    # Chuẩn hóa cụm CLIP: một chân AO chung với các chân A1..An.
    # Một số file cũ chỉ ghi một phần target ở dòng AO; các chân A còn lại để parent=-1.
    # Theo cấu hình máy JBZ, AO là đầu chung và A1..An là các đầu nhánh một pin.
    normalized_special_sources: set[int] = set()
    by_connector: dict[str, list[LegacyPin]] = {}
    for pin in pins:
        by_connector.setdefault(pin.connector.upper(), []).append(pin)

    ao_sources = [
        pin for pin in pins
        if pin.connector.upper() == "AO" and pin.parent == "-1"
    ]
    for source in ao_sources:
        branches: list[tuple[int, LegacyPin]] = []
        for pin in pins:
            match = re.fullmatch(r"A(\d+)", pin.connector.upper())
            if match and pin.physical_pin != source.physical_pin:
                branches.append((int(match.group(1)), pin))
        if not branches:
            continue
        branches.sort(key=lambda item: item[0])
        branch_phys = tuple(pin.physical_pin for _n, pin in branches)
        unknown_explicit = [target for target in source.targets if target not in branch_phys]
        if unknown_explicit:
            raise ModelCompileError(
                f"Cụm AO tại P{source.row_index} có target ngoài A1..An: {unknown_explicit}"
            )
        replacements: dict[int, LegacyPin] = {
            source.physical_pin: replace(source, targets=branch_phys)
        }
        for _number, branch in branches:
            if branch.targets:
                raise ModelCompileError(
                    f"Chân CLIP {branch.connector} physical={branch.physical_pin} "
                    "không được chứa target riêng"
                )
            if branch.parent not in ("", "-1", str(source.physical_pin)):
                raise ModelCompileError(
                    f"Chân CLIP {branch.connector} đang trỏ parent={branch.parent}, "
                    f"không khớp AO={source.physical_pin}"
                )
            replacements[branch.physical_pin] = replace(
                branch, parent=str(source.physical_pin), targets=()
            )
        pins = [replacements.get(pin.physical_pin, pin) for pin in pins]
        normalized_special_sources.add(source.physical_pin)
        compile_warnings.append(
            f"Đã chuẩn hóa CLIP AO physical={source.physical_pin} -> "
            + ",".join(str(value) for value in branch_phys)
        )

    # Chỉ cho phép loại đặc biệt AO/A đã được chuẩn hóa ở trên.
    # Các loại khác (ví dụ TEMP) vẫn bị chặn cho đến khi có quy tắc xác nhận.
    for pin in pins:
        special_type = pin.fields[7].strip()
        if pin.parent == "-1" and special_type:
            allowed_clip = (
                pin.physical_pin in normalized_special_sources
                and special_type.upper() in {"A", "AO"}
            )
            if not allowed_clip:
                special_rows.append(
                    f"P{pin.row_index}: physical={pin.physical_pin}, "
                    f"type={special_type}, line={pin.line_name}"
                )

    source_pins = [pin for pin in pins if pin.parent == "-1"]
    targets_flat: list[int] = []
    physical_set = {p.physical_pin for p in pins}
    for pin in source_pins:
        for target in pin.targets:
            if target not in physical_set:
                raise ModelCompileError(
                    f"Pin/P{pin.row_index} tham chiếu target {target} không tồn tại"
                )
            targets_flat.append(target)

    target_pins = [pin for pin in pins if pin.parent not in ("", "-1")]
    for pin in target_pins:
        parent = _parse_int(pin.parent, f"Pin/P{pin.row_index} parent")
        if parent not in physical_set:
            raise ModelCompileError(f"Pin/P{pin.row_index} parent {parent} không tồn tại")

    if len(targets_flat) != len(target_pins):
        raise ModelCompileError(
            f"Số target trong source={len(targets_flat)} nhưng số pin target={len(target_pins)}"
        )

    if special_rows:
        preview = "\n".join(special_rows[:12])
        more = "" if len(special_rows) <= 12 else f"\n... và {len(special_rows)-12} dòng khác"
        raise ModelCompileError(
            "Model có kênh đặc biệt chưa có golden trace để xác nhận hai cờ cuối PINDATA. "
            "Phần mềm Hardware Only không mô phỏng và không gửi cờ đoán xuống bo.\n" + preview + more
        )

    command_texts: list[str] = [f":MODEL,{model_name}", f":PINCOUNT,{len(source_pins)}"]
    offset = 0
    for index, pin in enumerate(source_pins):
        command_texts.append(
            f":PINDATA,{index},{pin.physical_pin},{offset},{len(pin.targets)},0,0"
        )
        offset += len(pin.targets)

    array_chunks = _chunks(targets_flat, 64)
    command_texts.append(f":ARRAYCOUNT,{len(array_chunks)}")
    for index, chunk in enumerate(array_chunks):
        payload = ",".join(str(v) for v in chunk)
        command_texts.append(f":ARRAY,{index},{len(chunk)}" + (f",{payload}" if payload else ""))

    con_payload = [*con_values, 5000, 65535]
    con_chunks = _chunks(con_payload, 64)
    command_texts.append(f":CONCOUNT,{len(con_chunks)}")
    for index, chunk in enumerate(con_chunks):
        payload = ",".join(str(v) for v in chunk)
        command_texts.append(f":CON,{index},{len(chunk)},{payload}")

    connector_chunks = _chunks(connector_pin_counts, 64)
    command_texts.append(f":CONNECTORCOUNT,{len(connector_chunks)}")
    for index, chunk in enumerate(connector_chunks):
        payload = ",".join(str(v) for v in chunk)
        command_texts.append(f":CONNECTOR,{index},{len(chunk)},{payload}")

    command_texts.append(":FINISH")
    warnings = [*compile_warnings, *validate_sequence(command_texts)]
    commands = [
        ProtocolCommand(tx, default_expectation(tx), "compiled-model")
        for tx in command_texts
    ]
    summary = CompiledModelSummary(
        model_name=model_name,
        pin_rows=pin_count,
        source_records=len(source_pins),
        target_items=len(targets_flat),
        connector_count=connector_count,
        array_packets=len(array_chunks),
        con_packets=len(con_chunks),
        connector_packets=len(connector_chunks),
    )
    profile = ProtocolProfile(
        model_name=model_name,
        commands=commands,
        source_path=model_path,
        metadata={
            "format": "legacy-ini-model-compiled",
            "pin_rows": pin_count,
            "source_records": len(source_pins),
            "target_items": len(targets_flat),
            "connector_count": connector_count,
            "array_packets": len(array_chunks),
            "con_packets": len(con_chunks),
            "connector_packets": len(connector_chunks),
            "compiler_safety": "continuity-plus-confirmed-ao-clip-bank-flags-0-0",
        },
        warnings=warnings,
    )
    return profile, summary
