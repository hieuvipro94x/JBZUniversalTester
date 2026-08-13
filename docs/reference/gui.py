from __future__ import annotations

import os
import queue
import re
import threading
import time
import tkinter as tk
from datetime import datetime
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

from jbz_model_loader.model_locator import resolve_model_files
from jbz_model_loader.profile_io import load_model_profile, load_setup_profile, merge_setup_before_finish
from jbz_model_loader.serial_session import LineLogger, SerialSession
from jbz_uart import UartManager
from jbz_platform import IS_WINDOWS, user_home

from .board import BoardController
from .config import AppConfig, DATABASE_PATH, LOG_DIR, load_config, save_config
from .cycle_state import TestCycleState
from .fault_table import FaultRow, FaultTable, color_label
from .model_data import ProductModel, SetupData, load_product_model, load_setup
from .protocol import BoardEvent
from .probe_state import PinProbeTracker
from .storage import ResultStore
from .ui_theme import (
    BG, BORDER, BRAND, BUTTON, FIELD, FONT, GRID, HEADER, MUTED, PANEL,
    STATUS_FAIL, STATUS_PASS, STATUS_READY, STATUS_RUNNING, STATUS_WARN,
    TEXT, TEXT_DARK, apply_ttk_theme, flat_button, scaled_font, value_label,
)


# Màu nghiệp vụ giữ nguyên ý nghĩa của V10; bảng màu nền/nút lấy từ TesterVN.
PURPLE = STATUS_RUNNING
GREEN = STATUS_PASS
RED = STATUS_FAIL
BLUE = "#0026d9"
YELLOW = "#fff3a0"
CYAN = "#23d9d9"

APP_VERSION = "1.42"

def _resolve_app_build() -> str:
    """Đọc mã build thật từ biến môi trường hoặc file build của project."""
    env_build = os.environ.get("JBZ_APP_BUILD", "").strip()
    if env_build:
        return env_build

    project_root = Path(__file__).resolve().parent.parent
    for name in ("BUILD", "build.txt", "VERSION", "version.txt"):
        build_file = project_root / name
        if build_file.is_file():
            value = build_file.read_text(encoding="utf-8", errors="ignore").strip()
            if value:
                return value.splitlines()[0].strip()

    # Giá trị dự phòng: thời gian file giao diện được đóng gói/cập nhật.
    return datetime.fromtimestamp(Path(__file__).stat().st_mtime).strftime("%Y%m%d.%H%M")


APP_BUILD = _resolve_app_build()



def _model_code_from_response(response: str | None) -> str:
    if not response:
        return ""
    match = re.match(r":MODELNAME,([^,]+)", response)
    return match.group(1).strip() if match else ""


def _normalized_model_code(value: str) -> str:
    compact = re.sub(r"[^A-Z0-9]", "", value.upper())
    return compact[2:] if compact.startswith("WH") else compact


def _user_home_candidates() -> list[Path]:
    """Trả về các home có thể chứa Models/Setups trên Windows hoặc Linux."""
    homes: list[Path] = []

    def add(path: Path | str | None) -> None:
        if not path:
            return
        candidate = Path(path).expanduser()
        if candidate not in homes:
            homes.append(candidate)

    add(os.environ.get("JBZ_DATA_HOME"))

    if IS_WINDOWS:
        add(os.environ.get("USERPROFILE"))
        drive = os.environ.get("HOMEDRIVE", "")
        home_path = os.environ.get("HOMEPATH", "")
        if drive and home_path:
            add(drive + home_path)
        add(user_home())
    else:
        # Khi chạy bằng sudo, HOME có thể là /root; SUDO_USER mới là user vận hành.
        for variable in ("SUDO_USER", "USER", "LOGNAME"):
            username = os.environ.get(variable, "").strip()
            if not username or not re.fullmatch(r"[A-Za-z0-9._-]+", username):
                continue
            expanded = os.path.expanduser(f"~{username}")
            if expanded != f"~{username}":
                add(expanded)

        add(Path.home())
        home_root = Path("/home")
        if home_root.is_dir():
            try:
                for child in sorted(home_root.iterdir(), key=lambda item: item.name.lower()):
                    if child.is_dir():
                        add(child)
            except OSError:
                pass

    return homes


def _model_directory_pairs(config: AppConfig) -> list[tuple[Path, Path]]:
    """Sinh các cặp thư mục Models/Setups mà không phụ thuộc /home/<user> cố định."""
    pairs: list[tuple[Path, Path]] = []

    def add(models: Path | str | None, setups: Path | str | None) -> None:
        if not models or not setups:
            return
        pair = (Path(models).expanduser(), Path(setups).expanduser())
        if pair not in pairs:
            pairs.append(pair)

    # Biến môi trường có độ ưu tiên cao nhất.
    add(os.environ.get("JBZ_MODELS_DIR"), os.environ.get("JBZ_SETUPS_DIR"))

    # Sau đó tìm theo home của user đang chạy / user gọi sudo.
    for home in _user_home_candidates():
        add(home / "Models", home / "Setups")
        add(home / "models", home / "setups")

    # Giữ tương thích với đường dẫn đã lưu trong config nếu đó là thư mục riêng.
    add(getattr(config, "models_dir", None), getattr(config, "setups_dir", None))
    return pairs


def _configure_model_directories(config: AppConfig) -> None:
    """Chọn cặp thư mục hợp lệ cho máy hiện tại ngay khi khởi động."""
    configured_models = Path(str(getattr(config, "models_dir", ""))).expanduser()
    configured_setups = Path(str(getattr(config, "setups_dir", ""))).expanduser()

    # Giữ đường dẫn cấu hình riêng nếu nó vẫn hợp lệ. Khi máy được clone và
    # /home/<user-cũ> không còn tồn tại, chương trình mới tự chuyển sang user hiện tại.
    if configured_models.is_dir() and configured_setups.is_dir():
        return

    pairs = _model_directory_pairs(config)
    selected = next(
        ((models, setups) for models, setups in pairs if models.is_dir() and setups.is_dir()),
        pairs[0] if pairs else (Path.home() / "Models", Path.home() / "Setups"),
    )
    config.models_dir = str(selected[0])
    config.setups_dir = str(selected[1])


def _resolve_model_files_for_user(code: str, config: AppConfig):
    """Tìm model theo mã hàng qua các home user và cập nhật cặp thư mục đang dùng."""
    attempts: list[str] = []
    last_error: Exception | None = None

    for models_dir, setups_dir in _model_directory_pairs(config):
        attempts.append(f"Models={models_dir} | Setups={setups_dir}")
        try:
            files = resolve_model_files(code, str(models_dir), str(setups_dir))
        except Exception as exc:
            last_error = exc
            continue

        config.models_dir = str(models_dir)
        config.setups_dir = str(setups_dir)
        return files

    locations = "\n".join(f"- {item}" for item in attempts)
    detail = f"\nLỗi cuối: {last_error}" if last_error else ""
    raise FileNotFoundError(
        f"Không tìm thấy model/setup cho mã '{code}'.\n"
        f"Đã tìm trong:\n{locations}{detail}"
    )


class RejectDialog(tk.Toplevel):
    def __init__(self, parent, open_count: int, other_count: int, on_confirm):
        super().__init__(parent)
        self.parent_window = parent
        self.on_confirm = on_confirm
        self.title("Xử lý hàng không đạt")
        self.configure(bg=RED)
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()
        self.protocol("WM_DELETE_WINDOW", self._confirm)
        width, height = 520, 330
        x = parent.winfo_rootx() + int(parent.winfo_width() * 0.58)
        y = parent.winfo_rooty() + int(parent.winfo_height() * 0.36)
        self.geometry(f"{width}x{height}+{x}+{y}")
        tk.Label(
            self,
            text="XỬ LÝ HÀNG KHÔNG ĐẠT",
            bg=CYAN,
            fg="#444",
            font=(FONT, 18, "bold"),
            relief="solid",
            bd=2,
        ).place(x=14, y=14, width=492, height=58)
        message = "KIỂM TRA MẠCH KHÔNG ĐẠT.\nHÃY XỬ LÝ SẢN PHẨM LỖI.\n\n"
        message += f"Hở mạch: {open_count}    Đấu sai: {other_count}"
        tk.Label(
            self,
            text=message,
            bg=RED,
            fg="#ffe000",
            font=(FONT, 15, "bold"),
            justify="center",
        ).place(x=20, y=92, width=480, height=150)
        tk.Button(
            self,
            text="XÁC NHẬN",
            command=self._confirm,
            font=(FONT, 13, "bold"),
            bg="#dddddd",
            relief="raised",
            bd=3,
        ).place(x=370, y=257, width=120, height=52)
        self.bind("<Return>", lambda _e: self._confirm())
        self.after(100, self.focus_force)

    def _confirm(self):
        try:
            self.grab_release()
        except tk.TclError:
            pass
        self.destroy()
        self.parent_window.after_idle(self.on_confirm)


class MainMenuWindow(tk.Toplevel):
    """Màn hình menu chính của máy, chỉ hiển thị các chức năng vận hành."""

    def __init__(self, parent: "MainWindow"):
        super().__init__(parent)
        self.parent_window = parent
        self.title("Universal Tester - Menu chính")
        self.configure(bg=BG)
        self.protocol("WM_DELETE_WINDOW", self._exit)
        sw, sh = self.winfo_screenwidth(), self.winfo_screenheight()
        self.ui_scale = getattr(parent, "ui_scale", max(0.70, min(1.35, min(sw / 1650, sh / 930))))
        self.geometry(f"{sw}x{sh}+0+0")
        self.attributes("-fullscreen", True)
        self.bind("<Escape>", self._handle_escape)
        self.model_display_var = tk.StringVar(value="CHƯA CHỌN MÃ HÀNG")
        self.product_display_var = tk.StringVar(value="")
        self.setup_display_var = tk.StringVar(value="")
        self.state_display_var = tk.StringVar(value="HÃY BẤM NHẬP MODEL ĐỂ CHỌN MÃ HÀNG")
        self.board_display_var = tk.StringVar(value="ĐANG KẾT NỐI BO...")

        # Trình tải model nội tuyến: nằm ngay trong MainMenuWindow,
        # không tạo thêm cửa sổ Toplevel.
        self.inline_code_var = tk.StringVar(value="")
        self.inline_status_var = tk.StringVar(value="NHẬP MÃ HÀNG CẦN TẢI")
        self.inline_cancel_event = threading.Event()
        self.inline_loader_running = False

        self._build()
        self.refresh()
        self.after(200, self.refresh)

    def _font(self, size: int, weight: str | None = None):
        scaled = max(8, int(size * self.ui_scale))
        return scaled_font(self.ui_scale, size, weight)

    def _build(self):
        apply_ttk_theme(self, self.ui_scale)

        header = tk.Frame(self, bg=BG)
        header.place(relx=0.012, rely=0.018, relwidth=0.976, relheight=0.13)
        tk.Label(
            header,
            textvariable=self.setup_display_var,
            bg=BG,
            fg=MUTED,
            anchor="nw",
            justify="left",
            wraplength=max(420, int(760 * self.ui_scale)),
            font=self._font(11),
        ).pack(side="left", fill="both", expand=True)

        info = tk.Frame(header, bg=BG)
        info.pack(side="right", anchor="ne")

        # Firmware chỉ được hiển thị sau khi người vận hành tải model thành công.
        self.firmware_var = tk.StringVar(value="")
        # Phiên bản/build phần mềm luôn hiển thị.
        self.build_var = tk.StringVar(
            value=f"Software : V{APP_VERSION} | Build {APP_BUILD}"
        )

        tk.Label(
            info,
            textvariable=self.firmware_var,
            bg=BG,
            fg=TEXT,
            anchor="e",
            font=self._font(14, "bold"),
        ).pack(anchor="e")

        tk.Label(
            info,
            textvariable=self.build_var,
            bg=BG,
            fg=MUTED,
            anchor="e",
            font=self._font(12),
        ).pack(anchor="e", pady=(max(3, int(5 * self.ui_scale)), 0))

        center = tk.Frame(self, bg=BG)
        center.place(relx=0.5, rely=0.48, anchor="center", relwidth=0.88, relheight=0.58)

        logo = tk.Frame(center, bg=PANEL, highlightthickness=0)
        logo.pack(pady=(0, max(10, int(16 * self.ui_scale))))
        tk.Label(
            logo,
            text="VINA",
            bg=PANEL,
            fg=BRAND,
            font=self._font(48, "bold"),
            padx=max(18, int(28 * self.ui_scale)),
        ).pack()
        tk.Label(
            logo,
            text="JBZVINA",
            bg=BRAND,
            fg="white",
            font=self._font(18, "bold"),
            padx=max(12, int(20 * self.ui_scale)),
            pady=max(2, int(3 * self.ui_scale)),
        ).pack(fill="x")

        tk.Label(
            center,
            textvariable=self.model_display_var,
            bg=BG,
            fg=TEXT,
            font=self._font(31),
            justify="center",
        ).pack(pady=(max(5, int(8 * self.ui_scale)), max(3, int(6 * self.ui_scale))))
        tk.Label(
            center,
            textvariable=self.product_display_var,
            bg=BG,
            fg=MUTED,
            font=self._font(15),
            justify="center",
            wraplength=max(760, int(1180 * self.ui_scale)),
        ).pack(pady=max(2, int(4 * self.ui_scale)))

        self.state_label = tk.Label(
            center,
            textvariable=self.state_display_var,
            bg=HEADER,
            fg=TEXT,
            relief="solid",
            bd=1,
            font=self._font(14, "bold"),
            padx=max(16, int(24 * self.ui_scale)),
            pady=max(8, int(11 * self.ui_scale)),
        )
        self.state_label.pack(pady=max(8, int(14 * self.ui_scale)))

        self.board_label = tk.Label(
            center,
            textvariable=self.board_display_var,
            bg=BG,
            fg=MUTED,
            font=self._font(10),
            justify="center",
            wraplength=max(760, int(1250 * self.ui_scale)),
        )
        self.board_label.pack(pady=2)

        # ==============================================================
        # NHẬP MODEL NỘI TUYẾN - NẰM NGAY TRÊN MENU CHÍNH
        # ==============================================================
        self.inline_loader_frame = tk.Frame(center, bg=BG)

        inline_row = tk.Frame(self.inline_loader_frame, bg=BG)
        inline_row.pack()

        tk.Label(
            inline_row,
            text="MÃ HÀNG",
            bg=BG,
            fg=TEXT,
            font=self._font(14, "bold"),
        ).pack(side="left", padx=(0, 12))

        self.inline_entry = tk.Entry(
            inline_row,
            textvariable=self.inline_code_var,
            font=self._font(19),
            justify="center",
            relief="solid",
            bd=1,
            bg=FIELD,
            fg=TEXT_DARK,
            insertbackground=TEXT_DARK,
            width=24,
        )
        self.inline_entry.pack(
            side="left",
            ipady=max(7, int(9 * self.ui_scale)),
            ipadx=10,
        )
        self.inline_entry.bind("<Return>", lambda _e: self._start_inline_model_load())

        self.inline_load_button = flat_button(
            inline_row,
            "TẢI MODEL",
            self._start_inline_model_load,
            self.ui_scale,
            font=self._font(13, "bold"),
            width=12,
        )
        self.inline_load_button.pack(
            side="left",
            padx=(12, 0),
            ipady=max(5, int(7 * self.ui_scale)),
        )

        self.inline_status_label = tk.Label(
            self.inline_loader_frame,
            textvariable=self.inline_status_var,
            bg=BG,
            fg=TEXT,
            font=self._font(12),
            justify="center",
            wraplength=max(600, int(920 * self.ui_scale)),
        )
        self.inline_status_label.pack(
            pady=(max(12, int(18 * self.ui_scale)), max(6, int(8 * self.ui_scale)))
        )

        self.inline_progress = ttk.Progressbar(
            self.inline_loader_frame,
            maximum=100,
            mode="determinate",
            length=max(560, int(760 * self.ui_scale)),
            style="TesterVN.Horizontal.TProgressbar",
        )
        # Chưa hiển thị thanh tiến trình khi chỉ mở vùng nhập model.
        # Thanh này chỉ được pack sau khi nhấn Enter hoặc nút TẢI MODEL.

        self.inline_percent_label = tk.Label(
            self.inline_loader_frame,
            text="0%",
            bg=BG,
            fg=MUTED,
            font=self._font(11, "bold"),
        )
        # Chưa pack nhãn phần trăm ở đây.

        # Ẩn lúc mở chương trình; chỉ hiện khi bấm NHẬP MODEL.
        self.inline_loader_frame.pack_forget()

        # ==============================================================
        # THANH ĐIỀU KHIỂN MENU DƯỚI CÙNG
        # - 5 nút chức năng nằm sát bên trái.
        # - Nút KẾT THÚC nằm riêng sát bên phải.
        # - Chỉ thay đổi giao diện, giữ nguyên toàn bộ callback/logic V10.
        # ==============================================================
        buttons = tk.Frame(self, bg=BG)
        buttons.place(
            relx=0.004,
            rely=0.992,
            anchor="sw",
            relwidth=0.992,
            height=max(50, int(58 * self.ui_scale)),
        )

        left_buttons = tk.Frame(buttons, bg=BG)
        left_buttons.pack(side="left", fill="y")

        left_items = (
            ("BẮT ĐẦU KIỂM TRA", self._start_test),
            ("KIỂM TRA CHÂN PIN", self._open_diagnostics),
            ("LỊCH SỬ", self._open_report),
            ("CÀI ĐẶT", self._open_settings),
            ("NHẬP MODEL", self._open_loader),
        )

        normal_button_width = max(
            200,
            min(175, int(145 * self.ui_scale)),
        )
        button_height = max(
            46,
            int(52 * self.ui_scale),
        )
        button_gap = max(
            4,
            int(6 * self.ui_scale),
        )

        for index, (label, command) in enumerate(left_items):
            button_box = tk.Frame(
                left_buttons,
                bg=BG,
                width=normal_button_width,
                height=button_height,
            )
            button_box.pack(
                side="left",
                padx=(0, button_gap),
            )
            button_box.pack_propagate(False)

            button = flat_button(
                button_box,
                label,
                command,
                self.ui_scale,
                font=self._font(10, "bold"),
            )
            button.pack(fill="both", expand=True)

            if index == 0:
                self.test_button = button

        exit_button_width = max(
            105,
            min(125, int(115 * self.ui_scale)),
        )

        exit_box = tk.Frame(
            buttons,
            bg=BG,
            width=exit_button_width,
            height=button_height,
        )
        exit_box.pack(side="right")
        exit_box.pack_propagate(False)

        exit_button = flat_button(
            exit_box,
            "KẾT THÚC",
            self._exit,
            self.ui_scale,
            font=self._font(10, "bold"),
        )
        exit_button.pack(fill="both", expand=True)

    def refresh(self):
        if not self.winfo_exists():
            return
        parent = self.parent_window
        code = parent.current_model_code or (parent.product.code if parent.product else "")
        if code and parent.product:
            self.model_display_var.set(parent.product.title or code)
            details = []
            if parent.product.product_no:
                details.append(f"Mã sản phẩm: {parent.product.product_no}")
            if parent.product.product_name:
                details.append(f"Tên sản phẩm: {parent.product.product_name}")
            if parent.product.vehicle:
                details.append(f"Loại xe: {parent.product.vehicle}")
            self.product_display_var.set("     ".join(details))
            if parent.model_verified:
                self.state_display_var.set("MODEL ĐÃ TẢI - BẤM KIỂM TRA ĐỂ BẮT ĐẦU")
                self.state_label.configure(bg=PURPLE, fg="white")
                self.test_button.configure(state="normal")
            else:
                self.state_display_var.set("ĐANG XÁC NHẬN MODEL TRÊN BO...")
                self.state_label.configure(bg=STATUS_WARN, fg="white")
                self.test_button.configure(state="disabled")
        elif code:
            self.model_display_var.set(code)
            self.product_display_var.set("")
            self.state_display_var.set("BO ĐANG CÓ MODEL NHƯNG CHƯA NẠP DỮ LIỆU LOCAL - HÃY BẤM NHẬP MODEL")
            self.state_label.configure(bg=STATUS_WARN, fg="white")
            self.test_button.configure(state="disabled")
        else:
            self.model_display_var.set("CHƯA CHỌN MÃ HÀNG")
            self.product_display_var.set("")
            self.state_display_var.set("HÃY BẤM NHẬP MODEL ĐỂ CHỌN MÃ HÀNG")
            self.state_label.configure(bg=HEADER, fg=TEXT)
            self.test_button.configure(state="disabled")
        self.setup_display_var.set(str(parent.setup.path) if parent.setup else "")
        self.board_display_var.set(parent.board_info_var.get())

        # Chỉ hiện firmware sau khi model đã được tải xuống bo thành công.
        if parent.firmware_reveal_after_upload and parent.firmware_version:
            self.firmware_var.set(f"Firmware : {parent.firmware_version}")
        else:
            self.firmware_var.set("")

        # Software/build luôn hiện, kể cả khi chưa kết nối bo.
        self.build_var.set(f"Software : V{APP_VERSION} | Build {APP_BUILD}")

    def _handle_escape(self, _event=None):
        """Ẩn vùng nhập model khi nhấn ESC nếu không đang tải."""
        if self.inline_loader_running:
            # Không cho đóng giao diện giữa lúc đang truyền dữ liệu xuống bo.
            return "break"

        if self.inline_loader_frame.winfo_manager():
            self._hide_inline_loader()
            return "break"

        return "break"

    def _hide_inline_loader(self):
        """Ẩn ô nhập, trạng thái, thanh tiến trình và phần trăm."""
        self.inline_progress.pack_forget()
        self.inline_percent_label.pack_forget()
        self.inline_loader_frame.pack_forget()

        self.inline_progress["value"] = 0
        self.inline_percent_label.configure(text="0%")
        self.inline_status_var.set("NHẬP MÃ HÀNG CẦN TẢI")
        self.inline_code_var.set("")
        self.inline_entry.configure(state="normal")
        self.inline_load_button.configure(state="normal")
        self.focus_set()

    def _open_loader(self):
        """Hiện ô nhập model ngay trong màn hình menu chính."""
        if self.inline_loader_running:
            self.inline_entry.focus_set()
            return

        self.inline_cancel_event.clear()
        self.inline_code_var.set("")
        self.inline_status_var.set("NHẬP MÃ HÀNG CẦN TẢI")
        self.inline_progress["value"] = 0
        self.inline_percent_label.configure(text="0%")
        self.inline_progress.pack_forget()
        self.inline_percent_label.pack_forget()
        self.inline_entry.configure(state="normal")
        self.inline_load_button.configure(state="normal")

        if not self.inline_loader_frame.winfo_manager():
            self.inline_loader_frame.pack(
                pady=(max(10, int(14 * self.ui_scale)), 0)
            )

        self.inline_loader_frame.lift()
        self.inline_entry.focus_set()
        self.inline_entry.select_range(0, "end")

    def _start_inline_model_load(self):
        code = self.inline_code_var.get().strip()
        if not code:
            messagebox.showwarning("Mã hàng", "Hãy nhập mã hàng", parent=self)
            self.inline_entry.focus_set()
            return

        if self.inline_loader_running:
            return

        self.inline_loader_running = True
        self.inline_cancel_event.clear()

        # Bắt đầu một lần tải mới: ẩn firmware cho tới khi tải hoàn tất.
        self.parent_window.firmware_reveal_after_upload = False
        self.firmware_var.set("")
        self.inline_entry.configure(state="disabled")
        self.inline_load_button.configure(state="disabled")
        self.inline_progress["value"] = 0
        self.inline_percent_label.configure(text="0%")

        # Chỉ hiện tiến trình sau khi người dùng nhấn Enter/TẢI MODEL.
        if not self.inline_progress.winfo_manager():
            self.inline_progress.pack(
                pady=(max(8, int(10 * self.ui_scale)), 0),
                ipady=max(5, int(7 * self.ui_scale)),
            )
        if not self.inline_percent_label.winfo_manager():
            self.inline_percent_label.pack(pady=(5, 0))

        self.inline_status_var.set("ĐANG TÌM FILE MODEL VÀ SETUP...")

        threading.Thread(
            target=self._inline_model_worker,
            args=(code,),
            daemon=True,
        ).start()

    def _inline_model_worker(self, code: str):
        try:
            parent = self.parent_window
            files = _resolve_model_files_for_user(code, parent.config)
            model = load_product_model(files.model_path)
            setup = load_setup(files.setup_path)
            profile = load_model_profile(files.model_path)
            profile = merge_setup_before_finish(
                profile,
                load_setup_profile(files.setup_path),
            )

            parent.disconnect_board()
            LOG_DIR.mkdir(parents=True, exist_ok=True)
            logger = LineLogger(
                LOG_DIR / f"model_upload_{datetime.now():%Y%m%d_%H%M%S}.log"
            )
            try:
                with SerialSession(
                    None,
                    parent.config.baudrate,
                    logger,
                    self.inline_cancel_event,
                    preferred_port=parent.config.last_uart,
                    on_port_found=parent.remember_uart,
                    probe_timeout=parent.config.uart_probe_timeout_ms / 1000.0,
                ) as session:
                    def progress(done: int, total: int, command: str):
                        percent = int(done * 100 / max(1, total))
                        self.after(
                            0,
                            self._inline_model_progress,
                            percent,
                            command,
                        )

                    result = session.upload_profile(
                        profile,
                        progress=progress,
                        reset_after=True,
                        verify_after=False,
                    )
            finally:
                logger.close()

            wait_ms = max(0, int(parent.config.board_boot_stabilize_ms))
            self.after(
                0,
                lambda: self.inline_status_var.set(
                    "100% - ĐANG CHỜ BO KHỞI ĐỘNG LẠI..."
                ),
            )
            time.sleep(wait_ms / 1000.0)
            self.after(
                0,
                self._inline_model_success,
                model,
                setup,
                result.verified_model_response,
            )
        except Exception as exc:
            self.after(0, self._inline_model_failure, str(exc))

    def _inline_model_progress(self, percent: int, command: str):
        self.inline_progress["value"] = max(0, min(100, percent))
        self.inline_percent_label.configure(text=f"{percent}%")
        self.inline_status_var.set(command)

    def _inline_model_success(
        self,
        model: ProductModel,
        setup: SetupData,
        verified: str | None,
    ):
        self.inline_progress["value"] = 100
        self.inline_percent_label.configure(text="100%")
        self.inline_status_var.set("TẢI MODEL HOÀN TẤT")

        parent = self.parent_window
        parent.set_loaded_model(model, setup, verified)
        parent.firmware_reveal_after_upload = True
        parent.auto_start_pending = False
        parent.connect_board_async()
        parent.refresh_menu_window()

        self.inline_loader_running = False
        self.after(700, self._hide_inline_loader_after_success)

    def _hide_inline_loader_after_success(self):
        # Đạt 100% thì tự ẩn toàn bộ vùng nhập model.
        self._hide_inline_loader()
        self.refresh()

    def _inline_model_failure(self, message: str):
        self.inline_loader_running = False
        self.inline_entry.configure(state="normal")
        self.inline_load_button.configure(state="normal")
        self.inline_status_var.set("LỖI")
        messagebox.showerror("Không thể tải model", message, parent=self)
        self.inline_entry.focus_set()

        parent = self.parent_window
        if not parent.board or not parent.board.connected:
            parent.connect_board_async()

    def _start_test(self):
        self.parent_window.enter_test_screen(menu_owner=self)

    def _open_report(self):
        window = ReportWindow(self.parent_window)
        window.transient(self)
        window.lift()

    def _open_diagnostics(self):
        parent = self.parent_window
        if not parent.board or not parent.board.connected:
            messagebox.showerror("UART", "Chưa kết nối bo", parent=self)
            return
        window = DiagnosticWindow(parent)
        window.transient(self)
        window.lift()

    def _open_settings(self):
        window = SettingsWindow(self.parent_window)
        window.transient(self)
        window.lift()

    def _exit(self):
        if messagebox.askyesno("Thoát", "Bạn có muốn thoát chương trình?", parent=self):
            self.parent_window.close_app(confirm=False)

    def destroy(self):
        if getattr(self.parent_window, "menu_window", None) is self:
            self.parent_window.menu_window = None
        super().destroy()

class SettingsWindow(tk.Toplevel):
    def __init__(self, parent: "MainWindow"):
        super().__init__(parent)
        self.parent_window = parent
        self.title("Cài đặt thiết bị")
        self.configure(bg=BG)
        sw, sh = self.winfo_screenwidth(), self.winfo_screenheight()
        self.ui_scale = getattr(parent, "ui_scale", max(0.70, min(1.35, min(sw / 1650, sh / 930))))
        self.geometry(f"{sw}x{sh}+0+0")
        self.attributes("-fullscreen", True)
        self.bind("<Escape>", lambda _e: self.destroy())
        apply_ttk_theme(self, self.ui_scale)

        cfg = parent.config
        self.baud = tk.IntVar(value=cfg.baudrate)
        self.maxext = tk.IntVar(value=cfg.maxext)
        self.fullscreen = tk.BooleanVar(value=cfg.fullscreen)
        self.auto_pen = tk.BooleanVar(value=cfg.auto_pass_pen)
        self.show_probe = tk.BooleanVar(value=cfg.show_pin_probe)
        self.pass_pen_delay = tk.IntVar(value=cfg.pass_pen_delay_ms)
        self.unconnect_delay = tk.IntVar(value=cfg.unconnect_delay_ms)
        self.marking_timeout = tk.IntVar(value=cfg.marking_timeout_ms)
        self.pass_action_delay = tk.IntVar(value=cfg.pass_action_delay_ms)
        self.next_cycle_delay = tk.IntVar(value=cfg.next_cycle_delay_ms)
        self.board_boot_delay = tk.IntVar(value=cfg.board_boot_stabilize_ms)
        self.table_refresh = tk.IntVar(value=cfg.table_refresh_ms)
        self.reconnect_delay = tk.IntVar(value=cfg.reconnect_delay_ms)
        self.probe_timeout = tk.IntVar(value=cfg.uart_probe_timeout_ms)
        self.default_pin_count = tk.IntVar(value=cfg.default_pin_count)
        self.last_lot = tk.StringVar(value=cfg.last_lot)
        self.models_dir = tk.StringVar(value=cfg.models_dir)
        self.setups_dir = tk.StringVar(value=cfg.setups_dir)

        self._build_header()
        self._build_body(cfg)
        self._build_footer()

    def _build_header(self):
        top = tk.Frame(self, bg=PANEL, highlightbackground=BORDER, highlightthickness=1)
        top.pack(fill="x")
        inner = tk.Frame(top, bg=PANEL)
        inner.pack(fill="x", padx=max(14, int(18 * self.ui_scale)), pady=max(8, int(10 * self.ui_scale)))

        flat_button(inner, "VỀ TRANG CHÍNH", self.destroy, self.ui_scale, width=18).pack(side="left", ipady=5)
        tk.Label(
            inner,
            text="CÀI ĐẶT CẤU HÌNH",
            bg=PANEL,
            fg=TEXT_DARK,
            font=scaled_font(self.ui_scale, 25, "bold"),
        ).pack(side="left", expand=True)
        tk.Label(
            inner,
            text="JBZ UART TESTER - SETTINGS",
            bg=PANEL,
            fg=MUTED,
            font=scaled_font(self.ui_scale, 12),
            width=26,
            anchor="e",
        ).pack(side="right")

    def _build_body(self, cfg: AppConfig):
        shell = tk.Frame(self, bg=BG)
        shell.pack(fill="both", expand=True)

        canvas = tk.Canvas(shell, bg=BG, bd=0, highlightthickness=0)
        scrollbar = ttk.Scrollbar(shell, orient="vertical", command=canvas.yview)
        canvas.configure(yscrollcommand=scrollbar.set)
        scrollbar.pack(side="right", fill="y")
        canvas.pack(side="left", fill="both", expand=True)

        content = tk.Frame(canvas, bg=BG)
        window_id = canvas.create_window((0, 0), window=content, anchor="nw")
        content.bind("<Configure>", lambda _e: canvas.configure(scrollregion=canvas.bbox("all")))
        canvas.bind("<Configure>", lambda e: canvas.itemconfigure(window_id, width=e.width))

        pad = max(10, int(12 * self.ui_scale))
        grid = tk.Frame(content, bg=BG)
        grid.pack(fill="both", expand=True, padx=pad, pady=pad)
        for column, weight in enumerate((1, 1, 1)):
            grid.columnconfigure(column, weight=weight, uniform="settings")

        device = self._panel(grid, "THIẾT BỊ / UART", 0, 0)
        self._entry_row(device, "Tốc độ UART", self.baud, "baud")
        self._entry_row(device, "MAXEXT", self.maxext, "")
        self._entry_row(device, "Số chân mặc định", self.default_pin_count, "pin")
        self._info_block(
            device,
            "UART: TỰ ĐỘNG QUÉT VÀ KẾT NỐI",
            f"Cổng hiện tại: {cfg.last_uart or 'đang tự tìm'}",
            STATUS_PASS,
        )

        timing = self._panel(grid, "THỜI GIAN / CHU TRÌNH", 0, 1)
        self._entry_row(timing, "Giữ relay marking", self.pass_pen_delay, "ms")
        self._entry_row(timing, "Chờ tháo dây", self.unconnect_delay, "ms")
        self._entry_row(timing, "Timeout marking", self.marking_timeout, "ms")
        self._entry_row(timing, "PASS action delay", self.pass_action_delay, "ms")
        self._entry_row(timing, "Delay chu kỳ mới", self.next_cycle_delay, "ms")
        self._entry_row(timing, "Ổn định boot bo", self.board_boot_delay, "ms")

        display = self._panel(grid, "GIAO DIỆN / VẬN HÀNH", 0, 2)
        self._check_row(display, "Toàn màn hình", self.fullscreen)
        self._check_row(display, "Tự động kích relay/van marking khi ĐẠT", self.auto_pen)
        self._check_row(display, "Hiển thị đầu dò GND", self.show_probe)
        self._entry_row(display, "Làm mới bảng lỗi", self.table_refresh, "ms")
        self._entry_row(display, "LOT gần nhất", self.last_lot, "")

        paths = self._panel(grid, "MODEL / SETUP", 1, 0, columnspan=2)
        self._path_row(paths, "Thư mục Models", self.models_dir, lambda: self._choose_dir(self.models_dir))
        self._path_row(paths, "Thư mục Setups", self.setups_dir, lambda: self._choose_dir(self.setups_dir))
        self._info_block(paths, "Nguồn mã hàng", "Phần mềm UART dùng file .model + .setup riêng của bo UART.", BRAND)

        service = self._panel(grid, "KẾT NỐI / BẢO TRÌ", 1, 2)
        self._entry_row(service, "Delay reconnect", self.reconnect_delay, "ms")
        self._entry_row(service, "Timeout dò UART", self.probe_timeout, "ms")
        self._info_block(
            service,
            "Config",
            "Lưu vào app.json trong thư mục cấu hình người dùng Windows.",
            MUTED,
        )

    def _build_footer(self):
        footer = tk.Frame(self, bg=PANEL, highlightbackground=BORDER, highlightthickness=1)
        footer.pack(side="bottom", fill="x")
        inner = tk.Frame(footer, bg=PANEL)
        inner.pack(fill="x", padx=max(10, int(14 * self.ui_scale)), pady=max(6, int(8 * self.ui_scale)))
        tk.Label(
            inner,
            text="Config saved: app.json - logic UART và model upload giữ nguyên",
            bg=PANEL,
            fg=MUTED,
            font=scaled_font(self.ui_scale, 11),
            anchor="w",
        ).pack(side="left", fill="x", expand=True)
        flat_button(
            inner,
            "LƯU CÀI ĐẶT",
            self._save,
            self.ui_scale,
            bg="#77d77a",
            fg=TEXT_DARK,
            font=scaled_font(self.ui_scale, 14, "bold"),
            width=20,
        ).pack(side="left", padx=4, ipady=7)
        flat_button(
            inner,
            "TRỞ VỀ",
            self.destroy,
            self.ui_scale,
            font=scaled_font(self.ui_scale, 14, "bold"),
            width=14,
        ).pack(side="left", padx=4, ipady=7)

    def _panel(self, parent, title: str, row: int, column: int, columnspan: int = 1) -> tk.Frame:
        panel = tk.Frame(parent, bg=PANEL, highlightbackground=BORDER, highlightthickness=1)
        panel.grid(
            row=row,
            column=column,
            columnspan=columnspan,
            sticky="nsew",
            padx=max(5, int(6 * self.ui_scale)),
            pady=max(5, int(6 * self.ui_scale)),
        )
        panel.columnconfigure(1, weight=1)
        tk.Label(
            panel,
            text=title,
            bg=PANEL,
            fg=BRAND,
            font=scaled_font(self.ui_scale, 16, "bold"),
            anchor="w",
        ).grid(row=0, column=0, columnspan=3, sticky="ew", padx=14, pady=(12, 8))
        panel._next_row = 1
        return panel

    def _entry_row(self, parent, label: str, variable, unit: str):
        row = parent._next_row
        parent._next_row += 1
        tk.Label(parent, text=label, bg=PANEL, fg=MUTED, font=scaled_font(self.ui_scale, 13), anchor="w").grid(
            row=row, column=0, sticky="ew", padx=(14, 8), pady=4
        )
        entry = tk.Entry(
            parent,
            textvariable=variable,
            bg=FIELD,
            fg=TEXT_DARK,
            relief="solid",
            bd=1,
            font=scaled_font(self.ui_scale, 13),
        )
        entry.grid(row=row, column=1, sticky="ew", pady=4, ipady=max(4, int(5 * self.ui_scale)))
        tk.Label(parent, text=unit, bg=PANEL, fg=MUTED, font=scaled_font(self.ui_scale, 12), width=7, anchor="w").grid(
            row=row, column=2, sticky="w", padx=(8, 14), pady=4
        )

    def _path_row(self, parent, label: str, variable: tk.StringVar, command):
        row = parent._next_row
        parent._next_row += 1
        tk.Label(parent, text=label, bg=PANEL, fg=MUTED, font=scaled_font(self.ui_scale, 13), anchor="w").grid(
            row=row, column=0, sticky="ew", padx=(14, 8), pady=4
        )
        tk.Entry(
            parent,
            textvariable=variable,
            bg=FIELD,
            fg=TEXT_DARK,
            relief="solid",
            bd=1,
            font=scaled_font(self.ui_scale, 12),
        ).grid(row=row, column=1, sticky="ew", pady=4, ipady=max(4, int(5 * self.ui_scale)))
        flat_button(parent, "CHỌN", command, self.ui_scale, width=8, font=scaled_font(self.ui_scale, 11)).grid(
            row=row, column=2, sticky="ew", padx=(8, 14), pady=4
        )

    def _check_row(self, parent, label: str, variable: tk.BooleanVar):
        row = parent._next_row
        parent._next_row += 1
        tk.Checkbutton(
            parent,
            text=label,
            variable=variable,
            bg=PANEL,
            fg=TEXT,
            activebackground=PANEL,
            activeforeground=TEXT_DARK,
            selectcolor=FIELD,
            font=scaled_font(self.ui_scale, 13),
            anchor="w",
        ).grid(row=row, column=0, columnspan=3, sticky="ew", padx=12, pady=4)

    def _info_block(self, parent, title: str, text: str, color: str):
        row = parent._next_row
        parent._next_row += 1
        box = tk.Frame(parent, bg="#f7f9fc", highlightbackground=GRID, highlightthickness=1)
        box.grid(row=row, column=0, columnspan=3, sticky="ew", padx=14, pady=(8, 12))
        tk.Label(box, text=title, bg="#f7f9fc", fg=color, font=scaled_font(self.ui_scale, 12, "bold"), anchor="w").pack(
            fill="x", padx=9, pady=(7, 2)
        )
        tk.Label(box, text=text, bg="#f7f9fc", fg=MUTED, font=scaled_font(self.ui_scale, 11), anchor="w", justify="left").pack(
            fill="x", padx=9, pady=(0, 7)
        )

    def _choose_dir(self, variable: tk.StringVar):
        selected = filedialog.askdirectory(parent=self, initialdir=variable.get() or str(Path.home()))
        if selected:
            variable.set(selected)

    def _save(self):
        try:
            cfg = self.parent_window.config
            cfg.baudrate = max(1200, int(self.baud.get()))
            cfg.maxext = max(0, int(self.maxext.get()))
            cfg.fullscreen = bool(self.fullscreen.get())
            cfg.auto_pass_pen = bool(self.auto_pen.get())
            cfg.show_pin_probe = bool(self.show_probe.get())
            cfg.pass_pen_delay_ms = max(50, int(self.pass_pen_delay.get()))
            cfg.unconnect_delay_ms = max(50, int(self.unconnect_delay.get()))
            cfg.marking_timeout_ms = max(500, int(self.marking_timeout.get()))
            cfg.pass_action_delay_ms = max(0, int(self.pass_action_delay.get()))
            cfg.next_cycle_delay_ms = max(20, int(self.next_cycle_delay.get()))
            cfg.board_boot_stabilize_ms = max(0, int(self.board_boot_delay.get()))
            cfg.table_refresh_ms = max(20, int(self.table_refresh.get()))
            cfg.reconnect_delay_ms = max(100, int(self.reconnect_delay.get()))
            cfg.uart_probe_timeout_ms = max(1800 if IS_WINDOWS else 80, int(self.probe_timeout.get()))
            cfg.default_pin_count = max(1, int(self.default_pin_count.get()))
            cfg.last_lot = self.last_lot.get().strip()
            cfg.models_dir = self.models_dir.get().strip()
            cfg.setups_dir = self.setups_dir.get().strip()
        except (tk.TclError, ValueError) as exc:
            messagebox.showerror("Cấu hình chưa hợp lệ", f"Giá trị số không hợp lệ.\n\n{exc}", parent=self)
            return

        save_config(cfg)
        self.parent_window.attributes("-fullscreen", cfg.fullscreen)
        self.parent_window.disconnect_board()
        self.parent_window.connect_board_async()
        self.destroy()


class DiagnosticWindow(tk.Toplevel):
    def __init__(self, parent: "MainWindow"):
        super().__init__(parent)
        self.parent_window = parent
        self.title("Kiểm tra chân pin")
        self.configure(bg=BG)
        sw, sh = self.winfo_screenwidth(), self.winfo_screenheight()
        self.ui_scale = getattr(parent, "ui_scale", max(0.70, min(1.35, min(sw / 1650, sh / 930))))
        self.geometry(f"{sw}x{sh}+0+0")
        self.attributes("-fullscreen", True)
        self.bind("<Escape>", lambda _e: self.destroy())
        apply_ttk_theme(self, self.ui_scale)

        top = tk.Frame(self, bg=BG)
        top.pack(fill="x", padx=max(16, int(24 * self.ui_scale)), pady=max(12, int(18 * self.ui_scale)))
        flat_button(top, "VỀ TRANG CHÍNH", self.destroy, self.ui_scale, width=16).pack(side="left", ipady=5)
        tk.Label(top, text="KIỂM TRA CHÂN PIN / CHẨN ĐOÁN BO", bg=BG, fg=TEXT, font=scaled_font(self.ui_scale, 25)).pack(side="left", expand=True)
        tk.Label(top, text="", bg=BG, width=16).pack(side="right")

        notebook = ttk.Notebook(self, style="TesterVN.TNotebook")
        notebook.pack(fill="both", expand=True, padx=max(16, int(24 * self.ui_scale)), pady=(0, max(12, int(18 * self.ui_scale))))
        inp = tk.Frame(notebook, bg=BG)
        ana = tk.Frame(notebook, bg=BG)
        out = tk.Frame(notebook, bg=BG)
        notebook.add(inp, text="INPUT")
        notebook.add(ana, text="ANALOG RAW")
        notebook.add(out, text="OUTPUT")
        self.output_labels: dict[int, tk.Label] = {}
        self.input_labels: dict[int, tk.Label] = {}
        self.analog_labels: dict[tuple[str, int], tk.Label] = {}

        for ch in range(4):
            tk.Label(inp, text=f"INPUT {ch}", bg=BG, fg=TEXT, font=scaled_font(self.ui_scale, 14)).grid(row=ch, column=0, padx=25, pady=15, sticky="e")
            label = value_label(inp, text="-", scale=self.ui_scale, font=scaled_font(self.ui_scale, 14, "bold"), width=14, anchor="center")
            label.grid(row=ch, column=1, padx=10, sticky="ew", ipady=6)
            self.input_labels[ch] = label
            flat_button(inp, "ĐỌC", lambda c=ch: self._input(c), self.ui_scale, width=12).grid(row=ch, column=2, padx=10, ipady=5)
        inp.columnconfigure(1, weight=1)

        for row, family in enumerate(("RESISTOR", "AMPARE", "VOLTAGE")):
            tk.Label(ana, text=family, bg=BG, fg=TEXT, font=scaled_font(self.ui_scale, 13, "bold")).grid(row=row, column=0, padx=12, pady=18)
            for ch in range(8):
                label = value_label(ana, text="-", scale=self.ui_scale, width=8, anchor="center", font=scaled_font(self.ui_scale, 11))
                label.grid(row=row, column=ch + 1, padx=3, ipady=5)
                self.analog_labels[(family, ch)] = label
            flat_button(ana, "SCAN 0-7", lambda f=family: self._analog(f), self.ui_scale, width=11).grid(row=row, column=9, padx=10, ipady=4)

        for ch in range(5):
            tk.Label(out, text=f"OUTPUT {ch}", bg=BG, fg=TEXT, font=scaled_font(self.ui_scale, 14)).grid(row=ch, column=0, padx=20, pady=16, sticky="e")
            status = value_label(out, text="OFF", scale=self.ui_scale, width=12, anchor="center", font=scaled_font(self.ui_scale, 13, "bold"))
            status.grid(row=ch, column=1, padx=8, ipady=5)
            self.output_labels[ch] = status
            flat_button(out, "ON", lambda c=ch: self._output(c, True), self.ui_scale, width=10).grid(row=ch, column=2, padx=8, ipady=5)
            flat_button(out, "OFF", lambda c=ch: self._output(c, False), self.ui_scale, width=10).grid(row=ch, column=3, padx=8, ipady=5)

    def _run(self, function):
        threading.Thread(target=function, daemon=True).start()

    def _input(self, channel):
        def worker():
            try:
                event = self.parent_window.board.input_test(channel)
                state = event.values[1]
                self.after(0, lambda: self.input_labels[channel].configure(text=state, bg="#64db64" if state == "ON" else "#ff7777"))
            except Exception as exc:
                self.after(0, lambda: messagebox.showerror("INPUT", str(exc), parent=self))
        self._run(worker)

    def _analog(self, family):
        def worker():
            for channel in range(8):
                try:
                    event = self.parent_window.board.measure(family, channel)
                    value = event.values[0]
                    self.after(0, lambda c=channel, v=value: self.analog_labels[(family, c)].configure(text=str(v)))
                except Exception as exc:
                    self.after(0, lambda: messagebox.showerror(family, str(exc), parent=self))
                    return
        self._run(worker)

    def _output(self, channel, state):
        def worker():
            try:
                event = self.parent_window.board.output_test(channel, state)
                text = event.values[1]
                self.after(0, lambda: self.output_labels[channel].configure(text=text, bg="#64db64" if text == "ON" else "#dddddd"))
            except Exception as exc:
                self.after(0, lambda: messagebox.showerror("OUTPUT", str(exc), parent=self))
        self._run(worker)


class MainWindow(tk.Tk):
    def __init__(self):
        super().__init__()
        self.config_data = load_config()
        self.config = self.config_data
        _configure_model_directories(self.config)
        self.title("UniversalTester V 1.42 - JBZ Production VI V10")
        self.configure(bg=BG)
        screen_w, screen_h = self.winfo_screenwidth(), self.winfo_screenheight()
        self.ui_scale = max(0.70, min(1.35, min(screen_w / 1650, screen_h / 930)))
        self.attributes("-fullscreen", True)
        self.config.fullscreen = True
        self.geometry(f"{screen_w}x{screen_h}+0+0")
        self.minsize(1024, 600)
        self.bind("<F11>", self._toggle_fullscreen)
        # Escape không thoát fullscreen trong chế độ sản xuất.
        self.bind("<Escape>", lambda _e: None)
        self.bind("<F5>", lambda _e: self.start_test())
        self.protocol("WM_DELETE_WINDOW", self.close_app)

        self.events: queue.Queue[tuple[str, object]] = queue.Queue()
        self.board: BoardController | None = None
        self.product: ProductModel | None = None
        self.setup: SetupData | None = None
        self.current_model_code = ""
        self.expected_board_model_code = ""
        self.model_verified = False
        # Lưu IDN firmware khi kết nối, nhưng chỉ hiển thị sau khi tải model thành công.
        self.firmware_version = ""
        self.firmware_reveal_after_upload = False
        self.cycle_state = TestCycleState()
        # Alias giữ tương thích phần lưu raw_summary. Nội dung do cycle_state quản lý.
        self.current_cycle_raw = self.cycle_state.raw_lines
        self.cycle_open_count = 0
        self.cycle_other_count = 0
        self.testing = False
        self.test_phase = "IDLE"
        self.waiting_unconnect_reason: str | None = None
        self.pending_table_refresh = False
        self.table_refresh_after_id: str | None = None
        self.marking_phase = "IDLE"
        self.marking_token = 0
        self.reject_dialog_open = False
        self.auto_start_pending = False
        self.returning_to_menu = False
        self.reject_restart_token = 0
        self.menu_window: MainMenuWindow | None = None
        self.test_screen_active = False
        self.connecting_board = False
        self.reconnect_attempt = 0
        self.reconnect_after_id: str | None = None
        self.app_closing = False
        self._config_lock = threading.Lock()
        self.probe_tracker = PinProbeTracker()
        self.store = ResultStore(DATABASE_PATH)

        self.lot_var = tk.StringVar(value=self.config.last_lot or datetime.now().strftime("%y%m"))
        self.open_var = tk.IntVar(value=0)
        self.other_var = tk.IntVar(value=0)
        self.reject_var = tk.IntVar(value=0)
        self.total_var = tk.IntVar(value=0)
        self.pass_var = tk.IntVar(value=0)
        self.fail_var = tk.IntVar(value=0)
        self.rate_var = tk.StringVar(value="0.00 %")
        self.title_var = tk.StringVar(value="UNIVERSAL TESTER")
        self.product_no_var = tk.StringVar(value="")
        self.product_name_var = tk.StringVar(value="")
        self.vehicle_var = tk.StringVar(value="")
        self.customer_var = tk.StringVar(value="")
        self.status_var = tk.StringVar(value="SẴN SÀNG")
        self.board_info_var = tk.StringVar(value="CHƯA KẾT NỐI")
        self.probe_status_var = tk.StringVar(value="ĐẦU DÒ GND: SẴN SÀNG KHI ĐANG ĐO")
        self.build_ui()
        self.after(80, self.poll_events)
        self.withdraw()
        self.after(150, self.show_main_menu)
        self.after(250, self.connect_board_async)

    def _toggle_fullscreen(self, _event=None):
        self.config.fullscreen = not bool(self.attributes("-fullscreen"))
        self.attributes("-fullscreen", self.config.fullscreen)
        save_config(self.config)

    def _font(self, size: int, weight: str | None = None):
        scaled = max(8, int(size * self.ui_scale))
        return scaled_font(self.ui_scale, size, weight)

    def refresh_menu_window(self):
        if self.menu_window and self.menu_window.winfo_exists():
            self.menu_window.refresh()

    def show_main_menu(self):
        """Hiển thị menu chức năng; không tự mở ô nhập model."""
        self.test_screen_active = False
        self.withdraw()
        if self.menu_window and self.menu_window.winfo_exists():
            self.menu_window.refresh()
            self.menu_window.deiconify()
            self.menu_window.lift()
            self.menu_window.focus_force()
            return
        self.menu_window = MainMenuWindow(self)
        self.menu_window.lift()
        self.menu_window.focus_force()

    def enter_test_screen(self, menu_owner=None):
        """Chỉ vào màn hình test sau khi người vận hành bấm KIỂM TRA."""
        if not self.product:
            messagebox.showwarning("Mã hàng", "Hãy bấm NHẬP MODEL và tải mã hàng trước.", parent=menu_owner or self)
            return
        if not self.model_verified:
            messagebox.showwarning(
                "Chưa xác nhận model",
                "Phần mềm chưa đọc lại được đúng model từ bo. Hãy chờ kết nối hoặc tải lại model.",
                parent=menu_owner or self,
            )
            return
        if self.menu_window and self.menu_window.winfo_exists():
            self.menu_window.destroy()
        self.test_screen_active = True
        self.deiconify()
        self.attributes("-fullscreen", True)
        self.lift()
        self.focus_force()
        self._clear_cycle_display()
        self.test_phase = "READY"
        self.waiting_unconnect_reason = None
        self._set_status("SẴN SÀNG", PURPLE)
        if not self.board or not self.board.connected:
            self.auto_start_pending = True
            self.connect_board_async()
        else:
            self.auto_start_pending = False
            self.after(300, lambda: (self.test_screen_active and not self.testing) and self.start_test())

    def build_ui(self):
        apply_ttk_theme(self, self.ui_scale)
        top_h = max(150, int(210 * self.ui_scale))
        left_w = max(165, int(205 * self.ui_scale))
        right_w = max(310, int(390 * self.ui_scale))

        self.top = tk.Frame(self, bg=BG, height=top_h)
        self.top.pack(fill="x", side="top", padx=max(8, int(12 * self.ui_scale)), pady=(max(7, int(10 * self.ui_scale)), 0))
        self.top.pack_propagate(False)

        left = tk.Frame(self.top, bg=BG, width=left_w)
        left.pack(side="left", fill="y", padx=(0, max(5, int(8 * self.ui_scale))))
        left.pack_propagate(False)
        flat_button(
            left,
            "VỀ TRANG CHÍNH",
            self.return_to_main_menu,
            self.ui_scale,
            font=self._font(11),
        ).pack(fill="x", ipady=max(4, int(6 * self.ui_scale)), pady=(0, max(4, int(7 * self.ui_scale))))
        self._counter_row(left, "Số LOT", self.lot_var, editable=True)
        self._counter_row(left, "Số lỗi hở mạch", self.open_var, color=STATUS_FAIL)
        self._counter_row(left, "Số lỗi đấu sai", self.other_var, color=STATUS_FAIL)
        self._counter_row(left, "Số lỗi", self.reject_var, color=BRAND)

        middle = tk.Frame(self.top, bg=BG)
        middle.pack(side="left", fill="both", expand=True, padx=max(5, int(8 * self.ui_scale)))
        tk.Label(
            middle,
            textvariable=self.title_var,
            bg=BG,
            fg=TEXT,
            font=self._font(28),
            justify="center",
        ).pack(pady=(max(2, int(4 * self.ui_scale)), max(7, int(11 * self.ui_scale))))
        info = tk.Frame(middle, bg=BG)
        info.pack(fill="x", padx=max(5, int(10 * self.ui_scale)))
        self._info_field(info, 0, 0, "Mã sản phẩm", self.product_no_var)
        self._info_field(info, 0, 2, "Loại xe", self.vehicle_var)
        self._info_field(info, 1, 0, "Tên sản phẩm", self.product_name_var)
        self._info_field(info, 1, 2, "Mã khách hàng", self.customer_var)

        right = tk.Frame(self.top, bg=BG, width=right_w)
        right.pack(side="right", fill="y", padx=(max(5, int(8 * self.ui_scale)), 0))
        right.pack_propagate(False)
        status_w = max(145, int(185 * self.ui_scale))
        status_h = max(70, int(92 * self.ui_scale))
        status = tk.Label(
            right,
            textvariable=self.status_var,
            bg=STATUS_READY,
            fg="white",
            font=self._font(21, "bold"),
            relief="flat",
            bd=0,
            wraplength=status_w - 10,
            justify="center",
            cursor="hand2",
        )
        status.place(x=0, y=0, width=status_w, height=status_h)
        self.status_label = status
        self._right_counter(right, 0, "Tổng số", self.total_var, TEXT)
        self._right_counter(right, 1, "Số đạt", self.pass_var, STATUS_PASS)
        self._right_counter(right, 2, "Số lỗi", self.fail_var, STATUS_FAIL)
        label_x = status_w + max(7, int(10 * self.ui_scale))
        label_w = max(55, int(70 * self.ui_scale))
        value_x = label_x + label_w + max(4, int(6 * self.ui_scale))
        y_rate = max(int(140 * self.ui_scale), status_h + int(12 * self.ui_scale))
        tk.Label(right, text="Tỷ lệ đạt", bg=BG, fg=TEXT, font=self._font(12), anchor="e").place(
            x=label_x, y=y_rate, width=label_w, height=max(30, int(38 * self.ui_scale))
        )
        value_label(
            right,
            variable=self.rate_var,
            scale=self.ui_scale,
            fg=TEXT,
            font=self._font(15),
            anchor="center",
        ).place(
            x=value_x, y=y_rate, width=max(85, int(110 * self.ui_scale)), height=max(30, int(38 * self.ui_scale))
        )

        probe_h = max(34, int(44 * self.ui_scale))
        probe_bar = tk.Frame(self, bg=HEADER, height=probe_h, bd=1, relief="solid")
        probe_bar.pack(fill="x", padx=max(8, int(12 * self.ui_scale)), pady=(max(3, int(5 * self.ui_scale)), max(3, int(5 * self.ui_scale))))
        probe_bar.pack_propagate(False)
        tk.Label(
            probe_bar,
            text="ĐẦU DÒ GND",
            bg=HEADER,
            fg=TEXT_DARK,
            font=self._font(11, "bold"),
            relief="flat",
            bd=0,
        ).pack(side="left", fill="y", ipadx=max(8, int(12 * self.ui_scale)))
        self.probe_status_label = tk.Label(
            probe_bar,
            textvariable=self.probe_status_var,
            bg=FIELD,
            fg=TEXT,
            font=self._font(11),
            anchor="w",
            padx=max(8, int(12 * self.ui_scale)),
        )
        self.probe_status_label.pack(side="left", fill="both", expand=True)
        self.probe_button = flat_button(
            probe_bar,
            "ẨN DÒ CHÂN",
            self.toggle_pin_probe,
            self.ui_scale,
            font=self._font(10),
        )
        self.probe_button.pack(side="right", fill="y", ipadx=max(7, int(10 * self.ui_scale)))

        self.fault_table = FaultTable(self, ui_scale=self.ui_scale)
        self.fault_table.pack(fill="both", expand=True, padx=max(8, int(12 * self.ui_scale)), pady=(0, max(8, int(12 * self.ui_scale))))

    def _counter_row(self, parent, label, variable, color=TEXT, editable=False):
        row = tk.Frame(parent, bg=BG)
        row.pack(fill="x", pady=max(1, int(2 * self.ui_scale)))
        tk.Label(row, text=label, bg=BG, fg=TEXT, font=self._font(11), anchor="w").pack(side="left", fill="x", expand=True)
        if editable:
            widget = tk.Entry(
                row,
                textvariable=variable,
                bg=FIELD,
                fg=color,
                font=self._font(13),
                justify="center",
                relief="solid",
                bd=1,
            )
            widget.bind("<FocusOut>", lambda _e: self.refresh_summary())
        else:
            widget = value_label(
                row,
                variable=variable,
                scale=self.ui_scale,
                fg=color,
                font=self._font(13),
                anchor="center",
            )
        widget.pack(side="right", ipady=max(2, int(3 * self.ui_scale)))
        widget.configure(width=7)

    def _info_field(self, parent, row, column, label, variable, span=1):
        tk.Label(parent, text=label, bg=BG, fg=TEXT, font=self._font(11), anchor="e").grid(
            row=row, column=column, padx=(4, 7), pady=max(2, int(4 * self.ui_scale)), sticky="e"
        )
        value_label(
            parent,
            variable=variable,
            scale=self.ui_scale,
            font=self._font(12),
            anchor="w",
        ).grid(
            row=row,
            column=column + 1,
            columnspan=span,
            padx=(0, max(7, int(12 * self.ui_scale))),
            pady=max(2, int(4 * self.ui_scale)),
            sticky="ew",
            ipady=max(3, int(5 * self.ui_scale)),
        )
        parent.columnconfigure(column + 1, weight=1)

    def _right_counter(self, parent, index, text, variable, color):
        status_w = max(145, int(185 * self.ui_scale))
        label_x = status_w + max(7, int(10 * self.ui_scale))
        label_w = max(55, int(70 * self.ui_scale))
        value_x = label_x + label_w + max(4, int(6 * self.ui_scale))
        value_w = max(85, int(110 * self.ui_scale))
        row_h = max(29, int(36 * self.ui_scale))
        y = index * max(35, int(44 * self.ui_scale))
        tk.Label(parent, text=text, bg=BG, fg=TEXT, font=self._font(12), anchor="e").place(
            x=label_x, y=y, width=label_w, height=row_h
        )
        value_label(
            parent,
            variable=variable,
            scale=self.ui_scale,
            fg=color,
            font=self._font(15),
            anchor="center",
        ).place(x=value_x, y=y, width=value_w, height=row_h)

    def remember_uart(self, port: str) -> None:
        port = str(port or "").strip()
        if not port or self.config.last_uart == port:
            return
        with self._config_lock:
            self.config.last_uart = port
            save_config(self.config)

    def connect_board_async(self):
        if self.app_closing or self.connecting_board:
            return
        if self.board and self.board.connected:
            return
        if self.reconnect_after_id:
            try:
                self.after_cancel(self.reconnect_after_id)
            except tk.TclError:
                pass
            self.reconnect_after_id = None
        self.connecting_board = True
        self.board_info_var.set("ĐANG TỰ TÌM BO UNIVERSAL TESTER...")
        threading.Thread(target=self._connect_worker, daemon=True).start()

    def _connect_worker(self):
        try:
            manager = UartManager(
                baudrate=self.config.baudrate,
                preferred_port=self.config.last_uart,
                on_port_found=self.remember_uart,
                log_callback=lambda line: self.events.put(("log", line)),
                probe_timeout=self.config.uart_probe_timeout_ms / 1000.0,
            )
            found = manager.discover()
            if self.board:
                try:
                    self.board.disconnect()
                except Exception:
                    pass
            board = BoardController(
                found.port,
                self.config.baudrate,
                LOG_DIR,
                lambda event: self.events.put(("board_event", event)),
                lambda line: self.events.put(("log", line)),
                lambda exc: self.events.put(("connection_lost", str(exc or "Mất UART"))),
            )
            idn, model_response = board.connect((found.idn, found.model_response))
            self.board = board
            self.events.put(("connected", (idn, model_response, found.port)))
        except Exception as exc:
            try:
                if self.board:
                    self.board.disconnect()
            except Exception:
                pass
            self.board = None
            self.events.put(("connect_error", str(exc)))
        finally:
            self.connecting_board = False

    def _schedule_reconnect(self):
        if self.app_closing or self.connecting_board or (self.board and self.board.connected):
            return
        if self.reconnect_after_id:
            return
        base = max(100, int(self.config.reconnect_delay_ms))
        delay = min(2000, base * (2 ** min(self.reconnect_attempt, 3)))
        self.reconnect_attempt += 1
        self.reconnect_after_id = self.after(delay, self._run_scheduled_reconnect)

    def _run_scheduled_reconnect(self):
        self.reconnect_after_id = None
        self.connect_board_async()

    def disconnect_board(self):
        if self.reconnect_after_id:
            try:
                self.after_cancel(self.reconnect_after_id)
            except tk.TclError:
                pass
            self.reconnect_after_id = None
        if self.board:
            try:
                self.board.disconnect()
            except Exception:
                pass
            self.board = None

    def set_loaded_model(self, model: ProductModel, setup: SetupData, verified: str | None):
        self.product = model
        self.setup = setup
        self.current_model_code = _model_code_from_response(verified) or model.code
        self.expected_board_model_code = model.code
        self.model_verified = bool(
            verified
            and _normalized_model_code(_model_code_from_response(verified))
            == _normalized_model_code(model.code)
        )
        self.apply_product()
        self.refresh_summary()

    def apply_product(self):
        if not self.product:
            return
        product = self.product
        self.title_var.set(product.title or product.code)
        self.product_no_var.set(product.product_no)
        self.product_name_var.set(product.product_name)
        self.vehicle_var.set(product.vehicle)
        self.customer_var.set(product.customer_no)

    def load_current_model(self, code: str):
        try:
            files = _resolve_model_files_for_user(code, self.config)
            self.product = load_product_model(files.model_path)
            self.setup = load_setup(files.setup_path)
            self.current_model_code = self.product.code or code
            self.apply_product()
        except Exception as exc:
            self.events.put(("log", f"MODEL LOAD ERROR {exc}"))
        self.refresh_summary()

    def refresh_summary(self):
        model = self.current_model_code or (self.product.code if self.product else "")
        lot = self.lot_var.get().strip()
        if not model:
            return
        summary = self.store.summary(lot, model)
        self.total_var.set(summary.total)
        self.pass_var.set(summary.passed)
        self.fail_var.set(summary.failed)
        self.reject_var.set(summary.failed)
        self.rate_var.set(f"{summary.rate:.2f} %")
        self.config.last_lot = lot
        save_config(self.config)

    def open_model_loader(self):
        """Giữ tương thích lời gọi cũ nhưng mở loader nội tuyến trên menu."""
        self.show_main_menu()
        if self.menu_window and self.menu_window.winfo_exists():
            self.menu_window._open_loader()

    def _clear_cycle_display(self):
        self.cycle_state.clear()
        self.cycle_open_count = 0
        self.cycle_other_count = 0
        self.open_var.set(0)
        self.other_var.set(0)
        self.fault_table.clear()
        self.probe_tracker.clear()
        self._update_probe_status()
        self.pending_table_refresh = False
        if self.table_refresh_after_id:
            try:
                self.after_cancel(self.table_refresh_after_id)
            except tk.TclError:
                pass
            self.table_refresh_after_id = None

    def _schedule_table_refresh(self, immediate: bool = False):
        if immediate:
            if self.table_refresh_after_id:
                try:
                    self.after_cancel(self.table_refresh_after_id)
                except tk.TclError:
                    pass
                self.table_refresh_after_id = None
            self.pending_table_refresh = False
            self._refresh_fault_table()
            return
        if self.pending_table_refresh:
            return
        self.pending_table_refresh = True
        delay = max(20, int(self.config.table_refresh_ms))
        self.table_refresh_after_id = self.after(delay, self._refresh_fault_table)

    def _refresh_fault_table(self):
        self.pending_table_refresh = False
        self.table_refresh_after_id = None
        rows: list[FaultRow] = []

        # Đấu sai luôn đứng đầu. Mỗi cặp chỉ hiện đúng hai dòng S/E,
        # dù firmware gửi thêm bản tin đối xứng E/S.
        for source, target in self.cycle_state.wrong_wiring.pairs.values():
            for label, physical in (("Đấu sai S", source), ("Đấu sai E", target)):
                pin = self.product.pin(physical) if self.product else None
                rows.append(FaultRow(
                    kind=label,
                    io=str(physical),
                    connector=pin.connector if pin else "",
                    pin=pin.local_pin if pin else "",
                    line=pin.line_name if pin else "",
                    splice_wire=pin.splice_wire if pin else "",
                    color_text=pin.color_text if pin else "",
                    gauge=pin.gauge if pin else "",
                    colors=pin.color_tokens if pin else (),
                    style="wrong",
                ))

        # Đầu dò là sự kiện tự phát từ firmware. Có thể có nhiều chân ON
        # đồng thời; chân chạm mới nhất đứng trước. Đấu sai vẫn ưu tiên cao hơn.
        if self.config.show_pin_probe:
            for physical in self.probe_tracker.ordered_pins():
                pin = self.product.pin(physical) if self.product else None
                rows.append(FaultRow(
                    kind="Đầu dò GND",
                    io=str(physical),
                    connector=pin.connector if pin else "",
                    pin=pin.local_pin if pin else "",
                    line=pin.line_name if pin else "",
                    splice_wire=pin.splice_wire if pin else "",
                    color_text=pin.color_text if pin else "",
                    gauge=pin.gauge if pin else "",
                    colors=pin.color_tokens if pin else (),
                    style="probe",
                ))

        # Mỗi mạng đang hở luôn hiển thị dòng nguồn S trước, sau đó là tất
        # cả chân đích còn hở. Topology lấy từ file model nên kể cả firmware
        # chỉ gửi :OPEN,<source>,<source>, bảng vẫn phục hồi đủ các nhánh.
        topology = self.product.network_topology() if self.product else {}
        for network_id in sorted(self.cycle_state.open_networks.active):
            physicals = self.cycle_state.open_networks.display_pins(
                network_id, topology.get(network_id, ())
            )
            for row_index, physical in enumerate(physicals):
                pin = self.product.pin(physical) if self.product else None
                rows.append(FaultRow(
                    kind="Đầu dây S" if row_index == 0 else "Hở mạch",
                    io=str(physical),
                    connector=pin.connector if pin else "",
                    pin=pin.local_pin if pin else "",
                    line=pin.line_name if pin else "",
                    splice_wire=pin.splice_wire if pin else "",
                    color_text=pin.color_text if pin else "",
                    gauge=pin.gauge if pin else "",
                    colors=pin.color_tokens if pin else (),
                ))

        self.cycle_open_count = self.cycle_state.open_count_for(topology)
        self.cycle_other_count = self.cycle_state.other_count
        self.open_var.set(self.cycle_open_count)
        self.other_var.set(self.cycle_other_count)
        self.fault_table.set_rows(rows)

    def _probe_description(self, physical: int) -> str:
        pin = self.product.pin(physical) if self.product else None
        if not pin:
            return f"ĐÃ CHẠM I/O {physical} - KHÔNG CÓ TRONG FILE MODEL"
        wire_color = color_label(pin.color_text, pin.color_tokens) or "-"
        splice_wire = pin.splice_wire or "-"
        gauge = pin.gauge or "-"
        line = pin.line_name or "-"
        return (
            f"I/O {physical}   |   Giắc {pin.connector or '-'}   |   "
            f"Chân {pin.local_pin or '-'}   |   Dây {line}   |   "
            f"Dập nối {splice_wire}   |   Tiết diện {gauge}   |   Màu {wire_color}"
        )

    def _update_probe_status(self) -> None:
        if not hasattr(self, "probe_status_label"):
            return
        if self.config.show_pin_probe and self.probe_tracker.active:
            ordered = self.probe_tracker.ordered_pins()
            newest = ordered[0]
            suffix = f"   |   +{len(ordered) - 1} chân đang chạm" if len(ordered) > 1 else ""
            self.probe_status_var.set(self._probe_description(newest) + suffix)
            self.probe_status_label.configure(bg=CYAN, fg="#111111")
        elif self.config.show_pin_probe and self.test_screen_active:
            self.probe_status_var.set(
                "SẴN SÀNG - BO TỰ PHÁT HIỆN ĐẦU DÒ TRONG CHU KỲ KIỂM TRA"
            )
            self.probe_status_label.configure(bg="#f5f5f5", fg="#333333")
        else:
            self.probe_status_var.set("ĐẦU DÒ GND: ĐANG ẨN")
            self.probe_status_label.configure(bg="#f5f5f5", fg="#777777")
        if hasattr(self, "probe_button"):
            self.probe_button.configure(
                text="ẨN DÒ CHÂN" if self.config.show_pin_probe else "HIỆN DÒ CHÂN",
                bg="#78d878" if self.config.show_pin_probe else "#dedede",
            )

    def toggle_pin_probe(self) -> None:
        """Chỉ ẩn/hiện thông tin đầu dò; không gửi lệnh xuống bo.

        Trace thật không có lệnh ``:PINTEST``. Firmware tự phát ``TESTPIN``
        sau START, vì vậy nút này tuyệt đối không thay đổi trạng thái bo.
        """
        self.config.show_pin_probe = not bool(self.config.show_pin_probe)
        if not self.config.show_pin_probe:
            self.probe_tracker.clear()
        save_config(self.config)
        self._update_probe_status()
        self._schedule_table_refresh(immediate=True)

    def _handle_test_pin(self, event: BoardEvent) -> None:
        if not event.values:
            return
        physical = int(event.values[0])
        state = str(event.values[1] if len(event.values) > 1 else "ON").upper()
        changed = self.probe_tracker.update(physical, state)
        if not changed:
            return
        self._update_probe_status()
        # Cập nhật ngay để người vận hành thấy pin vừa chạm; không chờ chu kỳ
        # gom OPEN 60 ms. Nhiều TESTPIN ON đồng thời đều được giữ lại.
        self._schedule_table_refresh(immediate=True)

    def return_to_main_menu(self):
        """Dừng sạch chu kỳ test rồi về menu chức năng, không mở ô nhập model."""
        if self.returning_to_menu:
            return
        self.returning_to_menu = True
        self.testing = False
        self.test_phase = "STOPPING"
        self.waiting_unconnect_reason = None
        self.test_screen_active = False
        self.marking_phase = "IDLE"
        self.marking_token += 1
        self.auto_start_pending = False
        self.probe_tracker.clear()
        self._update_probe_status()
        self._set_status("ĐANG DỪNG", "#777777")

        def worker():
            try:
                if self.board and self.board.connected:
                    try:
                        self.board.stop_test(wait_ack=True, timeout=1.0)
                    except Exception:
                        pass
                    self.board.all_outputs_off()
            finally:
                self.after(0, finish)

        def finish():
            self.returning_to_menu = False
            self.test_phase = "IDLE"
            self._clear_cycle_display()
            self._set_status("SẴN SÀNG", PURPLE)
            self.show_main_menu()

        threading.Thread(target=worker, daemon=True).start()

    # Tên cũ được giữ để tương thích mã gọi trước đây.
    def return_to_model_menu(self):
        self.return_to_main_menu()

    def continue_current_model_after_reject(self):
        """Xác nhận hàng lỗi theo đúng trace của phần mềm gốc.

        Không STOP, không tải lại model xuống bo. Ứng dụng đọc lại mapping local,
        gửi UNCONNECT, chờ REMOVAL + UNCONNECT rồi mới START chu kỳ kế tiếp.
        """
        self.reject_dialog_open = False
        self.testing = False
        self.test_phase = "WAIT_REMOVAL_FAIL"
        self.waiting_unconnect_reason = "FAIL"
        self.marking_phase = "IDLE"
        self.marking_token += 1
        self._set_status("HÃY THÁO SẢN PHẨM LỖI", "#d9a000", fg="#222222")

        code = self.current_model_code or (self.product.code if self.product else "")
        if not code:
            self.test_phase = "IDLE"
            self.waiting_unconnect_reason = None
            self._set_status("CHƯA CÓ MÃ HÀNG", RED)
            messagebox.showerror("Mã hàng", "Không xác định được mã hàng hiện tại.", parent=self)
            return

        try:
            # Chỉ reload dữ liệu local để cập nhật pin/connector/tên dây.
            # Không mở loader và không gửi lại MODEL/PINDATA xuống bo.
            files = _resolve_model_files_for_user(code, self.config)
            self.product = load_product_model(files.model_path)
            self.setup = load_setup(files.setup_path)
            self.current_model_code = self.product.code
            self.apply_product()
            self.refresh_summary()
        except Exception as exc:
            self.test_phase = "IDLE"
            self.waiting_unconnect_reason = None
            self._set_status("LỖI FILE MODEL", RED)
            messagebox.showerror(
                "Không thể đọc lại model",
                f"Không đọc lại được dữ liệu pin/connector của mã hiện tại.\n\n{exc}",
                parent=self,
            )
            return

        if not self.board or not self.board.connected:
            self.test_phase = "IDLE"
            self.waiting_unconnect_reason = None
            self._set_status("MẤT KẾT NỐI BO", RED)
            messagebox.showerror("UART", "Bo đã mất kết nối. Hãy về menu và kết nối lại.", parent=self)
            return

        try:
            # Trace thật: sau khi bấm xác nhận lỗi, PC gửi trực tiếp
            # :UNCONNECT,500,<pin_count>; không gửi :STOP.
            self.board.unconnect(self.config.unconnect_delay_ms, self._pin_count())
        except Exception as exc:
            self.test_phase = "IDLE"
            self.waiting_unconnect_reason = None
            self._set_status("LỖI CHỜ THÁO DÂY", RED)
            messagebox.showerror("Chờ tháo dây", str(exc), parent=self)

    def _start_next_cycle(self):
        if self.returning_to_menu or not self.test_screen_active:
            return
        if self.test_phase not in {"IDLE", "READY"}:
            return
        if not self.board or not self.board.connected:
            self.auto_start_pending = True
            self.connect_board_async()
            return
        self.start_test()

    def start_test(self):
        if self.returning_to_menu or not self.test_screen_active:
            return
        if not self.product:
            messagebox.showwarning("Mã hàng", "Hãy chọn và tải mã hàng trước", parent=self)
            return
        if not self.board or not self.board.connected:
            messagebox.showerror("UART", "Chưa kết nối bo", parent=self)
            return
        if self.test_phase not in {"IDLE", "READY"} or self.testing:
            # Không dùng START như nút toggle. Điều này ngăn STOP/START chồng nhau
            # khi người vận hành bấm nhiều lần hoặc chạm nhầm ô trạng thái.
            return

        self._clear_cycle_display()
        self.testing = True
        self.test_phase = "STARTING"
        self.waiting_unconnect_reason = None
        self._set_status("ĐANG KIỂM TRA", YELLOW, fg="#333333")
        try:
            self.board.start_test(self.config.maxext)
        except Exception as exc:
            self.testing = False
            self.test_phase = "IDLE"
            messagebox.showerror("START", str(exc), parent=self)

    def _set_status(self, text, bg, fg="white"):
        self.status_var.set(text)
        self.status_label.configure(bg=bg, fg=fg)

    def handle_event(self, event: BoardEvent):
        self.current_cycle_raw.append(event.raw)

        if event.family == "START":
            self.testing = True
            self.test_phase = "STARTING"
            self._set_status("ĐANG KIỂM TRA", YELLOW, fg="#333333")

        elif event.family == "MEASURE":
            self.testing = True
            self.test_phase = "MEASURING"
            self._set_status("ĐANG ĐO", YELLOW, fg="#333333")

        elif event.family == "CLEAR":
            # CLEAR mở một snapshot mới. Toàn bộ trạng thái OPEN/OTHER cũ phải xóa.
            self.cycle_state.open_networks.clear()
            self.cycle_state.wrong_wiring.clear()
            self.cycle_open_count = 0
            self.cycle_other_count = 0
            self.open_var.set(0)
            self.other_var.set(0)
            self.fault_table.clear()
            self.probe_tracker.clear()
            self._update_probe_status()
        elif event.family == "TESTPIN":
            self._handle_test_pin(event)

        elif event.family == "PIN":
            # Một số firmware phát :PIN,<pin>,0/1. Chỉ coi là trạng thái đầu
            # dò khi giá trị thứ hai đúng 0 hoặc 1; các dạng khác vẫn được lưu
            # nguyên trong log để tránh diễn giải nhầm một cặp chân.
            if len(event.values) >= 2 and int(event.values[1]) in {0, 1}:
                state = "ON" if int(event.values[1]) else "OFF"
                self._handle_test_pin(BoardEvent("TESTPIN", event.raw, (int(event.values[0]), state)))

        elif event.family == "OPEN":
            self.add_open(event)

        elif event.family == "OTHER":
            self.add_other(event)

        elif event.family == "CIRCUIT":
            value = int(event.values[0]) if event.values else -1
            self.finish_cycle(value)

        elif event.family == "PEN":
            if self.test_phase != "MARKING" or not self.board:
                return
            self.marking_phase = "WAIT_REMOVAL"
            self.test_phase = "WAIT_REMOVAL_PASS"
            self.waiting_unconnect_reason = "PASS"
            self._set_status("ĐÃ ĐÓNG DẤU - HÃY THÁO DÂY", "#00a02b")
            try:
                # Trace thật: nhận PEN xong gửi UNCONNECT gần như ngay lập tức.
                self.board.unconnect(self.config.unconnect_delay_ms, self._pin_count())
            except Exception as exc:
                self.marking_phase = "IDLE"
                self.test_phase = "IDLE"
                self.waiting_unconnect_reason = None
                messagebox.showerror("Chờ tháo dây", str(exc), parent=self)

        elif event.family == "REMOVAL":
            if self.waiting_unconnect_reason:
                self._set_status("HÃY THÁO DÂY", "#d9a000", fg="#222222")

        elif event.family == "UNCONNECT":
            # Đây là ranh giới sạch giữa hai sản phẩm theo trace thật.
            if self.waiting_unconnect_reason in {"PASS", "FAIL"}:
                self.waiting_unconnect_reason = None
                self.marking_phase = "IDLE"
                self.testing = False
                self.test_phase = "READY"
                self._clear_cycle_display()
                self._set_status("SẴN SÀNG", PURPLE)
                if self.product and not self.returning_to_menu and self.test_screen_active:
                    delay = max(20, int(self.config.next_cycle_delay_ms))
                    self.after(delay, self._start_next_cycle)

        elif event.family == "STOP":
            self.testing = False
            if not self.returning_to_menu:
                self.test_phase = "IDLE"

        elif event.family == "ERROR":
            self.testing = False
            self.test_phase = "IDLE"
            self._set_status("LỖI BO MẠCH", RED)
            messagebox.showerror("Lỗi bo mạch", event.raw, parent=self)

    def add_open(self, event: BoardEvent):
        # OPEN là snapshot sống. Chập đúng phải xóa ngay mạng khỏi bảng; tháo
        # ra phải tạo lại đầy đủ nguồn + chân đích theo topology file model.
        values = tuple(int(v) for v in event.values)
        if not values:
            return
        network_id = values[0]
        topology = self.product.network_topology() if self.product else {}
        before = self.cycle_state.open_networks.display_pins(
            network_id, topology.get(network_id, ())
        )
        self.cycle_state.open_networks.update(values)
        after = self.cycle_state.open_networks.display_pins(
            network_id, topology.get(network_id, ())
        )
        self.cycle_open_count = self.cycle_state.open_count_for(topology)
        self.open_var.set(self.cycle_open_count)

        # Khi chân vừa được chập đúng hoặc số chân hở giảm, ưu tiên refresh
        # ngay để dòng biến mất trước mắt người vận hành. Các update mở thêm
        # vẫn được gom theo table_refresh_ms để tránh giật với 100+ mạng.
        shrank = len(after) < len(before) or (before and not after)
        self._schedule_table_refresh(immediate=shrank)

    def add_other(self, event: BoardEvent):
        is_new = self.cycle_state.wrong_wiring.update(
            tuple(int(v) for v in event.values)
        )
        self.cycle_other_count = self.cycle_state.other_count
        self.other_var.set(self.cycle_other_count)
        # Đấu sai phải lập tức nhảy lên đầu để người vận hành nhìn thấy.
        self._schedule_table_refresh(immediate=is_new)

    def finish_cycle(self, circuit_value: int):
        self.testing = False
        self._schedule_table_refresh(immediate=True)

        # Log thật chứng minh hai chu kỳ PASS vẫn có hàng trăm OPEN lịch sử.
        # Kết luận cuối phải theo CIRCUIT, không được dùng lịch sử OPEN để ép FAIL.
        failed = circuit_value != 0
        result = "FAIL" if failed else "PASS"
        lot = self.lot_var.get().strip()
        model = self.current_model_code or (self.product.code if self.product else "UNKNOWN")
        self.store.add(
            lot,
            model,
            result,
            self.cycle_open_count,
            self.cycle_other_count,
            " | ".join(self.current_cycle_raw),
        )
        self.refresh_summary()

        if failed:
            self.test_phase = "RESULT_FAIL"
            self._set_status("KHÔNG ĐẠT", RED)
            if not self.reject_dialog_open:
                self.reject_dialog_open = True
                RejectDialog(
                    self,
                    self.cycle_open_count,
                    self.cycle_other_count,
                    self.continue_current_model_after_reject,
                )
            return

        # CIRCUIT,0 là kết quả đạt. Xóa snapshot OPEN còn sót nếu mất một
        # update ngắn, tránh để lỗi cũ nằm trên màn hình PASS.
        self.cycle_state.open_networks.active.clear()
        self.cycle_state.wrong_wiring.clear()
        self.cycle_open_count = 0
        self.cycle_other_count = 0
        self.open_var.set(0)
        self.other_var.set(0)
        self._schedule_table_refresh(immediate=True)
        self.test_phase = "RESULT_PASS"

        if self.config.auto_pass_pen and self.board:
            self.marking_phase = "PENDING"
            self.marking_token += 1
            token = self.marking_token
            self._set_status("ĐẠT - CHUẨN BỊ ĐÓNG DẤU", "#00a02b")
            # Trace thật: PASSPEN được gửi khoảng 300 ms sau CIRCUIT,0.
            delay = max(0, int(self.config.pass_action_delay_ms))
            self.after(delay, self._begin_marking, token)
        else:
            self.marking_phase = "WAIT_REMOVAL"
            self.test_phase = "WAIT_REMOVAL_PASS"
            self.waiting_unconnect_reason = "PASS"
            self._set_status("ĐẠT - HÃY THÁO DÂY", "#00a02b")
            if self.board:
                try:
                    self.board.unconnect(self.config.unconnect_delay_ms, self._pin_count())
                except Exception as exc:
                    self.test_phase = "IDLE"
                    self.waiting_unconnect_reason = None
                    messagebox.showerror("Chờ tháo dây", str(exc), parent=self)

    def _begin_marking(self, token: int):
        if (
            token != self.marking_token
            or self.returning_to_menu
            or self.test_phase != "RESULT_PASS"
            or not self.board
            or not self.board.connected
        ):
            return
        self.marking_phase = "MARKING"
        self.test_phase = "MARKING"
        self._set_status("ĐANG ĐÓNG DẤU", "#00a02b")
        try:
            self.board.pass_pen(self.config.pass_pen_delay_ms, self._pin_count())
            self.after(self.config.marking_timeout_ms, self._check_marking_timeout, token)
        except Exception as exc:
            self.marking_phase = "IDLE"
            self.test_phase = "IDLE"
            self._set_status("LỖI MARKING", RED)
            messagebox.showerror("Đóng dấu", str(exc), parent=self)

    def _check_marking_timeout(self, token: int):
        if token != self.marking_token or self.marking_phase != "MARKING":
            return
        self.marking_phase = "IDLE"
        self.test_phase = "IDLE"
        self.waiting_unconnect_reason = None
        self._set_status("LỖI MARKING", RED)
        messagebox.showerror(
            "Không nhận được phản hồi marking",
            "Đã gửi lệnh đóng relay/van nhưng bo không trả :PEN.\n"
            "Hãy kiểm tra relay, van khí, áp suất và dây điều khiển.",
            parent=self,
        )

    def _pin_count(self):
        if self.product:
            return self.product.pin_count
        return self.config.default_pin_count

    def open_report(self):
        ReportWindow(self)

    def open_settings(self):
        SettingsWindow(self)

    def open_diagnostics(self):
        if not self.board or not self.board.connected:
            messagebox.showerror("UART", "Chưa kết nối bo")
            return
        DiagnosticWindow(self)

    def poll_events(self):
        try:
            while True:
                kind, payload = self.events.get_nowait()
                if kind == "connected":
                    idn, model_response, port = payload
                    # Chỉ lưu lại IDN firmware; menu quyết định thời điểm hiển thị.
                    self.firmware_version = str(idn or "").strip()
                    self.reconnect_attempt = 0
                    self.board_info_var.set(f"ĐÃ KẾT NỐI {port}  |  {idn}  |  {model_response or ''}")
                    code = _model_code_from_response(model_response)
                    expected = self.expected_board_model_code
                    if code:
                        if expected and _normalized_model_code(code) != _normalized_model_code(expected):
                            self.model_verified = False
                            self.board_info_var.set(
                                f"MODEL KHÔNG KHỚP: bo={code}, cần={expected}"
                            )
                            self.after_idle(
                                lambda c=code, e=expected: messagebox.showerror(
                                    "Model trên bo không khớp",
                                    f"Bo đang báo model {c}, nhưng vừa tải {e}.\n"
                                    "Không cho phép bắt đầu kiểm tra. Hãy tải lại model.",
                                    parent=self.menu_window or self,
                                )
                            )
                        else:
                            self.current_model_code = code
                            self.load_current_model(code)
                            self.model_verified = self.product is not None
                            self.expected_board_model_code = ""
                    elif expected:
                        self.model_verified = False
                        self.board_info_var.set("KHÔNG ĐỌC ĐƯỢC MODELNAME TỪ BO")
                    self.refresh_menu_window()
                    if self.auto_start_pending and self.product and self.test_screen_active:
                        self.auto_start_pending = False
                        self.test_phase = "READY"
                        delay = max(20, int(self.config.next_cycle_delay_ms))
                        self.after(delay, self._start_next_cycle)
                elif kind == "connect_error":
                    self.board_info_var.set("CHƯA THẤY BO - ĐANG TỰ KẾT NỐI LẠI...")
                    self.events.put(("log", f"UART AUTO CONNECT ERROR: {payload}"))
                    self.refresh_menu_window()
                    self._schedule_reconnect()
                elif kind == "connection_lost":
                    self.board = None
                    self.testing = False
                    self.board_info_var.set("MẤT KẾT NỐI BO - ĐANG TỰ KẾT NỐI LẠI...")
                    self.events.put(("log", f"UART CONNECTION LOST: {payload}"))
                    self.refresh_menu_window()
                    self._schedule_reconnect()
                elif kind == "board_event":
                    self.handle_event(payload)
                elif kind == "log":
                    pass
        except queue.Empty:
            pass
        self.after(80, self.poll_events)

    def close_app(self, confirm: bool = True):
        if confirm and not messagebox.askyesno("Thoát", "Bạn có muốn thoát chương trình?", parent=self.menu_window or self):
            return
        self.app_closing = True
        if self.reconnect_after_id:
            try:
                self.after_cancel(self.reconnect_after_id)
            except tk.TclError:
                pass
            self.reconnect_after_id = None
        try:
            if self.board:
                try:
                    self.board.stop_test(wait_ack=True, timeout=0.8)
                except Exception:
                    pass
                self.board.all_outputs_off()
                self.board.disconnect()
        finally:
            self.store.close()
            self.destroy()


def run_gui():
    MainWindow().mainloop()
