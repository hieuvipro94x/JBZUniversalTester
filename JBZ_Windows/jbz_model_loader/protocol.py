from __future__ import annotations

import re
from .models import Expectation

KNOWN_FAMILIES = {
    "MODEL", "PINCOUNT", "PINDATA", "ARRAYCOUNT", "ARRAY",
    "CONCOUNT", "CON", "CONNECTORCOUNT", "CONNECTOR",
    "CONTESTCOUNT", "CONTESTDATA", "FINISH",
}


def normalize_command(text: str) -> str:
    value = text.strip().rstrip("\r\n")
    if not value:
        raise ValueError("Lệnh rỗng")
    if "\r" in value or "\n" in value:
        raise ValueError("Một command không được chứa nhiều dòng")
    return value


def command_family(command: str) -> str:
    value = normalize_command(command)
    return value.lstrip(":*").split(",", 1)[0].split("?", 1)[0].upper()


def command_index(command: str) -> str | None:
    parts = normalize_command(command).split(",")
    family = command_family(command)
    if family in {"PINDATA", "ARRAY", "CON", "CONNECTOR"} and len(parts) > 1:
        return parts[1]
    return None


def default_expectation(command: str) -> Expectation:
    family = command_family(command)
    idx = command_index(command)
    if family == "MODEL":
        return Expectation("exact", ":OK,MODEL", 3.0)
    if family == "PINCOUNT":
        return Expectation("exact", ":OK,PINCOUNT", 2.0)
    if family in {"PINDATA", "ARRAY", "CON", "CONNECTOR"}:
        if idx is None:
            raise ValueError(f"Thiếu index trong {command}")
        return Expectation("exact", f":OK,{family},{idx}", 2.0)
    if family == "ARRAYCOUNT":
        return Expectation("exact", ":OK,ARRAYCOUNT", 2.0)
    if family == "CONCOUNT":
        return Expectation("exact", ":OK,CONCOUNT", 2.0)
    if family == "CONNECTORCOUNT":
        return Expectation("exact", ":OK,CONNECTORCOUNT", 2.0)
    if family == "FINISH":
        return Expectation("prefix", ":OK,FINISH,", 4.0)
    if family in {"CONTESTCOUNT", "CONTESTDATA"}:
        raise ValueError(
            f"{family} chưa có ACK được xác nhận trong golden trace; "
            "profile phải khai báo expect rõ ràng"
        )
    raise ValueError(f"Không biết ACK mặc định cho command: {command}")


def extract_model_name(commands: list[str]) -> str:
    for command in commands:
        if command.startswith(":MODEL,"):
            return command.split(",", 1)[1].strip()
    return "UNKNOWN"


def validate_sequence(commands: list[str]) -> list[str]:
    warnings: list[str] = []
    families = [command_family(c) for c in commands]
    required = ["MODEL", "PINCOUNT", "ARRAYCOUNT", "CONCOUNT", "CONNECTORCOUNT", "FINISH"]
    for family in required:
        if family not in families:
            raise ValueError(f"Thiếu command bắt buộc: {family}")
    if families[0] != "MODEL":
        raise ValueError("Command đầu tiên phải là :MODEL")
    if families[-1] != "FINISH":
        raise ValueError("Command cuối cùng phải là :FINISH")
    if families.index("PINCOUNT") > families.index("ARRAYCOUNT"):
        raise ValueError("PINCOUNT phải đứng trước ARRAYCOUNT")
    if families.index("ARRAYCOUNT") > families.index("CONCOUNT"):
        raise ValueError("ARRAYCOUNT phải đứng trước CONCOUNT")
    if families.index("CONCOUNT") > families.index("CONNECTORCOUNT"):
        raise ValueError("CONCOUNT phải đứng trước CONNECTORCOUNT")

    def declared_count(prefix: str) -> int | None:
        for cmd in commands:
            if cmd.startswith(prefix + ","):
                try:
                    return int(cmd.split(",", 1)[1])
                except ValueError:
                    return None
        return None

    checks = [
        ("PINCOUNT", "PINDATA"),
        ("ARRAYCOUNT", "ARRAY"),
        ("CONCOUNT", "CON"),
        ("CONNECTORCOUNT", "CONNECTOR"),
    ]
    for count_family, item_family in checks:
        declared = declared_count(":" + count_family)
        actual = sum(1 for family in families if family == item_family)
        if declared is None:
            raise ValueError(f"Giá trị {count_family} không hợp lệ")
        if declared != actual:
            raise ValueError(
                f"{count_family}={declared} nhưng có {actual} command {item_family}"
            )

    for item_family in {"PINDATA", "ARRAY", "CON", "CONNECTOR"}:
        indexes = []
        for command in commands:
            if command_family(command) == item_family:
                idx = command_index(command)
                if idx is None or not re.fullmatch(r"\d+", idx):
                    raise ValueError(f"Index không hợp lệ: {command}")
                indexes.append(int(idx))
        expected = list(range(len(indexes)))
        if indexes != expected:
            raise ValueError(
                f"Index {item_family} phải liên tục 0..{len(indexes)-1}, nhận {indexes[:10]}"
            )

    if any(f in {"CONTESTCOUNT", "CONTESTDATA"} for f in families):
        warnings.append(
            "Profile có CONTESTCOUNT/CONTESTDATA. Đây là command family đã thấy trong "
            "phân tích binary nhưng ACK/payload chưa được golden trace WH322110 xác nhận."
        )
    return warnings
