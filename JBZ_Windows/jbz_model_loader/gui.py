from __future__ import annotations

import queue
import threading
import tkinter as tk
from datetime import datetime
from pathlib import Path
from tkinter import messagebox, ttk

from .config_store import load_config, save_config
from .model_locator import (
    ModelFiles,
    default_models_dir,
    default_setups_dir,
    ensure_default_directories,
    resolve_model_files,
)
from .profile_io import load_model_profile, load_setup_profile, merge_setup_before_finish
from .serial_session import LineLogger, SerialSession
from jbz_platform import loader_log_dir
from jbz_tester.ui_theme import (
    BG, BORDER, BRAND, BUTTON, FIELD, FONT, HEADER, MUTED, PANEL, TEXT, TEXT_DARK,
    apply_ttk_theme, flat_button, scaled_font, value_label,
)


class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("JBZ Model Downloader - CHỌN MÃ HÀNG")
        self.configure(bg=BG)
        sw, sh = self.winfo_screenwidth(), self.winfo_screenheight()
        self.ui_scale = max(0.70, min(1.35, min(sw / 1650, sh / 930)))
        self.geometry(f"{sw}x{sh}+0+0")
        self.minsize(1024, 600)
        self.config_data = load_config()
        self.events: queue.Queue[tuple[str, object]] = queue.Queue()
        self.cancel_event = threading.Event()
        self.worker: threading.Thread | None = None
        self.profile = None
        self.selected_files: ModelFiles | None = None

        self.last_uart = str(self.config_data.get("last_uart", ""))
        self.baud_var = tk.IntVar(value=int(self.config_data["baudrate"]))
        self.models_dir_var = tk.StringVar(value=str(default_models_dir()))
        self.setups_dir_var = tk.StringVar(value=str(default_setups_dir()))
        self.code_var = tk.StringVar(value=str(self.config_data.get("last_model_code", "")))
        self.model_found_var = tk.StringVar(value="Chưa tìm")
        self.setup_found_var = tk.StringVar(value="Chưa tìm")
        self.status_var = tk.StringVar(value="Đang tự kết nối bo thật...")
        self.progress_var = tk.DoubleVar(value=0)
        self.step_var = tk.StringVar(value="Nhập mã hàng rồi bấm TÌM VÀ NẠP")
        self.board_ok = False

        ensure_default_directories(
            Path(self.models_dir_var.get()).expanduser(),
            Path(self.setups_dir_var.get()).expanduser(),
        )
        self._build_ui()
        self.after(80, self._poll_events)
        self.after(250, self._probe)
        self.protocol("WM_DELETE_WINDOW", self._on_close)

    def _build_ui(self):
        apply_ttk_theme(self, self.ui_scale)

        header = tk.Frame(self, bg=BG)
        header.pack(fill="x", padx=max(16, int(24 * self.ui_scale)), pady=max(12, int(18 * self.ui_scale)))
        tk.Label(
            header,
            text="JBZ MODEL DOWNLOADER",
            bg=BG,
            fg=TEXT,
            font=scaled_font(self.ui_scale, 24),
        ).pack(side="left")
        tk.Label(
            header,
            text="PHẦN CỨNG THẬT",
            bg=BG,
            fg="#b00020",
            font=scaled_font(self.ui_scale, 12, "bold"),
        ).pack(side="right")

        body = tk.Frame(self, bg=BG)
        body.pack(fill="both", expand=True, padx=max(16, int(24 * self.ui_scale)), pady=(0, max(12, int(18 * self.ui_scale))))
        body.columnconfigure(0, weight=3)
        body.columnconfigure(1, weight=2)
        body.rowconfigure(0, weight=1)

        left = tk.Frame(body, bg=BG)
        left.grid(row=0, column=0, sticky="nsew", padx=(0, max(10, int(16 * self.ui_scale))))
        right = tk.Frame(body, bg=BG)
        right.grid(row=0, column=1, sticky="nsew")

        conn = tk.Frame(left, bg=BG)
        conn.pack(fill="x", pady=(0, max(12, int(18 * self.ui_scale))))
        tk.Label(conn, text="KẾT NỐI TỰ ĐỘNG", bg=BG, fg=TEXT, font=scaled_font(self.ui_scale, 15, "bold")).pack(anchor="w")
        tk.Label(
            conn,
            textvariable=self.status_var,
            bg=HEADER,
            fg=TEXT,
            relief="solid",
            bd=1,
            anchor="w",
            padx=10,
            font=scaled_font(self.ui_scale, 12),
        ).pack(fill="x", pady=(6, 0), ipady=max(6, int(8 * self.ui_scale)))
        tk.Label(
            conn,
            textvariable=self.baud_var,
            bg=BG,
            fg=MUTED,
            font=scaled_font(self.ui_scale, 10),
        ).pack(anchor="w", pady=(4, 0))

        select = tk.Frame(left, bg=BG)
        select.pack(fill="x", pady=max(8, int(12 * self.ui_scale)))
        tk.Label(select, text="CHỌN MÃ HÀNG", bg=BG, fg=TEXT, font=scaled_font(self.ui_scale, 23)).pack(pady=(0, max(12, int(18 * self.ui_scale))))
        input_row = tk.Frame(select, bg=BG)
        input_row.pack(fill="x")
        tk.Label(input_row, text="MÃ HÀNG", bg=BG, fg=TEXT, font=scaled_font(self.ui_scale, 13)).pack(side="left", padx=(0, 10))
        code_entry = tk.Entry(
            input_row,
            textvariable=self.code_var,
            font=scaled_font(self.ui_scale, 18),
            bg=FIELD,
            fg=TEXT_DARK,
            relief="solid",
            bd=1,
            justify="center",
        )
        code_entry.pack(side="left", fill="x", expand=True, ipady=max(7, int(9 * self.ui_scale)))
        code_entry.bind("<Return>", lambda _event: self._lookup_and_upload())

        action_row = tk.Frame(select, bg=BG)
        action_row.pack(fill="x", pady=max(10, int(14 * self.ui_scale)))
        self.action_button = flat_button(
            action_row,
            "TÌM FILE VÀ NẠP XUỐNG BO",
            self._lookup_and_upload,
            self.ui_scale,
            font=scaled_font(self.ui_scale, 13, "bold"),
        )
        self.action_button.pack(side="left", fill="x", expand=True, ipady=max(6, int(8 * self.ui_scale)))
        flat_button(action_row, "DỪNG", self._cancel, self.ui_scale, width=10).pack(side="left", padx=(8, 0), ipady=max(6, int(8 * self.ui_scale)))

        files = tk.Frame(left, bg=BG)
        files.pack(fill="x", pady=max(6, int(10 * self.ui_scale)))
        for row, (label, variable) in enumerate((
            ("File model", self.model_found_var),
            ("File setup", self.setup_found_var),
            ("Thư mục model", self.models_dir_var),
            ("Thư mục setup", self.setups_dir_var),
        )):
            tk.Label(files, text=label, bg=BG, fg=TEXT, font=scaled_font(self.ui_scale, 11), anchor="e").grid(row=row, column=0, sticky="e", padx=(0, 8), pady=4)
            value_label(files, variable=variable, scale=self.ui_scale, font=scaled_font(self.ui_scale, 10)).grid(row=row, column=1, sticky="ew", pady=4, ipady=4)
        files.columnconfigure(1, weight=1)

        ttk.Progressbar(
            left,
            variable=self.progress_var,
            maximum=100,
            style="TesterVN.Horizontal.TProgressbar",
        ).pack(fill="x", pady=(max(10, int(14 * self.ui_scale)), 0), ipady=2)
        tk.Label(left, textvariable=self.step_var, bg=BG, fg=TEXT, font=scaled_font(self.ui_scale, 11), anchor="w", wraplength=700).pack(fill="x", pady=(5, 0))

        tk.Label(right, text="NHẬT KÝ", bg=BG, fg=TEXT, font=scaled_font(self.ui_scale, 15, "bold")).pack(anchor="w", pady=(0, 6))
        self.log_text = tk.Text(
            right,
            wrap="none",
            font=("Consolas", max(8, int(9 * self.ui_scale))),
            bg=PANEL,
            fg=TEXT,
            relief="solid",
            bd=1,
            insertbackground=TEXT_DARK,
        )
        self.log_text.pack(fill="both", expand=True)
        code_entry.focus_set()

    def _append_log(self, text: str):
        self.log_text.insert("end", text + "\n")
        self.log_text.see("end")

    def _prepare_code(self) -> bool:
        try:
            files = resolve_model_files(
                self.code_var.get(),
                default_models_dir(),
                default_setups_dir(),
            )
            profile = merge_setup_before_finish(
                load_model_profile(files.model_path),
                load_setup_profile(files.setup_path),
            )
            self.selected_files = files
            self.profile = profile
            self.model_found_var.set(str(files.model_path))
            self.setup_found_var.set(str(files.setup_path))
            self._append_log(f"CODE  {files.code}")
            self._append_log(f"MODEL {files.model_path}")
            self._append_log(f"SETUP {files.setup_path}")
            self._append_log(f"BOARD MODEL NAME {profile.model_name}")
            self._append_log(f"CMD   {len(profile.commands)}")
            meta = profile.metadata
            if "pin_rows" in meta:
                self._append_log(
                    f"PIN   rows={meta['pin_rows']}, records={meta['source_records']}, "
                    f"targets={meta['target_items']}, connectors={meta['connector_count']}"
                )
            for warning in profile.warnings:
                self._append_log("WARN  " + warning)
            self.step_var.set(
                f"Đã tìm đủ model/setup cho {files.code}: {len(profile.commands)} command"
            )
            return True
        except Exception as exc:
            self.profile = None
            self.selected_files = None
            self.model_found_var.set("Không tìm thấy/không hợp lệ")
            self.setup_found_var.set("Không tìm thấy/không hợp lệ")
            self._append_log("ERROR " + str(exc))
            messagebox.showerror("Không thể chọn mã hàng", str(exc))
            return False

    def _lookup_and_upload(self):
        if self.worker and self.worker.is_alive():
            return
        if not self._prepare_code():
            return
        if not self.board_ok:
            messagebox.showerror(
                "Chưa kết nối bo",
                "Chưa nhận diện được bo thật. Phần mềm đang tự quét và sẽ tự kết nối lại.",
            )
            return
        assert self.profile is not None
        assert self.selected_files is not None
        if not messagebox.askyesno(
            "Xác nhận nạp mã hàng",
            f"Mã nhập: {self.selected_files.code}\n"
            f"Model tìm thấy: {self.selected_files.model_path.name}\n"
            f"Setup tìm thấy: {self.selected_files.setup_path.name}\n"
            f"Tên ghi xuống bo: {self.profile.model_name}\n"
            f"Tổng command: {len(self.profile.commands)}\n\n"
            "Nạp xuống bo test ngay?",
        ):
            return
        self._start_upload()

    def _remember_uart(self, port: str):
        self.last_uart = port
        self.config_data["last_uart"] = port
        save_config(self.config_data)

    def _probe(self):
        if self.worker and self.worker.is_alive():
            return
        self.cancel_event.clear()
        self.worker = threading.Thread(target=self._probe_worker, daemon=True)
        self.worker.start()

    def _probe_worker(self):
        log_path = loader_log_dir() / f"probe_{datetime.now():%Y%m%d_%H%M%S}.log"
        logger = LineLogger(log_path, lambda s: self.events.put(("log", s)))
        try:
            with SerialSession(
                None, self.baud_var.get(), logger, self.cancel_event,
                preferred_port=self.last_uart,
                on_port_found=self._remember_uart,
            ) as session:
                idn, model = session.handshake()
                self.events.put(("board_ok", f"ĐÃ KẾT NỐI {session.port}: {idn} | {model or 'chưa có model'}"))
        except Exception as exc:
            self.events.put(("board_fail", str(exc)))
        finally:
            logger.close()

    def _start_upload(self):
        if not self.profile:
            return
        self.cancel_event.clear()
        self.action_button.configure(state="disabled")
        self.progress_var.set(0)
        self.worker = threading.Thread(target=self._upload_worker, daemon=True)
        self.worker.start()

    def _upload_worker(self):
        def progress(done, total, command):
            self.events.put(("progress", (done, total, command)))

        log_path = loader_log_dir() / f"model_upload_{datetime.now():%Y%m%d_%H%M%S}.log"
        logger = LineLogger(log_path, lambda s: self.events.put(("log", s)))
        try:
            with SerialSession(
                None, self.baud_var.get(), logger, self.cancel_event,
                preferred_port=self.last_uart,
                on_port_found=self._remember_uart,
            ) as session:
                result = session.upload_profile(self.profile, progress=progress)
            self.events.put(("done", result))
        except Exception as exc:
            self.events.put(("error", str(exc)))
        finally:
            logger.close()
            self.events.put(("unlock", None))

    def _cancel(self):
        self.cancel_event.set()
        self._append_log("WARN  Đã yêu cầu dừng")

    def _poll_events(self):
        try:
            while True:
                kind, payload = self.events.get_nowait()
                if kind == "log":
                    self._append_log(str(payload))
                elif kind == "board_ok":
                    self.board_ok = True
                    self.status_var.set(str(payload))
                elif kind == "board_fail":
                    self.board_ok = False
                    self.status_var.set("CHƯA THẤY BO - ĐANG TỰ KẾT NỐI LẠI...")
                    self._append_log("ERROR " + str(payload))
                    if not (self.worker and self.worker.is_alive()):
                        self.after(800, self._probe)
                elif kind == "progress":
                    done, total, command = payload
                    self.progress_var.set(done * 100 / max(total, 1))
                    self.step_var.set(f"{done}/{total}: {command}")
                elif kind == "error":
                    self._append_log("ERROR " + str(payload))
                    messagebox.showerror("Nạp model thất bại", str(payload))
                elif kind == "done":
                    self.progress_var.set(100)
                    self.step_var.set(f"NẠP THÀNH CÔNG: {payload.model_name}")
                    messagebox.showinfo(
                        "Hoàn thành",
                        f"Đã nạp model thật: {payload.model_name}\n"
                        f"FINISH: {payload.finish_response}\n"
                        f"VERIFY: {payload.verified_model_response}\n"
                        f"Thời gian: {payload.elapsed_seconds:.2f}s",
                    )
                elif kind == "unlock":
                    self.action_button.configure(state="normal")
        except queue.Empty:
            pass
        self.after(80, self._poll_events)

    def _on_close(self):
        self.cancel_event.set()
        save_config({
            "last_uart": self.last_uart,
            "baudrate": self.baud_var.get(),
            "models_dir": str(default_models_dir()),
            "setups_dir": str(default_setups_dir()),
            "last_model_code": self.code_var.get().strip(),
        })
        self.destroy()


def run_gui():
    App().mainloop()
