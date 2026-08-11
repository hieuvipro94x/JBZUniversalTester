from jbz_tester.fault_table import FaultRow, fault_priority


def test_wrong_wiring_has_highest_priority():
    rows = [
        FaultRow("Hở mạch", "1", "1", "1", "L1"),
        FaultRow("Đấu sai S", "2", "1", "2", "L2", style="wrong"),
        FaultRow("Đấu sai E", "3", "1", "3", "L3", style="wrong"),
    ]
    ordered = sorted(rows, key=fault_priority)
    assert [row.kind for row in ordered] == ["Đấu sai S", "Đấu sai E", "Hở mạch"]


def test_probe_row_is_after_wrong_wiring_but_before_open():
    rows = [
        FaultRow("Hở mạch", "1", "1", "1", "L1"),
        FaultRow("Đầu dò", "2", "1", "2", "L2", style="probe"),
        FaultRow("Đấu sai S", "3", "1", "3", "L3", style="wrong"),
    ]
    ordered = sorted(rows, key=fault_priority)
    assert [row.kind for row in ordered] == ["Đấu sai S", "Đầu dò", "Hở mạch"]
