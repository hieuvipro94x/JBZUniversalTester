"""Bộ giao diện tham chiếu từ UniversalTesterVN_Project.

Module này chỉ chứa màu, font và helper dựng widget. Không chứa giao tiếp UART,
không xử lý model và không thay đổi trạng thái kiểm tra của bản Production V10.
"""
from __future__ import annotations

import tkinter as tk
from tkinter import ttk

BG = "#f4f4f1"
PANEL = "#fafafa"
FIELD = "#f8f8f6"
HEADER = "#eeeeea"
BUTTON = "#e7e7e4"
BUTTON_ACTIVE = "#d8d8d5"
BORDER = "#c7c7c2"
GRID = "#d7d7d2"
TEXT = "#555555"
TEXT_DARK = "#333333"
MUTED = "#777777"
BRAND = "#243b8f"
STATUS_READY = "#9e9e9e"
STATUS_RUNNING = "#8e44ad"
STATUS_PASS = "#2aa84a"
STATUS_FAIL = "#c62828"
STATUS_WARN = "#e09b27"
PROBE = "#23c8c8"
FONT = "Arial"


def scaled_font(scale: float, size: int, weight: str | None = None):
    px = max(8, int(size * scale))
    return (FONT, px, weight) if weight else (FONT, px)


def apply_ttk_theme(root: tk.Misc, scale: float = 1.0) -> None:
    """Áp dụng theme cho Treeview, Notebook và Progressbar của Tkinter."""
    style = ttk.Style(root)
    try:
        style.theme_use("clam")
    except tk.TclError:
        pass

    body_font = scaled_font(scale, 12)
    header_font = scaled_font(scale, 12, "bold")
    style.configure(
        "TesterVN.Treeview",
        background=PANEL,
        fieldbackground=PANEL,
        foreground=TEXT,
        bordercolor=BORDER,
        lightcolor=BORDER,
        darkcolor=BORDER,
        rowheight=max(28, int(34 * scale)),
        font=body_font,
    )
    style.configure(
        "TesterVN.Treeview.Heading",
        background=HEADER,
        foreground=TEXT,
        relief="flat",
        borderwidth=1,
        font=header_font,
        padding=max(3, int(5 * scale)),
    )
    style.map(
        "TesterVN.Treeview",
        background=[("selected", "#dcdcff")],
        foreground=[("selected", TEXT_DARK)],
    )
    style.configure(
        "TesterVN.TNotebook",
        background=BG,
        borderwidth=0,
    )
    style.configure(
        "TesterVN.TNotebook.Tab",
        background=BUTTON,
        foreground=TEXT,
        borderwidth=1,
        padding=(max(10, int(14 * scale)), max(5, int(7 * scale))),
        font=scaled_font(scale, 12),
    )
    style.map(
        "TesterVN.TNotebook.Tab",
        background=[("selected", FIELD), ("active", "#eeeeec")],
    )
    style.configure(
        "TesterVN.Horizontal.TProgressbar",
        troughcolor=FIELD,
        background=BRAND,
        bordercolor=BORDER,
        lightcolor=BRAND,
        darkcolor=BRAND,
    )


def flat_button(parent, text: str, command, scale: float, **kwargs) -> tk.Button:
    options = dict(
        text=text,
        command=command,
        bg=BUTTON,
        fg=TEXT,
        activebackground=BUTTON_ACTIVE,
        activeforeground=TEXT_DARK,
        disabledforeground="#aaaaaa",
        relief="solid",
        bd=1,
        highlightthickness=0,
        font=scaled_font(scale, 13),
        cursor="hand2",
    )
    options.update(kwargs)
    return tk.Button(parent, **options)


def value_label(parent, variable=None, text: str = "", scale: float = 1.0, **kwargs) -> tk.Label:
    options = dict(
        text=text,
        textvariable=variable,
        bg=FIELD,
        fg=TEXT,
        relief="solid",
        bd=1,
        anchor="w",
        padx=max(5, int(7 * scale)),
        font=scaled_font(scale, 14, "bold"),
    )
    if variable is None:
        options.pop("textvariable")
    else:
        options.pop("text")
    options.update(kwargs)
    return tk.Label(parent, **options)
