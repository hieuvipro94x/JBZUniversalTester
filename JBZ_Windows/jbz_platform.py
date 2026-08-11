from __future__ import annotations

import os
import sys
from pathlib import Path


IS_WINDOWS = sys.platform.startswith("win")
APP_NAME = "JBZUniversalTesterProduction"
LOADER_NAME = "JBZModelSetupDownloader"


def _env_path(name: str) -> Path | None:
    value = os.environ.get(name, "").strip()
    return Path(value).expanduser() if value else None


def user_home() -> Path:
    if IS_WINDOWS:
        return _env_path("USERPROFILE") or Path.home()
    return Path.home()


def config_dir(app_name: str = APP_NAME) -> Path:
    if IS_WINDOWS:
        base = _env_path("APPDATA") or (user_home() / "AppData" / "Roaming")
        return base / app_name
    base = _env_path("XDG_CONFIG_HOME") or (user_home() / ".config")
    return base / app_name


def data_dir(app_name: str = APP_NAME) -> Path:
    if IS_WINDOWS:
        base = _env_path("LOCALAPPDATA") or (user_home() / "AppData" / "Local")
        return base / app_name
    base = _env_path("XDG_DATA_HOME") or (user_home() / ".local" / "share")
    return base / app_name


def default_models_dir() -> Path:
    override = _env_path("JBZ_MODELS_DIR")
    if override:
        return override
    # Tương đương /home/<user>/Models của bản Raspberry Pi.
    return user_home() / "Models"


def default_setups_dir() -> Path:
    override = _env_path("JBZ_SETUPS_DIR")
    if override:
        return override
    return user_home() / "Setups"


def loader_log_dir() -> Path:
    return data_dir(LOADER_NAME) / "logs"
