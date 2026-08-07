"""Отчёт по фермам, помеченным экономической эвристикой.

Флаг farms.suspicious сервер только ПИШЕТ (пометка — сигнал человеку, не бан,
docs/ONLINE.md Ф4), и до этого скрипта его было некому читать: смотреть приходилось
руками в sqlite. Запуск:

    python Tools/suspicious_report.py

Печатает таблицу помеченных ферм: имя, сколько раз помечена, золото и уровень из
последнего снимка, когда снимок сохранён. Ничего не меняет — только SELECT.
"""

import json
import sqlite3
import sys
import time
from pathlib import Path

DB = Path(__file__).with_name("farm.db")


def main():
    if not DB.exists():
        print(f"базы нет: {DB}")
        return 1

    conn = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
    conn.row_factory = sqlite3.Row

    rows = conn.execute(
        "SELECT p.name, f.suspicious, f.state, f.saved_at, f.rev"
        " FROM farms f JOIN players p ON p.id = f.player_id"
        " WHERE f.suspicious > 0 ORDER BY f.suspicious DESC").fetchall()

    if not rows:
        print("помеченных ферм нет — эвристика молчит")
        return 0

    print(f"{'ферма':<24} {'пометок':>7} {'золото':>12} {'ступень':>7} {'снимок':>17}")
    for row in rows:
        gold, level = "?", "?"
        try:
            doc = json.loads(row["state"])
            gold = doc.get("Gold", "?")
            level = doc.get("FarmLevel", "?")
        except (ValueError, TypeError):
            pass
        saved = time.strftime("%Y-%m-%d %H:%M", time.gmtime(row["saved_at"]))
        print(f"{row['name']:<24} {row['suspicious']:>7} {gold:>12} {level:>7} {saved:>17}")

    print(f"\nвсего: {len(rows)}. Пометка — сигнал, не приговор: крупный подарок друга"
          f" даёт честный всплеск. Решение — за человеком.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
