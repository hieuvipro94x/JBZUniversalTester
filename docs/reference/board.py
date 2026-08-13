from __future__ import annotations

import queue
import threading
import time
from collections import defaultdict
from collections.abc import Callable
from datetime import datetime
from pathlib import Path

import serial

from .protocol import BoardEvent, parse_board_line


class BoardController:
    """Một kết nối UART duy nhất cho chế độ vận hành.

    Reader chạy riêng và mọi lệnh ghi được khóa để không chồng byte. Các lệnh
    có phản hồi chờ được đặt trong ``_transaction_lock``. MAXEXT chỉ được gửi
    đúng một lần cho mỗi START, ngay sau khi nhận MEASURE như trace máy gốc.
    """

    def __init__(
        self,
        port: str,
        baudrate: int,
        log_dir: Path,
        event_callback: Callable[[BoardEvent], None],
        log_callback: Callable[[str], None] | None = None,
        disconnect_callback: Callable[[Exception | None], None] | None = None,
    ) -> None:
        self.port = port
        self.baudrate = baudrate
        self.log_dir = log_dir
        self.event_callback = event_callback
        self.log_callback = log_callback
        self.disconnect_callback = disconnect_callback
        self.serial: serial.Serial | None = None
        self._stop = threading.Event()
        self._reader: threading.Thread | None = None
        self._write_lock = threading.Lock()
        self._transaction_lock = threading.Lock()
        self._queues: dict[str, queue.Queue[BoardEvent]] = defaultdict(queue.Queue)
        self._log_file = None
        self.auto_maxext: int | None = None
        self._maxext_sent = False
        self._intentional_disconnect = False

    @property
    def connected(self) -> bool:
        return bool(self.serial and self.serial.is_open)

    def _log(self, direction: str, text: str) -> None:
        line = f"[{datetime.now().strftime('%H:%M:%S.%f')[:-3]}] {direction} {text}"
        if self._log_file:
            self._log_file.write(line + "\n")
            self._log_file.flush()
        if self.log_callback:
            self.log_callback(line)

    def connect(
        self, identity_hint: tuple[str, str | None] | None = None
    ) -> tuple[str, str | None]:
        if self.connected:
            return identity_hint or self.query_identity()
        self.log_dir.mkdir(parents=True, exist_ok=True)
        self._log_file = (
            self.log_dir / f"tester_{datetime.now():%Y%m%d_%H%M%S}.log"
        ).open("a", encoding="utf-8")
        self.serial = serial.Serial(
            self.port,
            self.baudrate,
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
        self._intentional_disconnect = False
        self._stop.clear()
        self._reader = threading.Thread(target=self._reader_loop, daemon=True)
        self._reader.start()
        self._log("INFO", f"OPEN {self.port} {self.baudrate} 8N1")
        return identity_hint or self.query_identity()

    def disconnect(self) -> None:
        self._intentional_disconnect = True
        self._stop.set()
        if self._reader and self._reader.is_alive():
            self._reader.join(timeout=1.0)
        if self.serial and self.serial.is_open:
            self.serial.close()
            self._log("INFO", "CLOSE")
        self.serial = None
        self.auto_maxext = None
        self._maxext_sent = False
        if self._log_file:
            self._log_file.close()
            self._log_file = None

    def send_line(self, command: str) -> None:
        if not self.serial or not self.serial.is_open:
            raise RuntimeError("Chưa kết nối bo")
        payload = command.rstrip("\r\n").encode("ascii") + b"\r\n"
        with self._write_lock:
            self.serial.write(payload)
            self.serial.flush()
        self._log("TX", command)

    def _reader_loop(self) -> None:
        while not self._stop.is_set():
            try:
                if not self.serial:
                    return
                raw = self.serial.readline()
                if not raw:
                    continue
                text = raw.decode("ascii", errors="replace").strip("\r\n")
                self._log("RX", text)
                event = parse_board_line(text)
                self._queues[event.family].put(event)

                # Trace thật: bo gửi START,ON + MEASURE, sau đó PC mới gửi MAXEXT.
                if (
                    event.family == "MEASURE"
                    and self.auto_maxext is not None
                    and not self._maxext_sent
                ):
                    self._maxext_sent = True
                    self.send_line(f":MAXEXT,{self.auto_maxext}")

                self.event_callback(event)
            except serial.SerialException as exc:
                self._log("ERROR", f"SERIAL {exc}")
                self._stop.set()
                try:
                    if self.serial and self.serial.is_open:
                        self.serial.close()
                except Exception:
                    pass
                if not self._intentional_disconnect and self.disconnect_callback:
                    try:
                        self.disconnect_callback(exc)
                    except Exception as callback_exc:
                        self._log("ERROR", f"DISCONNECT CALLBACK {callback_exc}")
            except Exception as exc:  # không để reader chết vì callback/parser
                self._log("ERROR", f"READER {exc}")

    def _drain(self, family: str) -> None:
        q = self._queues[family]
        while True:
            try:
                q.get_nowait()
            except queue.Empty:
                return

    def _wait(self, family: str, timeout: float, predicate=None) -> BoardEvent:
        deadline = time.monotonic() + timeout
        q = self._queues[family]
        while time.monotonic() < deadline:
            try:
                event = q.get(
                    timeout=min(0.1, max(0.01, deadline - time.monotonic()))
                )
            except queue.Empty:
                continue
            if predicate is None or predicate(event):
                return event
        raise TimeoutError(f"Timeout chờ {family}")

    def query_identity(self) -> tuple[str, str | None]:
        with self._transaction_lock:
            self._drain("IDN")
            self.send_line("*IDN?")
            idn = self._wait("IDN", 2.0).raw
            self._drain("MODELNAME")
            self.send_line(":MODELNAME?")
            model = None
            try:
                model = self._wait("MODELNAME", 2.0).raw
            except TimeoutError:
                pass
            return idn, model

    def start_test(self, maxext: int = 0) -> None:
        self.auto_maxext = int(maxext)
        self._maxext_sent = False
        self.send_line(":START")

    def stop_test(self, wait_ack: bool = False, timeout: float = 1.0) -> bool:
        self.auto_maxext = None
        self._maxext_sent = False
        if not wait_ack:
            self.send_line(":STOP")
            return True
        with self._transaction_lock:
            self._drain("STOP")
            self.send_line(":STOP")
            try:
                self._wait("STOP", timeout)
                return True
            except TimeoutError:
                self._log("WARN", "Không nhận :STOP ACK trước timeout")
                return False

    def pass_pen(self, delay_ms: int, pin_count: int) -> None:
        self.send_line(f":PASSPEN,{int(delay_ms)},{int(pin_count)}")

    def unconnect(self, delay_ms: int, pin_count: int) -> None:
        self.send_line(f":UNCONNECT,{int(delay_ms)},{int(pin_count)}")

    def input_test(self, channel: int, wait_ms: int = 200) -> BoardEvent:
        with self._transaction_lock:
            self._drain("INPUT")
            self.send_line(f":INPUTTEST,{channel},{wait_ms}")
            return self._wait(
                "INPUT", 1.0, lambda e: e.values and e.values[0] == channel
            )

    def output_test(self, channel: int, state: bool) -> BoardEvent:
        expected = "ON" if state else "OFF"
        with self._transaction_lock:
            self._drain("OUTPUT")
            self.send_line(f":OUTPUTTEST,{channel},{1 if state else 0}")
            return self._wait(
                "OUTPUT",
                1.2,
                lambda e: e.values
                and e.values[0] == channel
                and str(e.values[1]).upper() == expected,
            )

    def measure(self, family: str, channel: int) -> BoardEvent:
        family = family.upper()
        if family not in {"RESISTOR", "AMPARE", "VOLTAGE"}:
            raise ValueError(family)
        with self._transaction_lock:
            self._drain(family)
            self.send_line(f":{family}TEST,{channel},200,1,0")
            return self._wait(family, 1.5)

    def all_outputs_off(self) -> None:
        for channel in range(5):
            try:
                self.output_test(channel, False)
            except Exception as exc:
                self._log("WARN", f"OUTPUT {channel} OFF: {exc}")
