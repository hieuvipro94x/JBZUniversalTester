from __future__ import annotations

import glob
import os
import re
import sys
import threading
import time
from concurrent.futures import FIRST_COMPLETED, Future, ThreadPoolExecutor, wait
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Iterable

import serial
from serial.tools import list_ports


IS_WINDOWS = sys.platform.startswith("win")


class UartDiscoveryError(RuntimeError):
    """Không tìm thấy bo JBZ trên bất kỳ cổng UART khả dụng nào."""


@dataclass(frozen=True)
class ProbeResult:
    port: str
    idn: str
    model_response: str | None
    elapsed_seconds: float


def _stable_key(path: str) -> tuple[int, int, str]:
    """Ưu tiên cổng ổn định trên từng hệ điều hành."""

    name = Path(path).name
    if IS_WINDOWS:
        match = re.fullmatch(r"COM(\d+)", path.upper())
        return (0, int(match.group(1)) if match else 9999, path.upper())

    if path == "/dev/serial0":
        priority = 0
    elif path == "/dev/serial1":
        priority = 1
    elif name.startswith("ttyAMA"):
        priority = 2
    elif name.startswith("ttyUSB"):
        priority = 3
    elif name.startswith("ttyACM"):
        priority = 4
    elif name.startswith("ttyS"):
        priority = 5
    else:
        priority = 9
    return (priority, 0, path)


def _deduplicate_aliases(paths: Iterable[str]) -> list[str]:
    """Loại cổng trùng trên Linux; Windows chỉ cần khử tên COM trùng."""

    result: list[str] = []
    if IS_WINDOWS:
        seen: set[str] = set()
        for path in paths:
            key = path.upper()
            if not path or key in seen:
                continue
            seen.add(key)
            result.append(path)
        return result

    seen_targets: set[str] = set()
    for path in paths:
        if not path or not os.path.exists(path):
            continue
        try:
            target = os.path.realpath(path)
        except OSError:
            target = path
        if target in seen_targets:
            continue
        seen_targets.add(target)
        result.append(path)
    return result


def _listed_ports() -> list[str]:
    try:
        return [port.device for port in list_ports.comports() if port.device]
    except Exception:
        return []


def candidate_ports(preferred_port: str = "") -> list[str]:
    """Liệt kê tất cả cổng serial có thể là bo JBZ trên Windows/Linux."""

    discovered: set[str] = set(_listed_ports())

    if not IS_WINDOWS:
        patterns = (
            "/dev/serial0",
            "/dev/serial1",
            "/dev/ttyAMA*",
            "/dev/ttyUSB*",
            "/dev/ttyACM*",
            "/dev/ttyS*",
        )
        for pattern in patterns:
            if "*" in pattern:
                discovered.update(glob.glob(pattern))
            elif os.path.exists(pattern):
                discovered.add(pattern)

    ordered: list[str] = []
    preferred_port = preferred_port.strip()
    if preferred_port:
        if IS_WINDOWS:
            # COMx không phải đường dẫn file nên không được kiểm tra bằng exists().
            if preferred_port.upper() in {p.upper() for p in discovered}:
                ordered.append(preferred_port)
        elif os.path.exists(preferred_port):
            ordered.append(preferred_port)

    ordered.extend(
        sorted(
            (p for p in discovered if not preferred_port or p.upper() != preferred_port.upper()),
            key=_stable_key,
        )
    )
    return _deduplicate_aliases(ordered)


def _preferred_port_can_be_tried(port: str) -> bool:
    if not port:
        return False
    if IS_WINDOWS:
        # Thử trực tiếp cache COM; Serial() sẽ trả lỗi nhanh nếu thiết bị đã mất.
        return bool(re.fullmatch(r"COM\d+", port.strip(), flags=re.IGNORECASE))
    return os.path.exists(port)


def _read_matching_line(
    uart: serial.Serial,
    deadline: float,
    predicate: Callable[[str], bool],
) -> str | None:
    while time.monotonic() < deadline:
        raw = uart.readline()
        if not raw:
            continue
        text = raw.decode("ascii", errors="replace").strip("\r\n\x00")
        if predicate(text):
            return text
    return None


def _is_jbz_identity(text: str) -> bool:
    """Nhận cả firmware ghi ``Universal Tester`` và ``UniversalTester``."""

    normalized = "".join(str(text or "").lower().split())
    return "universaltester" in normalized


def probe_jbz_port(
    port: str,
    baudrate: int = 115200,
    idn_timeout: float = 0.28,
    model_timeout: float = 0.20,
    stop_event: threading.Event | None = None,
) -> ProbeResult | None:
    """Mở tạm một cổng và xác nhận đúng bo JBZ bằng hai bước.

    Trên Windows, USB-UART có thể cần thêm thời gian sau ``CreateFile`` trước
    khi byte đầu tiên thực sự đi qua driver. Vì vậy timeout IDN tối thiểu được
    nới rộng và lệnh ``*IDN?`` được thử lại một lần mà không thay đổi protocol.
    """

    started = time.monotonic()
    if stop_event and stop_event.is_set():
        return None
    try:
        with serial.Serial(
            port=port,
            baudrate=int(baudrate),
            bytesize=serial.EIGHTBITS,
            parity=serial.PARITY_NONE,
            stopbits=serial.STOPBITS_ONE,
            timeout=0.025,
            write_timeout=0.15,
            xonxoff=False,
            rtscts=False,
            dsrdtr=False,
        ) as uart:
            try:
                uart.reset_input_buffer()
                uart.reset_output_buffer()
            except serial.SerialException:
                return None

            # USB-TTL/USB-Serial trên Windows có thể chưa sẵn sàng ngay sau open().
            # Không áp dụng delay này trên Raspberry Pi/Linux để giữ tốc độ cũ.
            if IS_WINDOWS:
                time.sleep(0.12)

            if stop_event and stop_event.is_set():
                return None

            effective_idn_timeout = max(
                1.80 if IS_WINDOWS else 0.05,
                float(idn_timeout),
            )
            idn = None
            idn_deadline = time.monotonic() + effective_idn_timeout
            for attempt in range(2):
                uart.write(b"*IDN?\r\n")
                uart.flush()
                remaining = max(0.0, idn_deadline - time.monotonic())
                if remaining <= 0:
                    break
                # Lần đầu chờ phần lớn budget; lần hai dùng toàn bộ thời gian còn lại.
                slice_seconds = remaining if attempt else max(0.35, remaining * 0.65)
                idn = _read_matching_line(
                    uart,
                    min(idn_deadline, time.monotonic() + slice_seconds),
                    _is_jbz_identity,
                )
                if idn:
                    break
                if stop_event and stop_event.is_set():
                    return None
            if not idn:
                return None

            model_response = None
            if not stop_event or not stop_event.is_set():
                uart.write(b":MODELNAME?\r\n")
                uart.flush()
                model_response = _read_matching_line(
                    uart,
                    time.monotonic() + max(0.05, float(model_timeout)),
                    lambda text: text.startswith(":MODELNAME,"),
                )

            return ProbeResult(
                port=port,
                idn=idn,
                model_response=model_response,
                elapsed_seconds=time.monotonic() - started,
            )
    except (OSError, serial.SerialException, ValueError):
        return None


class UartManager:
    """Tìm cổng JBZ nhanh, ưu tiên cache và quét song song khi cần."""

    def __init__(
        self,
        baudrate: int = 115200,
        preferred_port: str = "",
        on_port_found: Callable[[str], None] | None = None,
        log_callback: Callable[[str], None] | None = None,
        probe_timeout: float = 0.28,
    ) -> None:
        self.baudrate = int(baudrate)
        self.preferred_port = preferred_port.strip()
        self.on_port_found = on_port_found
        self.log_callback = log_callback
        self.probe_timeout = max(
            1.80 if IS_WINDOWS else 0.08,
            float(probe_timeout),
        )

    def _log(self, text: str) -> None:
        if self.log_callback:
            self.log_callback(text)

    def _remember(self, result: ProbeResult) -> ProbeResult:
        self.preferred_port = result.port
        if self.on_port_found:
            self.on_port_found(result.port)
        self._log(
            f"UART FOUND {result.port} in {result.elapsed_seconds * 1000:.0f} ms | "
            f"{result.idn} | {result.model_response or 'NO MODELNAME'}"
        )
        return result

    def discover(self) -> ProbeResult:
        started = time.monotonic()

        if self.preferred_port and _preferred_port_can_be_tried(self.preferred_port):
            self._log(f"UART FAST TRY {self.preferred_port}")
            cached = probe_jbz_port(
                self.preferred_port,
                self.baudrate,
                idn_timeout=self.probe_timeout if IS_WINDOWS else min(0.24, self.probe_timeout),
                model_timeout=0.35 if IS_WINDOWS else 0.16,
            )
            if cached:
                return self._remember(cached)

        ports = candidate_ports(self.preferred_port)
        if not ports:
            if IS_WINDOWS:
                raise UartDiscoveryError(
                    "Không có cổng COM nào. Kiểm tra cáp USB-UART, driver USB Serial "
                    "và Device Manager > Ports (COM & LPT)."
                )
            raise UartDiscoveryError(
                "Không có cổng serial nào trong /dev. Kiểm tra overlay UART, "
                "dây kết nối và quyền nhóm dialout."
            )

        self._log("UART PARALLEL SCAN " + ", ".join(ports))
        stop_event = threading.Event()
        executor = ThreadPoolExecutor(max_workers=min(8, max(1, len(ports))))
        futures: dict[Future[ProbeResult | None], str] = {
            executor.submit(
                probe_jbz_port,
                port,
                self.baudrate,
                self.probe_timeout,
                0.35 if IS_WINDOWS else 0.18,
                stop_event,
            ): port
            for port in ports
        }
        winner: ProbeResult | None = None
        try:
            pending = set(futures)
            absolute_deadline = time.monotonic() + self.probe_timeout + (0.70 if IS_WINDOWS else 0.30)
            while pending and time.monotonic() < absolute_deadline:
                done, pending = wait(
                    pending,
                    timeout=max(0.01, absolute_deadline - time.monotonic()),
                    return_when=FIRST_COMPLETED,
                )
                for future in done:
                    try:
                        result = future.result()
                    except Exception as exc:
                        self._log(f"UART PROBE ERROR {futures[future]}: {exc}")
                        continue
                    if result:
                        winner = result
                        stop_event.set()
                        for other in pending:
                            other.cancel()
                        break
                if winner:
                    break
        finally:
            executor.shutdown(wait=False)

        if winner:
            total_ms = (time.monotonic() - started) * 1000
            self._log(f"UART SCAN COMPLETE {total_ms:.0f} ms")
            return self._remember(winner)

        checked = ", ".join(ports)
        raise UartDiscoveryError(
            "Không tìm thấy bo Universal Tester. Đã thử: " + checked
        )
