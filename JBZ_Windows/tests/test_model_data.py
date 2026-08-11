from pathlib import Path
from jbz_tester.model_data import load_product_model


def test_sample_model():
    path = Path(__file__).parent / "sample.model"
    model = load_product_model(path)
    assert model.pin_count == 2
    assert model.pin(1).targets == (2,)


def test_product_model_builds_network_topology_from_targets_and_parent(tmp_path):
    model = tmp_path / "pair.model"
    model.write_text(
        """[Common]\nModel=PAIR\n[Connector]\nCount=1\n[Pin]\nCount=2\nP1=15|C1|1|RET1|||||-1|16\nP2=16|C1|2|RET1|||||15|\n""",
        encoding="utf-8",
    )
    product = load_product_model(model)
    assert product.network_targets(15) == (16,)
    assert product.network_topology()[15] == (16,)


def test_wire_color_is_read_from_model_color_field(tmp_path):
    model = tmp_path / "colors.model"
    model.write_text(
        """[Common]\nModel=COLORS\n[Connector]\nCount=1\n[Pin]\nCount=2\nP1=1|C1|1|MC01|||R||-1|2\nP2=2|C1|2|MC01|||L/W||1|\n""",
        encoding="utf-8",
    )
    product = load_product_model(model)
    assert product.pin(1).color_text == "R"
    assert product.pin(1).color_tokens == ("R",)
    assert product.pin(2).color_text == "L/W"
    assert product.pin(2).color_tokens == ("L", "W")




def test_v9_splice_gauge_and_color_use_separate_fields(tmp_path):
    model = tmp_path / "v9_columns.model"
    model.write_text(
        "[Common]\nModel=V9\n[Connector]\nCount=1\n[Pin]\nCount=2\n"
        "P1=1|C1|1|MC21|MC01|0.3|Gr/Br||-1|2\n"
        "P2=2|C1|2|MC01||1.25|B/G||1|\n",
        encoding="utf-8",
    )
    product = load_product_model(model)
    p1 = product.pin(1)
    p2 = product.pin(2)
    assert p1.splice_wire == "MC01"
    assert p1.gauge == "0.3"
    assert p1.color_text == "Gr/Br"
    assert p1.color_tokens == ("GR", "BR")
    assert p2.splice_wire == "MC21"
    assert p2.gauge == "1.25"


def test_v9_missing_or_invalid_gauge_is_blank(tmp_path):
    model = tmp_path / "blank_gauge.model"
    model.write_text(
        "[Common]\nModel=BLANK\n[Connector]\nCount=1\n[Pin]\nCount=2\n"
        "P1=1|C1|1|MC21|MC01||Gr/Br||-1|2\n"
        "P2=2|C1|2|MC01|MC21|NOT_GAUGE|B/G||1|\n",
        encoding="utf-8",
    )
    product = load_product_model(model)
    assert product.pin(1).gauge == ""
    assert product.pin(2).gauge == ""


def test_v9_decimal_comma_gauge_is_normalized(tmp_path):
    model = tmp_path / "comma_gauge.model"
    model.write_text(
        "[Common]\nModel=COMMA\n[Connector]\nCount=1\n[Pin]\nCount=1\n"
        "P1=1|C1|1|L1||0,5|R||-1|\n",
        encoding="utf-8",
    )
    product = load_product_model(model)
    assert product.pin(1).gauge == "0.5"


def test_v9_splice_link_is_bidirectional_without_duplicates(tmp_path):
    model = tmp_path / "splice_both.model"
    model.write_text(
        "[Common]\nModel=SPLICE\n[Connector]\nCount=1\n[Pin]\nCount=2\n"
        "P1=1|C1|1|MC21|MC01|0.5|Gr/Br||-1|2\n"
        "P2=2|C1|2|MC01|MC21|0.5|B/G||1|\n",
        encoding="utf-8",
    )
    product = load_product_model(model)
    assert product.pin(1).splice_wire == "MC01"
    assert product.pin(2).splice_wire == "MC21"
