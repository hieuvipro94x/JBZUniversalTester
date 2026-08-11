from __future__ import annotations

import json

from jbz_platform import config_dir, default_models_dir, default_setups_dir

CONFIG_PATH = config_dir("JBZModelSetupDownloader") / "app.json"


def _default() -> dict:
    return {
        "last_uart": "",
        "baudrate": 115200,
        "models_dir": str(default_models_dir()),
        "setups_dir": str(default_setups_dir()),
        "last_model_code": "",
    }


def load_config() -> dict:
    defaults = _default()
    try:
        data = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        if isinstance(data, dict):
            defaults["last_uart"] = str(data.get("last_uart") or data.get("port") or "")
            defaults["baudrate"] = int(data.get("baudrate", defaults["baudrate"]))
            defaults["last_model_code"] = str(data.get("last_model_code", ""))
            models = str(data.get("models_dir", "") or "")
            setups = str(data.get("setups_dir", "") or "")
            if models and not models.startswith("/home/sa/"):
                defaults["models_dir"] = models
            if setups and not setups.startswith("/home/sa/"):
                defaults["setups_dir"] = setups
    except (FileNotFoundError, json.JSONDecodeError, OSError, TypeError, ValueError):
        pass
    return defaults


def save_config(data: dict) -> None:
    CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
    safe_data = {
        "last_uart": str(data.get("last_uart") or data.get("port") or ""),
        "baudrate": int(data.get("baudrate", 115200)),
        "models_dir": str(data.get("models_dir") or default_models_dir()),
        "setups_dir": str(data.get("setups_dir") or default_setups_dir()),
        "last_model_code": str(data.get("last_model_code", "")),
        "config_version": 11,
    }
    temp = CONFIG_PATH.with_suffix(".tmp")
    temp.write_text(json.dumps(safe_data, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(CONFIG_PATH)
