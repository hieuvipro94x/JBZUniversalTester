from pathlib import Path

from jbz_model_loader.model_compiler import compile_legacy_model

FIXTURES = Path(__file__).parent / "fixtures"


def test_wh322110_compiler_matches_all_115_commands_from_real_trace():
    profile, summary = compile_legacy_model(FIXTURES / "WH322110.model")
    expected = (FIXTURES / "WH322110_trace_commands.txt").read_text(
        encoding="utf-8"
    ).splitlines()
    actual = [command.tx for command in profile.commands]

    assert summary.model_name == "WH322110"
    assert summary.pin_rows == 200
    assert summary.source_records == 102
    assert len(actual) == 115
    assert actual == expected
