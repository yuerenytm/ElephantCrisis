"""确保 Design/*.csv 里有代码枚举对应的每一行。

CSV 是**唯一权威**，本脚本只做一件事：**补缺失的行，从不改已有行。**

- id 清单来自 `Design/meta/meta.json`（ConfigExport 导出的"代码契约快照"：
  代码里有哪些卡 / 地形 / 状态 / 角色）
- 新行的名字取 meta 里的值，数值留空，由策划填
- `card_stats.csv` 与 `rules.csv` 完全由人维护，本脚本不碰

用法::

    python Tools/config/build_tables.py            # 补缺失行
    python Tools/config/build_tables.py --check     # 只报告缺哪些行
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path
from typing import Dict, List, Optional, Tuple

REPO = Path(__file__).resolve().parents[2]
DESIGN = REPO / "Design"
SCRIPTS = REPO / "Game" / "Assets" / "Scripts"
META_JSON = DESIGN / "meta" / "meta.json"

# 每张表的主键列与要补的列（其余列留空由人填）
TABLES: Dict[str, Tuple[List[str], List[str]]] = {
    "roles.csv": (["id"], ["id", "名字", "移动", "血量", "攻击", "防御", "法抗%", "背包", "出生x", "出生y"]),
    "cards.csv": (["id"], ["id", "名字", "一句话描述", "装备槽", "使用方式", "印数"]),
    "tiles.csv": (["id"], ["id", "名字", "效果摘要", "移速修正", "攻击修正", "防御修正",
                           "法抗修正%", "射程修正", "其它参数"]),
    "statuses.csv": (["id"], ["id", "名字", "提示文案", "per_stack_atk", "per_stack_def",
                              "per_stack_move", "atk", "def", "move", "max_stacks", "其它参数"]),
}


def read_existing(name: str) -> Tuple[List[str], List[dict]]:
    path = DESIGN / name
    if not path.is_file():
        return [], []
    with path.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        return list(reader.fieldnames or []), list(reader)


def write_rows(name: str, fields: List[str], rows: List[dict]) -> None:
    path = DESIGN / name
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for row in rows:
            writer.writerow({f: row.get(f, "") for f in fields})


def defaults_from_meta(name: str, meta: dict) -> Dict[str, dict]:
    """meta 里能直接用的初始值（名字 / 槽位 / 用法）。"""
    out: Dict[str, dict] = {}
    if name == "cards.csv":
        for card in meta.get("cards", []):
            out[card["id"]] = {
                "名字": card["name"],
                "一句话描述": card["desc"],
                "装备槽": card["slot"],
                "使用方式": card["use"],
                "印数": 0,
            }
    elif name == "tiles.csv":
        for tile in meta.get("tiles", []):
            out[tile["id"]] = {"名字": tile["name"], "效果摘要": tile["effect"]}
    elif name == "statuses.csv":
        for status in meta.get("statuses", []):
            out[status["id"]] = {"名字": status["name"], "提示文案": status["tip"]}
    elif name == "roles.csv":
        for role in meta.get("roles", []):
            out[role["id"]] = {"名字": role["name"]}
            # 角色数值没有合理默认值：缺行必须人工补
    return out


def main(argv: List[str]) -> int:
    parser = argparse.ArgumentParser(description="补齐 Design/*.csv 缺失的行")
    parser.add_argument("--check", action="store_true", help="只报告，不写文件")
    args = parser.parse_args(argv)

    if not META_JSON.is_file():
        print(f"缺少 {META_JSON}，请先跑 ConfigExport（见 Design/README.md）", file=sys.stderr)
        return 2

    newest_cs = max((p.stat().st_mtime for p in (SCRIPTS / "Core").glob("*.cs")), default=0.0)
    if META_JSON.stat().st_mtime < newest_cs:
        print("⚠ meta.json 比 C# 源码旧，可能过期；建议先重跑 ConfigExport", file=sys.stderr)

    meta = json.loads(META_JSON.read_text(encoding="utf-8"))
    missing_total = 0

    for name, (key_columns, all_columns) in TABLES.items():
        header, rows = read_existing(name)
        fields = header or all_columns
        known = {tuple(str(row.get(c, "")) for c in key_columns) for row in rows}
        defaults = defaults_from_meta(name, meta)

        added = []
        for item_id, values in defaults.items():
            key = tuple([item_id] + [""] * (len(key_columns) - 1))
            if key in known:
                continue
            row = {column: "" for column in fields}
            row[key_columns[0]] = item_id
            for column, value in values.items():
                if column in row:
                    row[column] = value
            added.append(row)

        missing_total += len(added)
        if not added:
            print(f"[{name}] {len(rows)} 行，无缺失")
            continue
        print(f"[{name}] {len(rows)} 行，缺 {len(added)} 行：{[r[key_columns[0]] for r in added]}")
        if not args.check:
            write_rows(name, fields, rows + added)

    if missing_total and args.check:
        print(f"\n共缺 {missing_total} 行；去掉 --check 即补入（数值列留空，需人工填）")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
