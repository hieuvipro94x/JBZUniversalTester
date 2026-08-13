from jbz_tester.protocol import parse_board_line


def test_open():
    event = parse_board_line(":OPEN,78,78,79,80,81,82,83")
    assert event.family == "OPEN"
    assert event.values == (78, 78, 79, 80, 81, 82, 83)


def test_other():
    event = parse_board_line(":OTHER,2,12")
    assert event.family == "OTHER"
    assert event.values == (2, 12)


def test_circuit():
    assert parse_board_line(":CIRCUIT,0").values == (0,)
    assert parse_board_line(":CIRCUIT,1").values == (1,)


def test_testpin_on_off():
    on = parse_board_line(":TESTPIN,15,ON")
    off = parse_board_line(":TESTPIN,15,OFF")
    assert on.family == "TESTPIN"
    assert on.values == (15, "ON")
    assert off.values == (15, "OFF")


def test_pin_diagnostic_line():
    event = parse_board_line(":PIN,15,1")
    assert event.family == "PIN"
    assert event.values == (15, 1)
