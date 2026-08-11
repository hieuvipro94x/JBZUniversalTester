from __future__ import annotations

import time


class PinProbeTracker:
    """Trạng thái sống của đầu dò GND.

    Firmware tự phát ``:TESTPIN,<pin>,ON/OFF`` trong chu kỳ START. Tracker hỗ
    trợ nhiều chân đang ON đồng thời và giữ thứ tự chạm mới nhất để GUI đưa
    đúng chân lên đầu.
    """

    def __init__(self) -> None:
        self._active: dict[int, float] = {}

    def clear(self) -> None:
        self._active.clear()

    def update(self, pin: int, state: str, timestamp: float | None = None) -> bool:
        physical = int(pin)
        normalized = str(state).strip().upper()
        is_on = normalized in {"ON", "1", "TRUE"}
        if is_on:
            if physical in self._active:
                return False
            self._active[physical] = time.monotonic() if timestamp is None else float(timestamp)
            return True
        return self._active.pop(physical, None) is not None

    @property
    def active(self) -> bool:
        return bool(self._active)

    @property
    def count(self) -> int:
        return len(self._active)

    def ordered_pins(self) -> tuple[int, ...]:
        return tuple(
            pin for pin, _ in sorted(
                self._active.items(), key=lambda item: item[1], reverse=True
            )
        )

    def newest(self) -> int | None:
        ordered = self.ordered_pins()
        return ordered[0] if ordered else None
