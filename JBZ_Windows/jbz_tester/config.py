from __future__ import annotations

import json
from dataclasses import asdict, dataclass

from jbz_platform import config_dir, data_dir, default_models_dir, default_setups_dir
from jbz_uart.manager import IS_WINDOWS


APP_DIR = config_dir("JBZUniversalTesterProduction")
CONFIG_PATH = APP_DIR / "app.json"
DATA_DIR = data_dir("JBZUniversalTesterProduction")
LOG_DIR = DATA_DIR / "logs"
DATABASE_PATH = DATA_DIR / "results.sqlite3"
MODELS_DIR = default_models_dir()
SETUPS_DIR = default_setups_dir()


@dataclass()
class AppConfig:
    # last_uart chỉ là cache để lần sau mở nhanh; nếu cổng thay đổi phần mềm tự quét lại.
    last_uart: str = ""
    baudrate: int = 115200
    models_dir: str = str(MODELS_DIR)
    setups_dir: str = str(SETUPS_DIR)
    fullscreen: bool = True
    auto_pass_pen: bool = True
    maxext: int = 0
    pass_pen_delay_ms: int = 500
    unconnect_delay_ms: int = 500
    marking_timeout_ms: int = 5000
    pass_action_delay_ms: int = 300
    next_cycle_delay_ms: int = 20
    board_boot_stabilize_ms: int = 2500
    table_refresh_ms: int = 60
    reconnect_delay_ms: int = 250
    uart_probe_timeout_ms: int = 1800 if IS_WINDOWS else 280
    config_version: int = 12
    default_pin_count: int = 200
    # Firmware tự phát TESTPIN trong lúc START; tùy chọn này chỉ ẩn/hiện trên GUI.
    show_pin_probe: bool = True
    last_lot: str = ""


def load_config() -> AppConfig:
    cfg = AppConfig()
    try:
        raw = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        if isinstance(raw, dict):
            previous_version = int(raw.get("config_version", 1))
            if not raw.get("last_uart") and raw.get("port"):
                raw["last_uart"] = str(raw.get("port", ""))
            for key in asdict(cfg):
                if key in raw:
                    setattr(cfg, key, raw[key])
            if previous_version < 2:
                cfg.auto_pass_pen = True
            if previous_version < 5:
                cfg.pass_action_delay_ms = 300
                cfg.next_cycle_delay_ms = 20
                cfg.board_boot_stabilize_ms = 2500
                cfg.table_refresh_ms = 60
            if previous_version < 8:
                cfg.show_pin_probe = bool(raw.get("auto_pin_probe", True))
            if previous_version < 10:
                cfg.reconnect_delay_ms = 250
                cfg.uart_probe_timeout_ms = 280
            if previous_version < 12 and IS_WINDOWS:
                # Bản Windows V10 dùng 280 ms, quá ngắn với một số USB-UART.
                cfg.uart_probe_timeout_ms = max(1800, int(cfg.uart_probe_timeout_ms))
            cfg.config_version = 12
    except (FileNotFoundError, OSError, ValueError, TypeError, json.JSONDecodeError):
        pass

    # Nếu config cũ trỏ đến /home/sa thì chuyển sang thư mục user hiện tại.
    models = str(getattr(cfg, "models_dir", "") or "")
    setups = str(getattr(cfg, "setups_dir", "") or "")
    if not models or models.startswith("/home/sa/"):
        cfg.models_dir = str(MODELS_DIR)
    if not setups or setups.startswith("/home/sa/"):
        cfg.setups_dir = str(SETUPS_DIR)

    cfg.last_uart = str(cfg.last_uart or "").strip()
    cfg.baudrate = int(cfg.baudrate)
    cfg.maxext = int(cfg.maxext)
    cfg.reconnect_delay_ms = max(100, int(cfg.reconnect_delay_ms))
    cfg.uart_probe_timeout_ms = max(1800 if IS_WINDOWS else 80, int(cfg.uart_probe_timeout_ms))
    return cfg


def save_config(cfg: AppConfig) -> None:
    APP_DIR.mkdir(parents=True, exist_ok=True)
    tmp = CONFIG_PATH.with_suffix(".tmp")
    tmp.write_text(json.dumps(asdict(cfg), ensure_ascii=False, indent=2), encoding="utf-8")
    tmp.replace(CONFIG_PATH)
