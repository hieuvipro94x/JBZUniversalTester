from __future__ import annotations

import time

import serial
from serial.tools import list_ports

from jbz_uart.manager import _is_jbz_identity


def _vidpid(item) -> str:
    if item.vid is None or item.pid is None:
        return "N/A"
    return f"{item.vid:04X}:{item.pid:04X}"


def inspect_port(device: str, baudrate: int = 115200) -> tuple[bool, list[str]]:
    lines: list[str] = []
    try:
        with serial.Serial(
            port=device,
            baudrate=baudrate,
            bytesize=serial.EIGHTBITS,
            parity=serial.PARITY_NONE,
            stopbits=serial.STOPBITS_ONE,
            timeout=0.08,
            write_timeout=1.0,
            xonxoff=False,
            rtscts=False,
            dsrdtr=False,
        ) as uart:
            try:
                uart.reset_input_buffer()
                uart.reset_output_buffer()
            except serial.SerialException:
                pass

            # Cho driver USB-UART Windows ổn định sau khi mở cổng.
            time.sleep(0.20)
            found = False
            for attempt in range(1, 3):
                uart.write(b"*IDN?\r\n")
                uart.flush()
                lines.append(f"TX[{attempt}] *IDN?")
                deadline = time.monotonic() + 1.8
                while time.monotonic() < deadline:
                    raw = uart.readline()
                    if not raw:
                        continue
                    text = raw.decode("ascii", errors="replace").strip("\r\n\x00")
                    lines.append(f"RX {text!r}")
                    if _is_jbz_identity(text):
                        found = True
                        uart.write(b":MODELNAME?\r\n")
                        uart.flush()
                        lines.append("TX :MODELNAME?")
                        model_deadline = time.monotonic() + 0.8
                        while time.monotonic() < model_deadline:
                            model_raw = uart.readline()
                            if not model_raw:
                                continue
                            model = model_raw.decode("ascii", errors="replace").strip("\r\n\x00")
                            lines.append(f"RX {model!r}")
                            if model.startswith(":MODELNAME,"):
                                break
                        return True, lines
            return found, lines
    except serial.SerialException as exc:
        return False, [f"SERIAL ERROR: {exc}"]
    except OSError as exc:
        return False, [f"OS ERROR: {exc}"]
    except Exception as exc:
        return False, [f"ERROR: {type(exc).__name__}: {exc}"]


def main() -> int:
    ports = list(list_ports.comports())
    if not ports:
        print("KHONG THAY CONG COM NAO.")
        print("1) Kiem tra Device Manager > Ports (COM & LPT).")
        print("2) Cai dung driver CH340/CP210x/FTDI neu USB-TTL can driver.")
        print("3) Thu doi cap USB/doi cong USB.")
        return 1

    print("=== DANH SACH COM WINDOWS ===")
    for item in ports:
        print(
            f"- {item.device}: {item.description} | "
            f"VID:PID={_vidpid(item)} | HWID={item.hwid}"
        )

    print("\n=== KIEM TRA JBZ @ 115200 8N1 ===")
    found_ports: list[str] = []
    for item in ports:
        print(f"\n[{item.device}] {item.description}")
        ok, trace = inspect_port(item.device, 115200)
        for line in trace:
            print("  " + line)
        if ok:
            found_ports.append(item.device)
            print("  => FOUND JBZ UNIVERSAL TESTER")
        elif any("Access is denied" in line or "PermissionError" in line for line in trace):
            print("  => COM DANG BI PHAN MEM KHAC GIU. Dong PuTTY/TeraTerm/Arduino/Serial Monitor.")
        elif not any(line.startswith("RX ") for line in trace):
            print("  => MO DUOC COM NHUNG KHONG CO RX. Kiem tra TX/RX cheo va GND chung.")
        else:
            print("  => CO RX NHUNG KHONG DUNG IDN UNIVERSAL TESTER.")

    print("\n=== KET LUAN ===")
    if found_ports:
        print("Tim thay bo tai: " + ", ".join(found_ports))
        return 0

    print("Khong tim thay bo JBZ.")
    print("Neu COM mo duoc nhung khong co RX: noi USB-TTL TX -> BO RX, USB-TTL RX -> BO TX, GND -> GND.")
    print("Khong noi 5V cua USB-TTL vao chan 3.3V UART cua bo neu bo da duoc cap nguon rieng.")
    print("Muc TX idle cua USB-TTL phai phu hop logic UART cua bo; neu chi ~1.2V thi can kiem tra lai adapter/che do dien ap.")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
