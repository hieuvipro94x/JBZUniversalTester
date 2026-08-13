from __future__ import annotations

import configparser
import re
from dataclasses import dataclass, field
from pathlib import Path


# Ký hiệu màu đang được phần mềm/model JBZ sử dụng.
_COLOR_CODES = {
    "B", "BK", "BLACK",
    "W", "WHITE",
    "R", "RED",
    "G", "GREEN",
    "Y", "YELLOW",
    "L", "BL", "BLUE",
    "O", "ORANGE",
    "P", "PINK",
    "BR", "BROWN",
    "GR", "GRAY", "GREY",
    "V", "VIOLET",
    "SB", "SKY",
    "LG", "LIGHTGREEN",
}


@dataclass()
class PinRecord:
    row_index: int
    physical: int
    connector: str
    local_pin: str
    line_name: str
    splice_wire: str
    gauge: str
    color_text: str
    color_tokens: tuple[str, ...]
    parent: int | None
    targets: tuple[int, ...]
    raw_fields: tuple[str, ...] = field(default_factory=tuple)

    @property
    def location(self) -> str:
        return f"{self.connector}-{self.local_pin}"


@dataclass()
class ProductModel:
    path: Path
    code: str
    title: str
    product_no: str
    product_name: str
    vehicle: str
    customer_no: str
    connector_count: int
    pin_count: int
    pins: dict[int, PinRecord]

    def pin(self, physical: int | None) -> PinRecord | None:
        if physical is None:
            return None
        return self.pins.get(int(physical))

    def network_targets(self, source_physical: int) -> tuple[int, ...]:
        """Trả về toàn bộ chân đích của một mạng theo file ``.model``."""
        source = int(source_physical)
        result: list[int] = []
        source_pin = self.pin(source)
        if source_pin:
            result.extend(int(value) for value in source_pin.targets if int(value) != source)
        for physical, record in self.pins.items():
            if record.parent == source and physical != source:
                result.append(int(physical))
        return tuple(dict.fromkeys(result))

    def network_topology(self) -> dict[int, tuple[int, ...]]:
        result: dict[int, tuple[int, ...]] = {}
        for physical in self.pins:
            targets = self.network_targets(physical)
            if targets:
                result[int(physical)] = targets
        return result


@dataclass()
class SetupData:
    path: Path
    sections: dict[str, dict[str, str]]


def read_legacy_text(path: Path) -> str:
    raw = path.read_bytes()
    for encoding in ("utf-8-sig", "utf-8", "cp949", "euc-kr", "cp1258", "latin-1"):
        try:
            return raw.decode(encoding)
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1", errors="replace")


def _load_ini(path: Path) -> configparser.ConfigParser:
    parser = configparser.ConfigParser(interpolation=None, strict=False)
    parser.optionxform = str
    parser.read_string(read_legacy_text(path))
    return parser


def _int_or_none(value: str) -> int | None:
    try:
        number = int(value.strip())
        return None if number < 0 else number
    except (TypeError, ValueError):
        return None


def _targets(value: str) -> tuple[int, ...]:
    result: list[int] = []
    for token in re.split(r"[/,;\s]+", value.strip()):
        number = _int_or_none(token)
        if number is not None:
            result.append(number)
    return tuple(result)


_GAUGE_RE = re.compile(r"^\s*\d+(?:[\.,]\d+)?(?:\s*(?:MM2|MM²|SQ))?\s*$", re.IGNORECASE)


def _read_gauge(fields: tuple[str, ...]) -> str:
    """Đọc tiết diện dây từ cột số 5 của bản ghi Pin.

    Cấu trúc V9 xác nhận:
      - cột 4: dây dập nối / splice wire;
      - cột 5: tiết diện dây;
      - cột 6: màu dây.

    Chấp nhận các giá trị như ``0.3``, ``0,5``, ``1.25``, ``0.5 mm²``.
    Nếu trường trống hoặc không phải tiết diện thì trả chuỗi rỗng để GUI để trống.
    """
    if len(fields) <= 5:
        return ""
    value = fields[5].strip()
    if not value or not _GAUGE_RE.fullmatch(value):
        return ""
    # Giao diện dùng dấu chấm thống nhất, nhưng giữ hậu tố mm²/SQ nếu có.
    return value.replace(",", ".")


def _split_color_token(value: str) -> tuple[str, ...]:
    """Tách mã màu đơn hoặc màu sọc như ``L/W``, ``B-Y``.

    Dấu ``/`` trong file model là phân cách màu, không phải phân cách target ở
    trường này. Kết quả giữ tối đa bốn màu để khớp bốn ô màu của giao diện gốc.
    """
    result: list[str] = []
    for token in re.split(r"[/,+;\-\s]+", value.strip()):
        normalized = token.strip().upper()
        if normalized in _COLOR_CODES:
            result.append(normalized)
    return tuple(dict.fromkeys(result))[:4]


def _read_color(fields: tuple[str, ...]) -> tuple[str, tuple[str, ...]]:
    """Đọc màu dây theo các biến thể file model JBZ.

    Trong model thực tế, màu chính thường nằm ở cột số 6, ví dụ::

        P1=1|1|1|M1C8|||R||-1|60

    Một số phiên bản cũ có thể đặt màu ở cột 7. V9 không đọc cột 5
    làm màu vì cột 5 được dành riêng cho tiết diện dây.
    """
    candidates = [fields[index].strip() for index in (6, 7) if len(fields) > index]
    for candidate in candidates:
        tokens = _split_color_token(candidate)
        if tokens:
            return candidate, tokens
    return "", ()


def _split_splice_names(value: str) -> tuple[str, ...]:
    """Tách danh sách dây dập nối, giữ nguyên tên dây và loại trùng."""
    result: list[str] = []
    for token in re.split(r"[/,;]+", value.strip()):
        name = token.strip()
        if name:
            result.append(name)
    return tuple(dict.fromkeys(result))


def _apply_bidirectional_splice_links(pins: dict[int, PinRecord]) -> None:
    """Bổ sung quan hệ dây dập nối theo hai chiều.

    Nếu file chỉ khai báo ``MC21 -> MC01`` thì mọi dòng có tên dây ``MC01``
    cũng hiển thị ``MC21`` ở cột Dây dập nối. Không ghi ngược vào file model;
    đây chỉ là dữ liệu dẫn xuất dùng cho giao diện.
    """
    links: dict[str, list[str]] = {}
    for record in pins.values():
        source = record.line_name.strip()
        if not source:
            continue
        for target in _split_splice_names(record.splice_wire):
            if target == source:
                continue
            links.setdefault(source, []).append(target)
            links.setdefault(target, []).append(source)

    for record in pins.values():
        source = record.line_name.strip()
        if not source:
            continue
        ordered = list(_split_splice_names(record.splice_wire))
        ordered.extend(links.get(source, ()))
        ordered = [name for name in dict.fromkeys(ordered) if name and name != source]
        record.splice_wire = "/".join(ordered)


def load_product_model(path: str | Path) -> ProductModel:
    model_path = Path(path).expanduser().resolve()
    parser = _load_ini(model_path)
    common = parser["Common"] if parser.has_section("Common") else {}
    connector = parser["Connector"] if parser.has_section("Connector") else {}
    pin_section = parser["Pin"] if parser.has_section("Pin") else {}

    code = model_path.stem
    common_model = str(common.get("Model", "")).strip()
    title = common_model or code
    name = str(common.get("Name", "")).strip()
    product_no = str(common.get("No", "")).strip() or str(common.get("Customer", "")).strip()
    customer_no = str(common.get("Customer", "")).strip() or product_no
    vehicle = str(common.get("Kind", "")).strip()

    try:
        connector_count = int(str(connector.get("Count", "0")).strip() or 0)
    except ValueError:
        connector_count = 0
    try:
        pin_count = int(str(pin_section.get("Count", "0")).strip() or 0)
    except ValueError:
        pin_count = 0

    pins: dict[int, PinRecord] = {}
    for key, value in pin_section.items():
        match = re.fullmatch(r"P(\d+)", key, re.IGNORECASE)
        if not match:
            continue
        fields = tuple(value.split("|"))
        if len(fields) < 4:
            continue
        try:
            physical = int(fields[0].strip())
        except ValueError:
            continue
        connector_name = fields[1].strip() if len(fields) > 1 else ""
        local_pin = fields[2].strip() if len(fields) > 2 else ""
        line_name = fields[3].strip() if len(fields) > 3 else ""
        # V9: field[4] là dây dập nối, không phải tiết diện.
        splice_wire = fields[4].strip() if len(fields) > 4 else ""
        gauge = _read_gauge(fields)
        color_text, color_tokens = _read_color(fields)
        parent = _int_or_none(fields[8]) if len(fields) > 8 else None
        targets = _targets(fields[9]) if len(fields) > 9 else ()
        pins[physical] = PinRecord(
            row_index=int(match.group(1)),
            physical=physical,
            connector=connector_name,
            local_pin=local_pin,
            line_name=line_name,
            splice_wire=splice_wire,
            gauge=gauge,
            color_text=color_text,
            color_tokens=color_tokens,
            parent=parent,
            targets=targets,
            raw_fields=fields,
        )

    _apply_bidirectional_splice_links(pins)

    return ProductModel(
        path=model_path,
        code=code,
        title=title,
        product_no=product_no,
        product_name=name,
        vehicle=vehicle,
        customer_no=customer_no,
        connector_count=connector_count,
        pin_count=pin_count or len(pins),
        pins=pins,
    )


def load_setup(path: str | Path) -> SetupData:
    setup_path = Path(path).expanduser().resolve()
    parser = _load_ini(setup_path)
    sections = {section: dict(parser.items(section)) for section in parser.sections()}
    return SetupData(path=setup_path, sections=sections)


def resolve_model_file(code: str, root: Path | None = None) -> Path | None:
    from jbz_platform import default_models_dir

    normalized = re.sub(r"[^A-Z0-9]", "", code.upper())
    candidates: list[tuple[int, Path]] = []
    root = Path(root or default_models_dir()).expanduser()
    if not root.exists():
        return None
    for path in root.rglob("*"):
        if not path.is_file() or path.suffix.lower() != ".model":
            continue
        compact = re.sub(r"[^A-Z0-9]", "", path.stem.upper())
        if compact == normalized:
            candidates.append((0, path))
        elif compact == "WH" + normalized or (normalized.startswith("WH") and compact == normalized[2:]):
            candidates.append((1, path))
        elif normalized and normalized in compact:
            candidates.append((2, path))
    candidates.sort(key=lambda item: (item[0], len(item[1].name), item[1].name.lower()))
    return candidates[0][1] if candidates else None
