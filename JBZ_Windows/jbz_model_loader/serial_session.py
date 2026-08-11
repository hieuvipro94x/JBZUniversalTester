from __future__ import annotations

import threading
import time
from collections.abc import Callable
from datetime import datetime
from pathlib import Path

import serial

from jbz_uart import UartManager

from .models import Expectation, ProtocolProfile, UploadResult


class CancelledError(RuntimeError):
    pass


class ProtocolError(RuntimeError):
    pass




class LineLogger:
    def __init__(self, path: Path | None = None, callback: Callable[[str], None] | None = None):
        self.path = path
        self.callback = callback
        self._file = None
        if path:
            path.parent.mkdir(parents=True, exist_ok=True)
            self._file = path.open("a", encoding="utf-8")

    def write(self, direction: str, text: str) -> None:
        line = f"[{datetime.now().strftime('%H:%M:%S.%f')[:-3]}] {direction} {text}"
        if self._file:
            self._file.write(line + "\n")
            self._file.flush()
        if self.callback:
            self.callback(line)

    def close(self) -> None:
        if self._file:
            self._file.close()
            self._file = None


class SerialSession:
    def __init__(
        self,
        port: str | None = None,
        baudrate: int = 115200,
        logger: LineLogger | None = None,
        cancel_event: threading.Event | None = None,
        preferred_port: str = "",
        on_port_found: Callable[[str], None] | None = None,
        probe_timeout: float = 0.28,
    ):
        self.port = (port or "").strip()
        self.preferred_port = preferred_port.strip()
        self.on_port_found = on_port_found
        self.probe_timeout = max(0.08, float(probe_timeout))
        self.baudrate = baudrate
        self.logger = logger or LineLogger()
        self.cancel_event = cancel_event or threading.Event()
        self.serial: serial.Serial | None = None

    def open(self) -> None:
        if not self.port or self.port.lower() == "auto":
            manager = UartManager(
                baudrate=self.baudrate,
                preferred_port=self.preferred_port,
                on_port_found=self.on_port_found,
                log_callback=lambda line: self.logger.write("INFO", line),
                probe_timeout=self.probe_timeout,
            )
            result = manager.discover()
            self.port = result.port
            self.preferred_port = result.port
        self.serial = serial.Serial(
            port=self.port,
            baudrate=self.baudrate,
            bytesize=serial.EIGHTBITS,
            parity=serial.PARITY_NONE,
            stopbits=serial.STOPBITS_ONE,
            timeout=0.1,
            write_timeout=2.0,
            xonxoff=False,
            rtscts=False,
            dsrdtr=False,
        )
        self.serial.reset_input_buffer()
        self.serial.reset_output_buffer()
        self.logger.write("INFO", f"OPEN {self.port} {self.baudrate} 8N1")

    def close(self) -> None:
        if self.serial and self.serial.is_open:
            self.serial.close()
            self.logger.write("INFO", "CLOSE")
        self.serial = None

    def __enter__(self) -> "SerialSession":
        self.open()
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        self.close()

    def _check_cancel(self) -> None:
        if self.cancel_event.is_set():
            raise CancelledError("Người dùng đã hủy")

    def send_line(self, text: str) -> None:
        self._check_cancel()
        if not self.serial:
            raise RuntimeError("Serial chưa mở")
        payload = text.rstrip("\r\n").encode("ascii") + b"\r\n"
        self.serial.write(payload)
        self.serial.flush()
        self.logger.write("TX", text)

    def read_line(self, deadline: float) -> str | None:
        if not self.serial:
            raise RuntimeError("Serial chưa mở")
        while time.monotonic() < deadline:
            self._check_cancel()
            raw = self.serial.readline()
            if not raw:
                continue
            text = raw.decode("ascii", errors="replace").strip("\r\n")
            self.logger.write("RX", text)
            return text
        return None

    def send_expect(self, tx: str, expect: Expectation, retries: int = 0) -> str:
        last_lines: list[str] = []
        for attempt in range(retries + 1):
            self.send_line(tx)
            deadline = time.monotonic() + expect.timeout
            while time.monotonic() < deadline:
                line = self.read_line(deadline)
                if line is None:
                    break
                last_lines.append(line)
                if line.startswith(":NAK") or line.startswith(":ERROR"):
                    raise ProtocolError(f"Bo báo lỗi sau {tx}: {line}")
                if expect.matches(line):
                    return line
            if attempt < retries:
                self.logger.write("WARN", f"Timeout {tx}; thử lại {attempt + 2}/{retries + 1}")
        tail = " | ".join(last_lines[-5:]) or "không có RX"
        raise ProtocolError(
            f"Timeout/sai ACK cho {tx}; cần {expect.mode} '{expect.value}'; RX cuối: {tail}"
        )

    def handshake(self) -> tuple[str, str | None]:
        idn = self.send_expect("*IDN?", Expectation("prefix", "Universal Tester", 1.8))
        self.send_line(":MODELNAME?")
        deadline = time.monotonic() + 1.8
        model_line = None
        while time.monotonic() < deadline:
            line = self.read_line(deadline)
            if line is None:
                break
            if line.startswith(":MODELNAME,"):
                model_line = line
                break
        return idn, model_line

    def upload_profile(
        self,
        profile: ProtocolProfile,
        progress: Callable[[int, int, str], None] | None = None,
        reset_after: bool = True,
        verify_after: bool = True,
        retries: int = 0,
    ) -> UploadResult:
        started = time.monotonic()
        self.handshake()
        finish_response = ""
        total = len(profile.commands)
        for index, command in enumerate(profile.commands, 1):
            self._check_cancel()
            if progress:
                progress(index - 1, total, command.tx)
            response = self.send_expect(command.tx, command.expect, retries=retries)
            if command.tx == ":FINISH":
                finish_response = response
            if progress:
                progress(index, total, command.tx)

        verified = None
        if reset_after:
            self.send_line(":RESET")
            deadline = time.monotonic() + 3.0
            saw_bootloader = False
            while time.monotonic() < deadline:
                line = self.read_line(deadline)
                if line is None:
                    break
                if line == "BootLoader":
                    saw_bootloader = True
                    self.send_line(":STOP")
                if line == "BOOT":
                    break
            if not saw_bootloader:
                self.logger.write("WARN", "Không thấy 'BootLoader' sau :RESET; tiếp tục")

        if verify_after:
            # Firmware cần thời gian khởi động lại. Trong ứng dụng Production,
            # verify_after=False và phiên UART test sẽ mở lại sau khoảng 2,5 s,
            # đúng trace máy gốc. Nhánh này giữ cho CLI độc lập vẫn an toàn.
            self.close()
            time.sleep(2.5)
            self.open()
            _idn, verified = self.handshake()
            if not verified:
                raise ProtocolError("Nạp xong nhưng không verify được :MODELNAME?")
            parts = verified.split(",")
            if len(parts) < 2 or parts[1] != profile.model_name:
                raise ProtocolError(
                    f"Verify model không khớp: mong {profile.model_name}, nhận {verified}"
                )

        return UploadResult(
            model_name=profile.model_name,
            finish_response=finish_response,
            verified_model_response=verified,
            elapsed_seconds=time.monotonic() - started,
        )
