#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
from dataclasses import dataclass, asdict
from datetime import datetime
from pathlib import Path
from statistics import median
from typing import Iterable

from jbz_tester.cycle_state import TestCycleState
from jbz_tester.protocol import parse_board_line

LINE_RE = re.compile(
    rb"(?P<time>\d{2}:\d{2}:\d{2}\.\d{6}) (?P<op>read|write)\((?P<fd>\d+), \"(?P<data>(?:\\x[0-9a-fA-F]{2})*)\""
)
HEX_RE = re.compile(rb"\\x([0-9a-fA-F]{2})")


@dataclass()
class SerialLine:
    timestamp: datetime
    direction: str
    text: str

    @property
    def seconds(self) -> float:
        return self.timestamp.timestamp()


@dataclass()
class CycleSummary:
    index: int
    start: str
    result: str
    circuit_value: int | None
    open_messages: int
    active_open_networks: int
    unresolved_open_links: int
    other_messages: int
    wrong_pairs: int
    duration_s: float | None
    post_result_command: str | None
    post_result_delay_ms: float | None


def decode_hex_field(field: bytes) -> bytes:
    return bytes(int(match.group(1), 16) for match in HEX_RE.finditer(field))


def extract_serial_lines(path: Path, fd: int = 19) -> list[SerialLine]:
    output: list[SerialLine] = []
    # Giữ buffer riêng cho RX/TX vì một dòng ASCII có thể bị chia giữa syscalls.
    buffers = {"RX": b"", "TX": b""}
    for raw_line in path.read_bytes().splitlines():
        match = LINE_RE.search(raw_line)
        if not match or int(match.group("fd")) != fd:
            continue
        payload = decode_hex_field(match.group("data"))
        direction = "RX" if match.group("op") == b"read" else "TX"
        timestamp = datetime.strptime(match.group("time").decode(), "%H:%M:%S.%f")
        buffers[direction] += payload
        while b"\r\n" in buffers[direction]:
            item, buffers[direction] = buffers[direction].split(b"\r\n", 1)
            text = item.decode("ascii", errors="replace").strip()
            if text:
                output.append(SerialLine(timestamp, direction, text))
    output.sort(key=lambda line: line.timestamp)
    return output


def delta_ms(a: SerialLine, b: SerialLine) -> float:
    return round((b.timestamp - a.timestamp).total_seconds() * 1000.0, 3)


def find_next(lines: list[SerialLine], start: int, predicate) -> tuple[int, SerialLine] | None:
    for index in range(start, len(lines)):
        if predicate(lines[index]):
            return index, lines[index]
    return None


def analyze_cycles(lines: list[SerialLine]) -> list[CycleSummary]:
    summaries: list[CycleSummary] = []
    starts = [i for i, line in enumerate(lines) if line.direction == "TX" and line.text == ":START"]
    for cycle_index, start_index in enumerate(starts, 1):
        end_index = starts[cycle_index] if cycle_index < len(starts) else len(lines)
        segment = lines[start_index:end_index]
        state = TestCycleState()
        open_messages = 0
        other_messages = 0
        circuit_line: SerialLine | None = None
        circuit_value: int | None = None
        post_command: SerialLine | None = None
        for line in segment:
            if line.direction != "RX":
                continue
            event = parse_board_line(line.text)
            if event.family == "CLEAR":
                state.open_networks.clear()
                state.wrong_wiring.clear()
            elif event.family == "OPEN":
                open_messages += 1
                state.open_networks.update(event.values)
            elif event.family == "OTHER":
                other_messages += 1
                state.wrong_wiring.update(event.values)
            elif event.family == "CIRCUIT":
                circuit_line = line
                circuit_value = int(event.values[0])
                break
        if circuit_line:
            circuit_position = segment.index(circuit_line)
            for line in segment[circuit_position + 1:]:
                if line.direction == "TX" and line.text.startswith((":PASSPEN", ":UNCONNECT", ":STOP")):
                    post_command = line
                    break
        start_line = lines[start_index]
        summaries.append(CycleSummary(
            index=cycle_index,
            start=start_line.timestamp.strftime("%H:%M:%S.%f")[:-3],
            result="PASS" if circuit_value == 0 else ("FAIL" if circuit_value == 1 else "INCOMPLETE"),
            circuit_value=circuit_value,
            open_messages=open_messages,
            active_open_networks=len(state.open_networks.active),
            unresolved_open_links=state.open_count,
            other_messages=other_messages,
            wrong_pairs=len(state.wrong_wiring.pairs),
            duration_s=round((circuit_line.timestamp - start_line.timestamp).total_seconds(), 3) if circuit_line else None,
            post_result_command=post_command.text if post_command else None,
            post_result_delay_ms=delta_ms(circuit_line, post_command) if circuit_line and post_command else None,
        ))
    return summaries


def analyze_upload(lines: list[SerialLine]) -> dict:
    tx_model = next((i for i, x in enumerate(lines) if x.direction == "TX" and x.text.startswith(":MODEL,")), None)
    tx_finish = next((i for i, x in enumerate(lines) if x.direction == "TX" and x.text == ":FINISH"), None)
    result: dict = {}
    if tx_model is None or tx_finish is None:
        return result
    segment = lines[tx_model:tx_finish + 30]
    commands = [x for x in segment if x.direction == "TX" and x.text.startswith(":")]
    result["first_command"] = commands[0].text if commands else None
    result["upload_command_count_through_finish"] = sum(1 for x in commands if x.timestamp <= lines[tx_finish].timestamp)
    result["pindata_count"] = sum(x.text.startswith(":PINDATA,") for x in commands)
    result["array_count"] = sum(x.text.startswith(":ARRAY,") for x in commands)
    result["con_count"] = sum(x.text.startswith(":CON,") for x in commands)
    result["connector_count"] = sum(x.text.startswith(":CONNECTOR,") for x in commands)
    finish_ack = next((x for x in segment if x.direction == "RX" and x.text.startswith(":OK,FINISH")), None)
    reset = next((x for x in segment if x.direction == "TX" and x.text == ":RESET"), None)
    bootloader = next((x for x in segment if x.direction == "RX" and x.text in {"BootLoader", "BOOT"}), None)
    boot = next((x for x in segment if x.direction == "RX" and x.text == "BOOT"), None)
    first_start = None
    if boot:
        first_start = next((x for x in lines if x.timestamp > boot.timestamp and x.direction == "TX" and x.text == ":START"), None)
    result.update({
        "finish_ack": finish_ack.text if finish_ack else None,
        "finish_ack_ms": delta_ms(lines[tx_finish], finish_ack) if finish_ack else None,
        "reset_after_finish_ms": delta_ms(finish_ack, reset) if finish_ack and reset else None,
        "first_boot_response": bootloader.text if bootloader else None,
        "boot_to_first_start_ms": delta_ms(boot, first_start) if boot and first_start else None,
    })

    # Thống kê ACK của nhóm tải model theo thứ tự command/ACK kế tiếp.
    ack_delays: dict[str, list[float]] = {}
    for i, item in enumerate(segment):
        if item.direction != "TX":
            continue
        family = item.text.split(",", 1)[0]
        if family not in {":MODEL", ":PINCOUNT", ":PINDATA", ":ARRAYCOUNT", ":ARRAY", ":CONCOUNT", ":CON", ":CONNECTORCOUNT", ":CONNECTOR", ":FINISH"}:
            continue
        ack = next((x for x in segment[i + 1:] if x.direction == "RX" and x.text.startswith(":OK")), None)
        if ack:
            ack_delays.setdefault(family[1:], []).append(delta_ms(item, ack))
    result["ack_ms"] = {
        family: {
            "count": len(values),
            "min": round(min(values), 3),
            "median": round(median(values), 3),
            "max": round(max(values), 3),
        }
        for family, values in ack_delays.items()
    }
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("trace", type=Path, help="UART_SERIAL_ALL.txt")
    parser.add_argument("--fd", type=int, default=19)
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()
    lines = extract_serial_lines(args.trace, args.fd)
    payload = {
        "serial_line_count": len(lines),
        "upload": analyze_upload(lines),
        "cycles": [asdict(item) for item in analyze_cycles(lines)],
    }
    text = json.dumps(payload, ensure_ascii=False, indent=2)
    if args.json:
        args.json.write_text(text + "\n", encoding="utf-8")
    print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
