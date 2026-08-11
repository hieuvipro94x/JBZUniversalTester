from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from .model_locator import default_models_dir, default_setups_dir, resolve_model_files
from jbz_platform import loader_log_dir
from .profile_io import load_model_profile, load_setup_profile, merge_setup_before_finish
from .protocol import default_expectation


def trace_to_profile(trace_path: Path, output_path: Path) -> None:
    commands = []
    for raw in trace_path.read_text(encoding="utf-8", errors="replace").splitlines():
        match = re.search(r"\bTX\s+(:[A-Z][^\r\n]*)$", raw.strip())
        if not match:
            continue
        tx = match.group(1).strip()
        family = tx.lstrip(":").split(",", 1)[0]
        if family not in {
            "MODEL", "PINCOUNT", "PINDATA", "ARRAYCOUNT", "ARRAY", "CONCOUNT", "CON",
            "CONNECTORCOUNT", "CONNECTOR", "FINISH",
        }:
            continue
        exp = default_expectation(tx)
        commands.append({
            "tx": tx,
            "expect": {"mode": exp.mode, "value": exp.value, "timeout": exp.timeout},
        })
    if not commands:
        raise ValueError("Trace không có command tải model")
    model = next((c["tx"].split(",", 1)[1] for c in commands if c["tx"].startswith(":MODEL,")), "UNKNOWN")
    data = {
        "format": "jbz-model-protocol-v1",
        "model_name": model,
        "source": str(trace_path),
        "commands": commands,
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def _directory_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--models-dir", default=str(default_models_dir()))
    parser.add_argument("--setups-dir", default=str(default_setups_dir()))


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="JBZ Model Downloader - HARDWARE ONLY")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("gui")

    locate = sub.add_parser("locate", help="Tìm model/setup theo mã hàng")
    locate.add_argument("code")
    _directory_args(locate)

    inspect_code = sub.add_parser("inspect-code", help="Tìm và biên dịch theo mã hàng")
    inspect_code.add_argument("code")
    _directory_args(inspect_code)

    inspect = sub.add_parser("inspect")
    inspect.add_argument("model")
    inspect.add_argument("--setup")

    probe = sub.add_parser("probe")
    probe.add_argument("--port", default="auto")
    probe.add_argument("--baud", type=int, default=115200)

    upload_code = sub.add_parser("upload-code", help="Tìm file theo mã và nạp bo thật")
    upload_code.add_argument("code")
    _directory_args(upload_code)
    upload_code.add_argument("--port", default="auto")
    upload_code.add_argument("--baud", type=int, default=115200)
    upload_code.add_argument("--no-reset", action="store_true")
    upload_code.add_argument("--no-verify", action="store_true")

    upload = sub.add_parser("upload")
    upload.add_argument("model")
    upload.add_argument("--setup")
    upload.add_argument("--port", default="auto")
    upload.add_argument("--baud", type=int, default=115200)
    upload.add_argument("--no-reset", action="store_true")
    upload.add_argument("--no-verify", action="store_true")

    convert = sub.add_parser("trace-profile")
    convert.add_argument("trace")
    convert.add_argument("output")
    return parser


def _load_by_code(args):
    files = resolve_model_files(args.code, args.models_dir, args.setups_dir)
    profile = merge_setup_before_finish(
        load_model_profile(files.model_path), load_setup_profile(files.setup_path)
    )
    return files, profile


def _upload_profile(args, profile) -> None:
    import threading
    from datetime import datetime
    from .serial_session import LineLogger, SerialSession
    from .config_store import load_config, save_config

    cancel = threading.Event()
    config = load_config()
    def remember(port: str):
        config["last_uart"] = port
        save_config(config)
    log_path = loader_log_dir() / f"model_upload_{datetime.now():%Y%m%d_%H%M%S}.log"
    logger = LineLogger(log_path, print)
    try:
        with SerialSession(
            None if args.port.lower() == "auto" else args.port,
            args.baud, logger, cancel,
            preferred_port=config.get("last_uart", ""),
            on_port_found=remember,
        ) as session:
            result = session.upload_profile(
                profile,
                reset_after=not args.no_reset,
                verify_after=not args.no_verify,
            )
    finally:
        logger.close()
    print(result)


def main(argv=None) -> int:
    args = build_parser().parse_args(argv)
    if args.command == "gui":
        from .gui import run_gui
        run_gui()
        return 0
    if args.command == "trace-profile":
        trace_to_profile(Path(args.trace), Path(args.output))
        print(f"Đã tạo {args.output}")
        return 0
    if args.command == "locate":
        files = resolve_model_files(args.code, args.models_dir, args.setups_dir)
        print(json.dumps({
            "code": files.code,
            "model": str(files.model_path),
            "setup": str(files.setup_path),
        }, ensure_ascii=False, indent=2))
        return 0
    if args.command == "inspect-code":
        files, profile = _load_by_code(args)
        print(json.dumps({
            "code": files.code,
            "model_file": str(files.model_path),
            "setup_file": str(files.setup_path),
            "board_model_name": profile.model_name,
            "commands": len(profile.commands),
            "metadata": profile.metadata,
            "warnings": profile.warnings,
        }, ensure_ascii=False, indent=2))
        return 0
    if args.command == "inspect":
        profile = merge_setup_before_finish(
            load_model_profile(args.model), load_setup_profile(args.setup)
        )
        print(json.dumps({
            "mode": "HARDWARE_ONLY",
            "model_name": profile.model_name,
            "commands": len(profile.commands),
            "metadata": profile.metadata,
            "warnings": profile.warnings,
            "first": [c.tx for c in profile.commands[:3]],
            "last": [c.tx for c in profile.commands[-3:]],
        }, ensure_ascii=False, indent=2))
        return 0
    if args.command == "probe":
        from .serial_session import LineLogger, SerialSession
        from .config_store import load_config, save_config
        logger = LineLogger(callback=print)
        config = load_config()
        def remember(port: str):
            config["last_uart"] = port
            save_config(config)
        with SerialSession(
            None if args.port.lower() == "auto" else args.port,
            args.baud, logger,
            preferred_port=config.get("last_uart", ""),
            on_port_found=remember,
        ) as session:
            print(session.handshake())
        return 0
    if args.command == "upload-code":
        files, profile = _load_by_code(args)
        print(f"MODEL: {files.model_path}")
        print(f"SETUP: {files.setup_path}")
        _upload_profile(args, profile)
        return 0
    if args.command == "upload":
        profile = merge_setup_before_finish(
            load_model_profile(args.model), load_setup_profile(args.setup)
        )
        _upload_profile(args, profile)
        return 0
    return 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("Đã hủy", file=sys.stderr)
        raise SystemExit(130)
