from __future__ import annotations

import sys
import time
import types

try:
    import serial  # noqa: F401
except ModuleNotFoundError:
    serial_module = types.ModuleType("serial")
    serial_module.EIGHTBITS = 8
    serial_module.PARITY_NONE = "N"
    serial_module.STOPBITS_ONE = 1
    serial_module.SerialException = OSError
    serial_module.Serial = object
    tools_module = types.ModuleType("serial.tools")
    list_ports_module = types.ModuleType("serial.tools.list_ports")
    list_ports_module.comports = lambda: []
    tools_module.list_ports = list_ports_module
    serial_module.tools = tools_module
    sys.modules["serial"] = serial_module
    sys.modules["serial.tools"] = tools_module
    sys.modules["serial.tools.list_ports"] = list_ports_module

from jbz_uart.manager import ProbeResult, UartManager


def test_cached_uart_is_used_first(monkeypatch):
    calls = []
    monkeypatch.setattr("jbz_uart.manager.os.path.exists", lambda path: True)

    def fake_probe(port, baudrate, idn_timeout, model_timeout, stop_event=None):
        calls.append(port)
        return ProbeResult(port, "Universal Tester New V 1.3", ":MODELNAME,ABC,1", 0.01)

    monkeypatch.setattr("jbz_uart.manager.probe_jbz_port", fake_probe)
    remembered = []
    result = UartManager(preferred_port="/dev/ttyAMA4", on_port_found=remembered.append).discover()

    assert result.port == "/dev/ttyAMA4"
    assert calls == ["/dev/ttyAMA4"]
    assert remembered == ["/dev/ttyAMA4"]


def test_parallel_scan_selects_first_valid_board(monkeypatch):
    monkeypatch.setattr("jbz_uart.manager.os.path.exists", lambda path: False)
    monkeypatch.setattr(
        "jbz_uart.manager.candidate_ports",
        lambda preferred="": ["/dev/ttyAMA0", "/dev/ttyAMA1", "/dev/ttyS0"],
    )

    def fake_probe(port, *_args, **_kwargs):
        if port == "/dev/ttyAMA1":
            time.sleep(0.01)
            return ProbeResult(port, "Universal Tester V 1.19 Beta II", ":MODELNAME,X,4", 0.01)
        time.sleep(0.03)
        return None

    monkeypatch.setattr("jbz_uart.manager.probe_jbz_port", fake_probe)
    result = UartManager(probe_timeout=0.1).discover()
    assert result.port == "/dev/ttyAMA1"
    assert result.model_response == ":MODELNAME,X,4"


def test_manager_raises_clear_error_when_no_ports(monkeypatch):
    monkeypatch.setattr("jbz_uart.manager.os.path.exists", lambda path: False)
    monkeypatch.setattr("jbz_uart.manager.candidate_ports", lambda preferred="": [])

    try:
        UartManager().discover()
    except Exception as exc:
        assert "Không có cổng serial" in str(exc)
    else:
        raise AssertionError("Phải báo lỗi khi hệ thống không có cổng serial")


def test_manager_remembers_parallel_scan_result(monkeypatch):
    monkeypatch.setattr("jbz_uart.manager.os.path.exists", lambda path: False)
    monkeypatch.setattr("jbz_uart.manager.candidate_ports", lambda preferred="": ["/dev/serial0"])
    monkeypatch.setattr(
        "jbz_uart.manager.probe_jbz_port",
        lambda port, *_args, **_kwargs: ProbeResult(
            port, "Universal Tester New V 1.3", ":MODELNAME,WH322110,102", 0.02
        ),
    )
    saved = []
    manager = UartManager(on_port_found=saved.append)
    result = manager.discover()
    assert saved == ["/dev/serial0"]
    assert manager.preferred_port == result.port
