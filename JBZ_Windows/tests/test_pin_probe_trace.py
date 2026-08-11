from pathlib import Path
from jbz_tester.model_data import load_product_model
from jbz_tester.probe_state import PinProbeTracker
from jbz_tester.protocol import parse_board_line


FIXTURES = Path(__file__).parent / "fixtures"


def test_tracker_supports_multiple_simultaneous_pins_latest_first():
    tracker = PinProbeTracker()
    tracker.update(189, "ON", timestamp=1.0)
    tracker.update(190, "ON", timestamp=2.0)
    assert tracker.ordered_pins() == (190, 189)
    tracker.update(189, "OFF", timestamp=3.0)
    assert tracker.ordered_pins() == (190,)
    tracker.update(190, "OFF", timestamp=4.0)
    assert tracker.ordered_pins() == ()


def test_tracker_replays_actual_probe_sequence_without_orphan_state():
    tracker = PinProbeTracker()
    for raw in (FIXTURES / "pin_probe_trace_rx.txt").read_text().splitlines():
        event = parse_board_line(raw)
        if event.family == "TESTPIN":
            tracker.update(int(event.values[0]), str(event.values[1]))
    assert tracker.count == 0


def test_physical_pin_maps_directly_to_model_record_and_color():
    model = load_product_model(FIXTURES / "WH321798.model")
    pin = model.pin(125)
    assert pin is not None
    assert pin.connector == "HOLDER 07"
    assert pin.local_pin == "7"
    assert pin.line_name == "M3C3"
    assert pin.color_tokens == ("P",)


def test_pin_200_is_valid_even_without_network_parent_or_target():
    model = load_product_model(FIXTURES / "WH321798.model")
    pin = model.pin(200)
    assert pin is not None
    assert pin.connector == "BAND"
    assert pin.local_pin == "5"


def test_board_source_does_not_implement_or_send_pintest():
    source = (Path(__file__).parents[1] / "jbz_tester" / "board.py").read_text()
    assert "def pin_test" not in source
    assert ":PINTEST" not in source
