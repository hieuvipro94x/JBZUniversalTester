from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


@dataclass(frozen=True)
class Expectation:
    mode: str
    value: str
    timeout: float = 2.0

    def matches(self, line: str) -> bool:
        if self.mode == "exact":
            return line == self.value
        if self.mode == "prefix":
            return line.startswith(self.value)
        if self.mode == "contains":
            return self.value in line
        raise ValueError(f"Kiểu expect không hỗ trợ: {self.mode}")


@dataclass(frozen=True)
class ProtocolCommand:
    tx: str
    expect: Expectation
    source: str = "model"
    note: str = ""

    @property
    def family(self) -> str:
        body = self.tx.lstrip(":*")
        return body.split(",", 1)[0].split("?", 1)[0].upper()


@dataclass
class ProtocolProfile:
    model_name: str
    commands: list[ProtocolCommand]
    source_path: Path | None = None
    metadata: dict[str, Any] = field(default_factory=dict)
    warnings: list[str] = field(default_factory=list)

    @property
    def finish_command(self) -> ProtocolCommand | None:
        for command in reversed(self.commands):
            if command.tx == ":FINISH":
                return command
        return None


@dataclass
class SetupProfile:
    name: str
    values: dict[str, Any] = field(default_factory=dict)
    board_commands: list[ProtocolCommand] = field(default_factory=list)
    source_path: Path | None = None
    warnings: list[str] = field(default_factory=list)


@dataclass
class UploadResult:
    model_name: str
    finish_response: str
    verified_model_response: str | None
    elapsed_seconds: float
