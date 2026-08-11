from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

from jbz_platform import default_models_dir as platform_models_dir
from jbz_platform import default_setups_dir as platform_setups_dir


class ModelLookupError(ValueError):
    pass


@dataclass(frozen=True)
class ModelFiles:
    code: str
    model_path: Path
    setup_path: Path


def normalize_model_code(value: str) -> str:
    code = value.strip().upper().replace(" ", "")
    if not code:
        raise ModelLookupError("Chưa nhập mã hàng")
    if not re.fullmatch(r"[A-Z0-9][A-Z0-9_-]{0,63}", code):
        raise ModelLookupError(
            "Mã hàng chỉ được chứa chữ, số, dấu gạch ngang hoặc gạch dưới"
        )
    return code


def default_models_dir() -> Path:
    return platform_models_dir()


def default_setups_dir() -> Path:
    return platform_setups_dir()


def ensure_default_directories(models_dir: Path, setups_dir: Path) -> None:
    models_dir.mkdir(parents=True, exist_ok=True)
    setups_dir.mkdir(parents=True, exist_ok=True)


def _code_variants(code: str) -> list[str]:
    result = [code]
    if code.startswith("WH") and len(code) > 2:
        result.append(code[2:])
    else:
        result.append("WH" + code)
    return list(dict.fromkeys(result))


def _compact(value: str) -> str:
    """Chuẩn hóa tên file để tìm được cả tên có khoảng trắng/gạch/phụ tố."""
    return re.sub(r"[^A-Z0-9]", "", value.upper())


def _stem_rank(stem: str, code: str, preferred_stem: str | None = None) -> int | None:
    value = stem.upper().strip()
    compact_value = _compact(value)

    if preferred_stem and value == preferred_stem.upper().strip():
        return -20

    variants = _code_variants(code)
    for variant in variants:
        if value == variant:
            return 0

    for variant in variants:
        if value.startswith(variant + "-") or value.startswith(variant + "_"):
            return 10

    for variant in variants:
        if re.search(rf"(?:^|[-_ ]){re.escape(variant)}(?:$|[-_ ])", value):
            return 20

    compact_variants = [_compact(v) for v in variants]
    for variant in compact_variants:
        if compact_value == variant:
            return 25
    for variant in compact_variants:
        if variant and variant in compact_value:
            return 30

    return None


def _all_files_with_suffix(directory: Path, suffix: str) -> list[Path]:
    if not directory.exists():
        return []
    return sorted(
        [
            path.resolve()
            for path in directory.rglob("*")
            if path.is_file() and path.suffix.lower() == suffix.lower()
        ],
        key=lambda p: str(p).lower(),
    )


def _matching_files(
    directory: Path,
    suffix: str,
    code: str,
    preferred_stem: str | None = None,
) -> list[tuple[int, Path]]:
    matches: list[tuple[int, Path]] = []
    for path in _all_files_with_suffix(directory, suffix):
        rank = _stem_rank(path.stem, code, preferred_stem)
        if rank is not None:
            matches.append((rank, path))
    matches.sort(key=lambda item: (item[0], len(str(item[1])), str(item[1]).lower()))
    return matches


def _directory_listing(directory: Path, suffix: str, limit: int = 30) -> str:
    files = _all_files_with_suffix(directory, suffix)
    if not files:
        return f"Thư mục {directory} hiện không có file {suffix}."
    shown = files[:limit]
    text = "\n".join(f"- {path.name}" for path in shown)
    if len(files) > limit:
        text += f"\n- ... còn {len(files) - limit} file khác"
    return f"Các file {suffix} đang có trong {directory}:\n{text}"


def _choose_unique(
    matches: list[tuple[int, Path]],
    label: str,
    code: str,
    directory: Path,
) -> Path:
    if not matches:
        raise ModelLookupError(
            f"Không tìm thấy file {label} cho mã hàng {code}.\n"
            f"Đường dẫn đã quét: {directory}\n\n"
            f"{_directory_listing(directory, label)}"
        )

    best_rank = matches[0][0]
    best = [path for rank, path in matches if rank == best_rank]
    if len(best) > 1:
        listing = "\n".join(f"- {path}" for path in best[:20])
        raise ModelLookupError(
            f"Tìm thấy nhiều file {label} cùng mức ưu tiên cho mã {code}. "
            f"Hãy giữ lại một file duy nhất:\n{listing}"
        )
    return best[0]


def resolve_model_files(
    code: str,
    models_dir: str | Path | None = None,
    setups_dir: str | Path | None = None,
) -> ModelFiles:
    normalized = normalize_model_code(code)
    model_root = Path(models_dir or default_models_dir()).expanduser().resolve()
    setup_root = Path(setups_dir or default_setups_dir()).expanduser().resolve()
    ensure_default_directories(model_root, setup_root)

    model_matches = _matching_files(model_root, ".model", normalized)
    model_path = _choose_unique(model_matches, ".model", normalized, model_root)

    setup_matches = _matching_files(
        setup_root,
        ".setup",
        normalized,
        preferred_stem=model_path.stem,
    )
    setup_path = _choose_unique(setup_matches, ".setup", normalized, setup_root)
    return ModelFiles(normalized, model_path, setup_path)
