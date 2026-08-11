from jbz_tester.fault_table import FaultRow, FaultTable


def test_v9_column_order_is_exact():
    assert FaultTable.HEADERS == (
        "Loại lỗi", "I/O", "Giắc", "Chân", "Tên dây",
        "Dây dập nối", "Tiết diện", "Màu dây", "#1", "#2", "#3", "#4",
    )


def test_fault_row_keeps_splice_gauge_and_color_separate():
    row = FaultRow(
        kind="Hở mạch",
        io="9",
        connector="4",
        pin="3",
        line="MC21",
        splice_wire="MC01",
        gauge="0.5",
        color_text="Gr/Br",
        colors=("GR", "BR"),
    )
    assert row.splice_wire == "MC01"
    assert row.gauge == "0.5"
    assert row.color_text == "Gr/Br"
