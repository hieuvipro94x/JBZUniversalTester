from pathlib import Path

import pytest

from jbz_tester.cycle_state import TestCycleState as CycleState
from jbz_tester.protocol import parse_board_line

FIXTURES = Path(__file__).parent / "fixtures"


def replay(name: str) -> tuple[CycleState, int | None, int, int]:
    state = CycleState()
    circuit = None
    open_messages = 0
    other_messages = 0
    for line in (FIXTURES / name).read_text(encoding="utf-8").splitlines():
        event = parse_board_line(line)
        if event.family == "CLEAR":
            state.open_networks.clear()
            state.wrong_wiring.clear()
        elif event.family == "OPEN":
            open_messages += 1
            state.open_networks.update(event.values)
        elif event.family == "OTHER":
            other_messages += 1
            state.wrong_wiring.update(event.values)
        elif event.family == "CIRCUIT":
            circuit = int(event.values[0])
    return state, circuit, open_messages, other_messages


def test_open_message_replaces_same_network_and_single_key_clears_it():
    state = CycleState()
    state.open_networks.update((10, 10, 11))
    assert state.open_count == 1
    assert state.open_networks.active[10] == (10, 11)

    # Snapshot mới cho network 10, không phải một lỗi cộng thêm.
    state.open_networks.update((10, 11))
    assert state.open_count == 1
    assert state.open_networks.active[10] == (11,)

    # Dòng chỉ còn network id nghĩa là lỗi mạng đó đã hết.
    state.open_networks.update((10,))
    assert state.open_count == 0
    assert 10 not in state.open_networks.active


def test_reciprocal_other_messages_are_one_wrong_pair_but_two_table_rows():
    state = CycleState()
    assert state.wrong_wiring.update((113, 123)) is True
    assert state.wrong_wiring.update((123, 113)) is False
    assert state.other_count == 1
    assert state.wrong_wiring.row_count == 2
    assert list(state.wrong_wiring.pairs.values()) == [(113, 123)]


@pytest.mark.parametrize("fixture", ["cycle_1_rx.txt", "cycle_2_rx.txt"])
def test_real_pass_cycles_finish_with_no_active_open_networks(fixture: str):
    state, circuit, open_messages, other_messages = replay(fixture)
    assert open_messages > 190  # Có rất nhiều update OPEN trong chu kỳ PASS thật.
    assert other_messages == 0
    assert circuit == 0
    assert state.open_count == 0
    assert state.other_count == 0
    assert state.open_networks.active == {}


@pytest.mark.parametrize(
    ("fixture", "active_networks", "open_links"),
    [
        ("cycle_3_rx.txt", 81, 85),
        ("cycle_4_rx.txt", 89, 92),
    ],
)
def test_real_fail_cycles_keep_only_final_snapshot_and_one_wrong_pair(
    fixture: str, active_networks: int, open_links: int
):
    state, circuit, open_messages, other_messages = replay(fixture)
    assert open_messages >= 100
    assert other_messages == 2  # Bo gửi hai hướng A-B và B-A.
    assert circuit == 1
    assert len(state.open_networks.active) == active_networks
    assert state.open_count == open_links
    assert state.other_count == 1
    assert state.wrong_wiring.row_count == 2


def test_real_incomplete_cycle_initial_open_snapshot_counts_98_links():
    state, circuit, open_messages, other_messages = replay("cycle_5_rx.txt")
    assert circuit is None
    assert open_messages == 94
    assert other_messages == 0
    assert len(state.open_networks.active) == 94
    # Khớp bộ đếm Hở mạch 98 trên phần mềm gốc cho WH322110.
    assert state.open_count == 98


def test_live_pair_disappears_and_reappears_with_model_topology():
    state = CycleState()
    topology = {15: (16,)}

    state.open_networks.update((15, 15, 16))
    assert state.open_networks.display_pins(15, topology[15]) == (15, 16)
    assert state.open_count_for(topology) == 1

    # Chập đúng: firmware chỉ gửi network id, cả hai dòng phải mất.
    state.open_networks.update((15,))
    assert state.open_networks.display_pins(15, topology[15]) == ()
    assert state.open_count_for(topology) == 0

    # Tháo ra: hai dòng phải xuất hiện lại và bộ đếm tăng lại.
    state.open_networks.update((15, 15, 16))
    assert state.open_networks.display_pins(15, topology[15]) == (15, 16)
    assert state.open_count_for(topology) == 1


def test_source_only_payload_restores_all_model_targets():
    state = CycleState()
    topology = {16: (63, 75)}
    state.open_networks.update((16, 16))
    assert state.open_networks.display_pins(16, topology[16]) == (16, 63, 75)
    assert state.open_count_for(topology) == 2


def test_target_only_payload_still_shows_source_reference_row():
    state = CycleState()
    topology = {123: (171, 112)}
    state.open_networks.update((123, 171))
    assert state.open_networks.display_pins(123, topology[123]) == (123, 171)
    assert state.open_count_for(topology) == 1
