from __future__ import annotations

import csv
import sqlite3
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path


@dataclass()
class ResultSummary:
    total: int = 0
    passed: int = 0
    failed: int = 0

    @property
    def rate(self) -> float:
        return (self.passed / self.total * 100.0) if self.total else 0.0


class ResultStore:
    def __init__(self, path: Path) -> None:
        self.path = path
        path.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(path)
        self.conn.execute(
            """
            CREATE TABLE IF NOT EXISTS test_results (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tested_at TEXT NOT NULL,
                lot TEXT NOT NULL,
                model TEXT NOT NULL,
                result TEXT NOT NULL,
                open_count INTEGER NOT NULL,
                other_count INTEGER NOT NULL,
                raw_summary TEXT NOT NULL
            )
            """
        )
        self.conn.commit()

    def add(self, lot: str, model: str, result: str, open_count: int, other_count: int, raw_summary: str) -> None:
        self.conn.execute(
            "INSERT INTO test_results(tested_at, lot, model, result, open_count, other_count, raw_summary) VALUES(?,?,?,?,?,?,?)",
            (datetime.now().isoformat(timespec="seconds"), lot, model, result, open_count, other_count, raw_summary),
        )
        self.conn.commit()

    def summary(self, lot: str, model: str) -> ResultSummary:
        row = self.conn.execute(
            "SELECT COUNT(*), SUM(CASE WHEN result='PASS' THEN 1 ELSE 0 END), SUM(CASE WHEN result='FAIL' THEN 1 ELSE 0 END) FROM test_results WHERE lot=? AND model=?",
            (lot, model),
        ).fetchone()
        return ResultSummary(int(row[0] or 0), int(row[1] or 0), int(row[2] or 0))

    def recent(self, limit: int = 500) -> list[tuple]:
        return list(self.conn.execute(
            "SELECT tested_at, lot, model, result, open_count, other_count FROM test_results ORDER BY id DESC LIMIT ?",
            (limit,),
        ))

    def export_csv(self, path: Path) -> None:
        with path.open("w", encoding="utf-8-sig", newline="") as handle:
            writer = csv.writer(handle)
            writer.writerow(["tested_at", "lot", "model", "result", "open_count", "other_count"])
            writer.writerows(self.recent(1000000))

    def close(self) -> None:
        self.conn.close()
