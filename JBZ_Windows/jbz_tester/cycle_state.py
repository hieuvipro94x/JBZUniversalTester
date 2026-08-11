from __future__ import annotations

from dataclasses import dataclass, field
from typing import Mapping


@dataclass()
class OpenNetworkState:
    """Snapshot OPEN sống của từng mạng dây.

    Mỗi bản tin mới thay thế trạng thái cũ của cùng ``network_id``:

    - ``:OPEN,10,10,11``: mạng 10 đang hở, nguồn 10 và đích 11.
    - ``:OPEN,10,11``: nguồn 10 đã có tiếp xúc, đích 11 vẫn hở.
    - ``:OPEN,10``: mạng 10 đã đúng, xóa hoàn toàn khỏi bảng.

    ``active`` giữ nguyên payload sau network_id. Topology đầy đủ được lấy từ
    file model khi tính số đường hở và dựng các dòng giao diện.
    """

    active: dict[int, tuple[int, ...]] = field(default_factory=dict)
    capacity: dict[int, int] = field(default_factory=dict)

    def clear(self) -> None:
        self.active.clear()
        self.capacity.clear()

    def update(self, values: tuple[int, ...] | list[int]) -> int | None:
        numbers = tuple(int(v) for v in values)
        if not numbers:
            return None
        network_id = numbers[0]
        payload = tuple(dict.fromkeys(numbers[1:]))

        # Học số nhánh tối đa để vẫn đếm được nếu file model chưa đọc được.
        explicit_targets = tuple(pin for pin in payload if pin != network_id)
        if explicit_targets:
            self.capacity[network_id] = max(
                self.capacity.get(network_id, 0), len(explicit_targets)
            )

        if payload:
            self.active[network_id] = payload
        else:
            # Dòng chỉ có network_id là tín hiệu clear chính thức của firmware.
            self.active.pop(network_id, None)
        return network_id

    def unresolved_targets(
        self,
        network_id: int,
        expected_targets: tuple[int, ...] | list[int] = (),
    ) -> tuple[int, ...]:
        """Các chân đích hiện còn hở của mạng.

        Khi payload chỉ còn chân nguồn (ví dụ ``:OPEN,16,16``), firmware không
        liệt kê lại từng nhánh. Lúc này dùng topology từ model để phục hồi đầy
        đủ các chân đích, giúp bảng và bộ đếm không bị thiếu dòng.
        """
        source = int(network_id)
        payload = self.active.get(source)
        if payload is None:
            return ()

        expected = tuple(
            dict.fromkeys(int(pin) for pin in expected_targets if int(pin) != source)
        )
        explicit = tuple(pin for pin in payload if pin != source)
        if explicit:
            return explicit
        if source in payload and expected:
            return expected
        return ()

    def display_pins(
        self,
        network_id: int,
        expected_targets: tuple[int, ...] | list[int] = (),
    ) -> tuple[int, ...]:
        """Các dòng phải hiện: luôn có nguồn S rồi đến các chân hở."""
        source = int(network_id)
        if source not in self.active:
            return ()
        targets = self.unresolved_targets(source, expected_targets)
        # Dù payload UART bỏ source, phần mềm gốc vẫn hiện dòng nguồn S làm mốc.
        return (source, *targets)

    def unresolved_link_count(
        self,
        topology: Mapping[int, tuple[int, ...]] | None = None,
    ) -> int:
        total = 0
        topology = topology or {}
        for network_id, payload in self.active.items():
            expected = topology.get(network_id, ())
            targets = self.unresolved_targets(network_id, expected)
            if targets:
                total += len(targets)
            elif network_id in payload:
                # Không có topology: dùng số nhánh đã học từ payload trước đó.
                total += max(1, self.capacity.get(network_id, 1))
            else:
                total += len(payload)
        return total

    def active_pin_count(
        self,
        topology: Mapping[int, tuple[int, ...]] | None = None,
    ) -> int:
        topology = topology or {}
        return sum(
            len(self.display_pins(network_id, topology.get(network_id, ())))
            for network_id in self.active
        )


@dataclass()
class WrongWiringState:
    """Các cặp đấu sai hiện tại, tự gộp hai bản tin đối xứng A-B và B-A."""

    pairs: dict[tuple[int, int], tuple[int, int]] = field(default_factory=dict)

    def clear(self) -> None:
        self.pairs.clear()

    def update(self, values: tuple[int, ...] | list[int]) -> bool:
        numbers = tuple(int(v) for v in values)
        if len(numbers) < 2:
            return False
        source, target = numbers[0], numbers[1]
        key = tuple(sorted((source, target)))
        is_new = key not in self.pairs
        self.pairs.setdefault(key, (source, target))
        return is_new

    @property
    def pair_count(self) -> int:
        return len(self.pairs)

    @property
    def row_count(self) -> int:
        return self.pair_count * 2


@dataclass()
class TestCycleState:
    open_networks: OpenNetworkState = field(default_factory=OpenNetworkState)
    wrong_wiring: WrongWiringState = field(default_factory=WrongWiringState)
    raw_lines: list[str] = field(default_factory=list)

    def clear(self) -> None:
        self.open_networks.clear()
        self.wrong_wiring.clear()
        self.raw_lines.clear()

    @property
    def open_count(self) -> int:
        """Bộ đếm fallback, giữ tương thích API V5."""
        return self.open_networks.unresolved_link_count()

    def open_count_for(
        self,
        topology: Mapping[int, tuple[int, ...]] | None = None,
    ) -> int:
        """Bộ đếm chính xác khi có topology từ file model."""
        return self.open_networks.unresolved_link_count(topology)

    @property
    def other_count(self) -> int:
        return self.wrong_wiring.pair_count
