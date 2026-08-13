from __future__ import annotations

from jbz_uart import manager


def test_windows_candidate_ports_keep_cached_com_first(monkeypatch):
    monkeypatch.setattr(manager, "IS_WINDOWS", True)
    monkeypatch.setattr(manager, "_listed_ports", lambda: ["COM7", "COM3", "COM11"])
    assert manager.candidate_ports("COM11") == ["COM11", "COM3", "COM7"]


def test_windows_cached_com_is_probeable_without_file_exists(monkeypatch):
    monkeypatch.setattr(manager, "IS_WINDOWS", True)
    monkeypatch.setattr(manager.os.path, "exists", lambda _path: False)
    assert manager._preferred_port_can_be_tried("COM9") is True
    assert manager._preferred_port_can_be_tried("/dev/ttyAMA0") is False


def test_identity_accepts_spaced_and_compact_firmware_names():
    assert manager._is_jbz_identity("Universal Tester New V 1.3") is True
    assert manager._is_jbz_identity("UniversalTester V 1.19") is True
    assert manager._is_jbz_identity("Other Tester") is False


def test_windows_manager_enforces_usb_uart_safe_probe_timeout(monkeypatch):
    monkeypatch.setattr(manager, "IS_WINDOWS", True)
    uart = manager.UartManager(probe_timeout=0.28)
    assert uart.probe_timeout >= 1.8
