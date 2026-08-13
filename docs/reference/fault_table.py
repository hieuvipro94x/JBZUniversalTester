from __future__ import annotations

import tkinter as tk
from dataclasses import dataclass

from .ui_theme import BG, BORDER, FIELD, GRID, HEADER, PANEL, STATUS_FAIL, TEXT, TEXT_DARK, FONT


COLOR_MAP = {
    "B": "#101010", "BK": "#101010", "BLACK": "#101010",
    "W": "#ffffff", "WHITE": "#ffffff",
    "R": "#ed0000", "RED": "#ed0000",
    "G": "#00d000", "GREEN": "#00d000",
    "Y": "#ffff00", "YELLOW": "#ffff00",
    "L": "#0077ff", "BL": "#0077ff", "BLUE": "#0077ff",
    "O": "#ff9900", "ORANGE": "#ff9900",
    "P": "#ff2ca5", "PINK": "#ff2ca5",
    "BR": "#8a4300", "BROWN": "#8a4300",
    "GR": "#808080", "GRAY": "#808080", "GREY": "#808080",
    "V": "#7f00bb", "VIOLET": "#7f00bb",
    "SB": "#66ccff", "SKY": "#66ccff",
    "LG": "#77dd77", "LIGHTGREEN": "#77dd77",
}

COLOR_NAMES_VI = {
    "B": "Đen", "BK": "Đen", "BLACK": "Đen",
    "W": "Trắng", "WHITE": "Trắng",
    "R": "Đỏ", "RED": "Đỏ",
    "G": "Xanh lá", "GREEN": "Xanh lá",
    "Y": "Vàng", "YELLOW": "Vàng",
    "L": "Xanh dương", "BL": "Xanh dương", "BLUE": "Xanh dương",
    "O": "Cam", "ORANGE": "Cam",
    "P": "Hồng", "PINK": "Hồng",
    "BR": "Nâu", "BROWN": "Nâu",
    "GR": "Xám", "GRAY": "Xám", "GREY": "Xám",
    "V": "Tím", "VIOLET": "Tím",
    "SB": "Xanh trời", "SKY": "Xanh trời",
    "LG": "Xanh nhạt", "LIGHTGREEN": "Xanh nhạt",
}


def color_label(raw: str, tokens: tuple[str, ...]) -> str:
    """Tên màu dễ đọc; vẫn giữ mã gốc để đối chiếu hồ sơ sản xuất."""
    if not tokens:
        return raw
    names = [COLOR_NAMES_VI.get(token.upper(), token) for token in tokens]
    readable = "/".join(names)
    code = raw.strip() or "/".join(tokens)
    return f"{code} ({readable})" if readable and readable.upper() != code.upper() else code


@dataclass()
class FaultRow:
    kind: str
    io: str
    connector: str
    pin: str
    line: str
    splice_wire: str = ""
    gauge: str = ""
    color_text: str = ""
    colors: tuple[str, ...] = ()
    style: str = "normal"


def fault_priority(row: FaultRow) -> int:
    """Đấu sai luôn đứng đầu, sau đó đến đầu dò và hở mạch."""
    if row.style == "wrong" or row.kind.startswith("Đấu sai"):
        return 0
    if row.style == "probe":
        return 1
    return 2


class FaultTable(tk.Frame):
    # Thứ tự cột V9 khớp giao diện máy gốc và yêu cầu sản xuất.
    HEADERS = (
        "Loại lỗi", "I/O", "Giắc", "Chân", "Tên dây",
        "Dây dập nối", "Tiết diện", "Màu dây", "#1", "#2", "#3", "#4",
    )
    WIDTHS = (165, 135, 95, 82, 175, 150, 95, 130, 28, 28, 28, 28)

    def __init__(self, master, ui_scale: float = 1.0, **kwargs):
        super().__init__(master, bg=BG, **kwargs)
        self.ui_scale = max(0.70, min(1.35, float(ui_scale)))
        self.rows: list[FaultRow] = []
        self.header_height = max(28, int(38 * self.ui_scale))
        self.row_height = max(26, int(36 * self.ui_scale))
        self.header_font_size = max(9, int(13 * self.ui_scale))
        self.row_font_size = max(9, int(12 * self.ui_scale))
        self.header = tk.Canvas(
            self, height=self.header_height, bg=HEADER,
            highlightthickness=1, highlightbackground=BORDER,
        )
        self.canvas = tk.Canvas(self, bg=PANEL, highlightthickness=0)
        self.scroll = tk.Scrollbar(self, orient="vertical", command=self.canvas.yview)
        self.canvas.configure(yscrollcommand=self.scroll.set)
        self.header.pack(fill="x", side="top")
        self.scroll.pack(fill="y", side="right")
        self.canvas.pack(fill="both", expand=True, side="left")
        self.canvas.bind("<Configure>", lambda _e: self.redraw())
        self.canvas.bind_all("<MouseWheel>", self._wheel)
        self.redraw_header()

    def _wheel(self, event):
        self.canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")

    def clear(self):
        self.rows.clear()
        self.redraw()
        self.canvas.yview_moveto(0.0)

    def set_rows(self, rows: list[FaultRow]):
        self.rows = sorted(list(rows), key=fault_priority)
        self.redraw()
        self.canvas.yview_moveto(0.0)

    def add(self, row: FaultRow):
        self.rows.append(row)
        self.rows.sort(key=fault_priority)
        self.redraw()
        if fault_priority(row) <= 1:
            self.canvas.yview_moveto(0.0)
        else:
            self.canvas.yview_moveto(1.0)

    def _geometry(self, canvas_width: int) -> tuple[float, float]:
        total = float(sum(self.WIDTHS))
        width = max(float(canvas_width), total)
        return width, width / total

    def redraw_header(self):
        self.header.delete("all")
        width, scale = self._geometry(self.header.winfo_width())
        x = 0.0
        for title, base in zip(self.HEADERS, self.WIDTHS):
            w = base * scale
            self.header.create_rectangle(
                x, 0, x + w, self.header_height,
                fill=HEADER, outline=GRID,
            )
            self.header.create_text(
                x + w / 2,
                self.header_height / 2,
                text=title,
                font=(FONT, self.header_font_size),
                fill=TEXT,
            )
            x += w
        self.header.configure(scrollregion=(0, 0, width, self.header_height))

    def redraw(self):
        self.redraw_header()
        self.canvas.delete("all")
        row_h = self.row_height
        width, scale = self._geometry(self.canvas.winfo_width())

        for row_index, row in enumerate(self.rows):
            y = row_index * row_h
            if row.style == "wrong":
                bg, fg = "#3446a8", "white"
            elif row.style == "probe":
                bg, fg = "#bdeeee", TEXT_DARK
            else:
                bg, fg = FIELD, TEXT

            color_text_value = row.color_text or color_label("", row.colors)
            values = (
                row.kind, row.io, row.connector, row.pin, row.line,
                row.splice_wire, row.gauge, color_text_value, "", "", "", "",
            )
            x = 0.0
            for col, (value, base) in enumerate(zip(values, self.WIDTHS)):
                w = base * scale
                self.canvas.create_rectangle(
                    x, y, x + w, y + row_h, fill=bg, outline=GRID
                )
                if col < 8:
                    self.canvas.create_text(
                        x + max(4, int(6 * self.ui_scale)),
                        y + row_h / 2,
                        text=value,
                        anchor="w",
                        font=(FONT, self.row_font_size),
                        fill=fg,
                    )
                else:
                    token_index = col - 8
                    if token_index < len(row.colors):
                        token = row.colors[token_index].upper()
                        color = COLOR_MAP.get(token, "#cccccc")
                        self.canvas.create_rectangle(
                            x + 2, y + 2, x + w - 2, y + row_h - 2,
                            fill=color, outline="#777777",
                        )
                x += w

        self.canvas.configure(
            scrollregion=(0, 0, width, max(self.canvas.winfo_height(), len(self.rows) * row_h))
        )
