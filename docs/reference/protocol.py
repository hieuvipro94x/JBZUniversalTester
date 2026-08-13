from __future__ import annotations

from dataclasses import dataclass
from typing import Any


@dataclass()
class BoardEvent:
    family: str
    raw: str
    values: tuple[Any, ...] = ()


def _numbers(parts: list[str]) -> tuple[int, ...]:
    values: list[int] = []
    for item in parts:
        try:
            values.append(int(item.strip()))
        except ValueError:
            pass
    return tuple(values)


def parse_board_line(line: str) -> BoardEvent:
    text = line.strip()
    if not text:
        return BoardEvent("EMPTY", text)
    if text.startswith("Universal Tester") or text.startswith("UniversalTester"):
        return BoardEvent("IDN", text, (text,))
    if text.startswith(":MODELNAME,"):
        return BoardEvent("MODELNAME", text, tuple(text.split(",")[1:]))
    if text == ":START,ON":
        return BoardEvent("START", text, ("ON",))
    if text == ":MEASURE":
        return BoardEvent("MEASURE", text)
    if text == ":CLEAR":
        return BoardEvent("CLEAR", text)
    if text.startswith(":OPEN,"):
        return BoardEvent("OPEN", text, _numbers(text.split(",")[1:]))
    if text.startswith(":OTHER,"):
        return BoardEvent("OTHER", text, _numbers(text.split(",")[1:]))
    if text.startswith(":TESTPIN,"):
        parts = text.split(",")
        try:
            pin = int(parts[1].strip())
        except (IndexError, ValueError):
            return BoardEvent("RAW", text, (text,))
        state = parts[2].strip().upper() if len(parts) > 2 else "ON"
        return BoardEvent("TESTPIN", text, (pin, state))
    if text.startswith(":PIN,"):
        # Một số firmware phát bản tin chẩn đoán dạng :PIN,<pin>,<value>.
        return BoardEvent("PIN", text, _numbers(text.split(",")[1:]))
    if text.startswith(":CIRCUIT,"):
        try:
            value = int(text.split(",", 1)[1])
        except ValueError:
            value = -1
        return BoardEvent("CIRCUIT", text, (value,))
    if text.startswith(":INPUT,"):
        parts = text.split(",")
        return BoardEvent("INPUT", text, (int(parts[1]), parts[2] if len(parts) > 2 else ""))
    if text.startswith(":OUTPUT,"):
        parts = text.split(",")
        return BoardEvent("OUTPUT", text, (int(parts[1]), parts[2] if len(parts) > 2 else ""))
    for family in ("RESISTOR", "AMPARE", "VOLTAGE"):
        if text.startswith(f":{family},"):
            value = text.split(",", 1)[1]
            try:
                value = int(value)
            except ValueError:
                pass
            return BoardEvent(family, text, (value,))
    if text.startswith(":ERROR") or text.startswith(":NAK"):
        return BoardEvent("ERROR", text, (text,))
    if text in {":PASS", ":PEN", ":REMOVAL", ":UNCONNECT", ":STOP"}:
        return BoardEvent(text[1:], text)
    if text in {"BOOT", "BootLoader", "START PROCESS"}:
        return BoardEvent("BOOT", text, (text,))
    if text == ":ACK":
        return BoardEvent("ACK", text)
    return BoardEvent("RAW", text, (text,))
