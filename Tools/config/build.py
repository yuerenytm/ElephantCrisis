"""把 Design/*.csv 合成为游戏读取的 game_rules.json。

流程只有一段：

    Design/*.csv  --校验-->  game_rules.json  --JsonUtility-->  游戏

没有 yaml 中间产物、没有手写行解析器、没有"和旧快照比对"——
CSV 就是唯一权威，改了表跑一次即可。

用法::

    python Tools/config/build.py            # 校验 + 生成
    python Tools/config/build.py --check     # 只校验，不写文件
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path
from typing import Dict, List, Tuple

REPO = Path(__file__).resolve().parents[2]
DESIGN = REPO / "Design"
RULE_JSON = REPO / "Game" / "Assets" / "StreamingAssets" / "Config" / "game_rules.json"

# 列名 → yaml 风格键名（唯一映射，不许有歧义；表里就按这些列存参数）
TILE_PARAMS = {
    "移速修正": "move",
    "攻击修正": "atk",
    "防御修正": "def",
    "法抗修正%": "magic_resist",
    "射程修正": "range",
}
ROLE_PARAMS = {
    "移动": "move",
    "血量": "hp",
    "攻击": "atk",
    "防御": "def",
    "法抗%": "magic_resist",
    "背包": "bag",
}
USABLE_SLOTS = {"", "Weapon", "Armor", "Vehicle", "Accessory"}
USABLE_KINDS = {
    "None", "Instant", "ChooseStat", "TargetCell", "TargetUnit",
    "EquipToggle", "ChooseDelay", "ChooseDirection",
}

REQUIRED_COLUMNS = {
    "roles.csv": ["id", "名字", "移动", "血量", "攻击", "防御", "法抗%", "背包", "出生x", "出生y"],
    "cards.csv": ["id", "名字", "装备槽", "使用方式", "印数"],
    "card_stats.csv": ["卡牌", "参数", "值"],
    "tiles.csv": ["id", "名字"],
    "statuses.csv": ["id", "名字"],
    "rules.csv": ["分组", "键", "值"],
}

ROLE_LIMITS = {"move": (1, 12), "hp": (1, 500), "atk": (0, 100), "def": (0, 100),
               "magic_resist": (0, 100), "bag": (1, 999)}
ITEM_LIMITS = {"atk": (0, 100), "def": (0, 100), "move": (0, 10), "range": (0, 20),
               "damage": (0, 200), "radius": (0, 10), "charges": (0, 99),
               "rounds": (0, 20), "heal": (0, 200), "bag": (0, 20),
               "chance": (0.0, 1.0), "pct": (0, 100), "per_card": (0, 10), "cost": (0, 10)}
TILE_LIMITS = {"move": (-5, 5), "atk": (-10, 10), "def": (-10, 10),
               "magic_resist": (-100, 100), "range": (0, 5)}
STATUS_LIMITS = (-15, 15)
DECK_LIMIT = (0, 999)


# ---------------------------------------------------------------- 读表


def read_csv(name: str) -> List[dict]:
    path = DESIGN / name
    if not path.is_file():
        raise SystemExit(f"缺少 {path}，请先跑 build_tables.py")
    with path.open(encoding="utf-8-sig", newline="") as handle:
        return [{k: (v or "").strip() for k, v in row.items()} for row in csv.DictReader(handle)]


def read_header(name: str) -> List[str]:
    path = DESIGN / name
    if not path.is_file():
        return []
    with path.open(encoding="utf-8-sig", newline="") as handle:
        return next(csv.reader(handle), [])


def typed(text: str):
    """CSV 单元格 → 值：空串→None，整数→int，小数→float，其余→字符串。"""
    if text == "":
        return None
    for cast in (int, float):
        try:
            return cast(text)
        except ValueError:
            pass
    return text


def as_number(text: str):
    for cast in (int, float):
        try:
            return cast(text)
        except (TypeError, ValueError):
            pass
    return None


def split_params(text: str) -> Dict[str, object]:
    """``a=1; b=2`` → ``{"a":1,"b":2}``。"""
    out: Dict[str, object] = {}
    for chunk in text.split(";"):
        if "=" not in chunk:
            continue
        key, value = chunk.split("=", 1)
        out[key.strip()] = typed(value.strip())
    return out


# ---------------------------------------------------------------- 合成


def build_sections() -> Tuple[Dict[str, dict], Dict[str, str]]:
    """六张表 → 配置结构 + display 文案。"""
    sections: Dict[str, dict] = {"roles": {}, "deck": {}, "items": {}, "status": {},
                                 "terrain": {}, "grid": {"spawn_cells": {}}}
    display: Dict[str, str] = {}

    # rules.csv：全局旋钮（也含没能归属到条目、但带真实数值的键）
    for row in read_csv("rules.csv"):
        sections.setdefault(row["分组"], {})[row["键"]] = typed(row["值"])

    # roles.csv
    for row in read_csv("roles.csv"):
        role_id = row["id"]
        sections["roles"][role_id] = {key: typed(row.get(col, "")) for col, key in ROLE_PARAMS.items()}
        sections["grid"]["spawn_cells"][role_id] = [typed(row.get("出生x", "")), typed(row.get("出生y", ""))]
        display[f"role_{role_id}_name"] = row.get("名字", "")

    # cards.csv → deck + display
    for row in read_csv("cards.csv"):
        card_id = row["id"]
        sections["deck"][card_id] = typed(row.get("印数", "0"))
        display[f"card_{card_id}_name"] = row.get("名字", "")
        display[f"card_{card_id}_desc"] = row.get("一句话描述", "")

    # card_stats.csv → items
    for row in read_csv("card_stats.csv"):
        card_id, param = row["卡牌"], row["参数"]
        key = card_id if param == "value" else f"{card_id}_{param}"
        sections["items"][key] = typed(row["值"])

    # tiles.csv → terrain + display
    for row in read_csv("tiles.csv"):
        tile_id = row["id"]
        for col, param in TILE_PARAMS.items():
            if row.get(col, "") != "":
                sections["terrain"][f"{tile_id}_{param}"] = typed(row[col])
        for key, value in split_params(row.get("其它参数", "")).items():
            sections["terrain"][f"{tile_id}_{key}"] = value
        display[f"tile_{tile_id}_name"] = row.get("名字", "")
        display[f"tile_{tile_id}_effect"] = row.get("效果摘要", "")

    # statuses.csv → status + display（列名即键名，保证可逆）
    for row in read_csv("statuses.csv"):
        status_id = row["id"]
        for key, value in row.items():
            if key in {"id", "名字", "提示文案", "其它参数"} or value == "":
                continue
            sections["status"][f"{status_id}_{key}"] = typed(value)
        for key, value in split_params(row.get("其它参数", "")).items():
            sections["status"][f"{status_id}_{key}"] = value
        display[f"status_{status_id}_name"] = row.get("名字", "")
        display[f"status_{status_id}_tip"] = row.get("提示文案", "")

    return sections, display


# ---------------------------------------------------------------- 校验
#
# 目的只有一个：**让填错在构建期失败，而不是在游戏里静默生效。**


def check_range(problems: List[str], where: str, label: str, text: str, bounds) -> None:
    number = as_number(text)
    if number is None:
        problems.append(f"{where} · {label} 不是数字：{text!r}")
        return
    if not (bounds[0] <= number <= bounds[1]):
        problems.append(f"{where} · {label} 越界：{number}（允许 {bounds[0]}～{bounds[1]}）")


def validate() -> List[str]:
    problems: List[str] = []

    # ① 列结构
    for name, columns in REQUIRED_COLUMNS.items():
        header = read_header(name)
        if not header:
            problems.append(f"{name} 不存在或为空")
            continue
        missing = [c for c in columns if c not in header]
        if missing:
            problems.append(f"{name} 缺列：{missing}")

    # ② 角色：五维值域 + 出生点在地图内
    grid = {row["键"]: as_number(row["值"]) for row in read_csv("rules.csv") if row["分组"] == "grid"}
    width, height = grid.get("width") or 18, grid.get("height") or 18
    for row in read_csv("roles.csv"):
        where = f"roles.csv[{row['id']}]"
        if not row.get("名字"):
            problems.append(f"{where} · 缺名字")
        for column, param in ROLE_PARAMS.items():
            check_range(problems, where, column, row.get(column, ""), ROLE_LIMITS[param])
        for axis, limit in (("出生x", width), ("出生y", height)):
            value = as_number(row.get(axis, ""))
            if value is None or not (0 <= value < limit):
                problems.append(f"{where} · {axis} 出界：{row.get(axis)!r}（地图 0～{limit - 1}）")

    # ③ 卡牌：印数、枚举、唯一键
    card_ids = set()
    for row in read_csv("cards.csv"):
        where = f"cards.csv[{row['id']}]"
        if row["id"] in card_ids:
            problems.append(f"{where} · id 重复")
        card_ids.add(row["id"])
        if not row.get("名字"):
            problems.append(f"{where} · 缺名字")
        check_range(problems, where, "印数", row.get("印数", ""), DECK_LIMIT)
        if row.get("使用方式", "") not in USABLE_KINDS:
            problems.append(f"{where} · 使用方式非法：{row.get('使用方式')!r}")
        if row.get("装备槽", "") not in USABLE_SLOTS:
            problems.append(f"{where} · 装备槽非法：{row.get('装备槽')!r}")

    # ④ 卡牌参数：归属到卡 + 按后缀限值
    seen_stats = set()
    for row in read_csv("card_stats.csv"):
        where = f"card_stats.csv[{row['卡牌']}.{row['参数']}]"
        if row["卡牌"] not in card_ids:
            problems.append(f"{where} · 卡牌不存在")
            continue
        if where in seen_stats:
            problems.append(f"{where} · 重复的参数行")
        seen_stats.add(where)
        suffix = row["参数"].rsplit("_", 1)[-1]
        bounds = ITEM_LIMITS.get(suffix)
        if bounds:
            check_range(problems, where, "值", row["值"], bounds)
        elif as_number(row["值"]) is None:
            problems.append(f"{where} · 值不是数字：{row['值']!r}")

    # ⑤ 地形
    for row in read_csv("tiles.csv"):
        where = f"tiles.csv[{row['id']}]"
        for column, param in TILE_PARAMS.items():
            if row.get(column, "") != "":
                check_range(problems, where, column, row[column], TILE_LIMITS[param])

    # ⑥ 状态
    for row in read_csv("statuses.csv"):
        where = f"statuses.csv[{row['id']}]"
        for column, value in row.items():
            if column in {"id", "名字", "提示文案", "其它参数"} or value == "":
                continue
            bounds = (0, 10) if column == "max_stacks" else STATUS_LIMITS
            check_range(problems, where, column, value, bounds)

    return problems


# ---------------------------------------------------------------- 输出


def format_scalar(value) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, float):
        return repr(value)
    return str(value)


def flatten_sections(sections: dict) -> List[dict]:
    """把嵌套结构拍平成 entries（C# 用 JsonUtility 读，字段必须扁平）。"""
    entries: List[dict] = []

    def push(section: str, key: str, value) -> None:
        entries.append({"section": section, "key": key, "value": format_scalar(value)})

    for section, body in sections.items():
        for key, value in body.items():
            if isinstance(value, dict):
                for sub, sub_value in value.items():
                    if isinstance(sub_value, list):
                        push(section, f"{key}.{sub}", ",".join(str(v) for v in sub_value))
                    else:
                        push(section, f"{key}.{sub}", sub_value)
            else:
                push(section, key, value)
    return entries


def emit_json(sections: dict, display: dict) -> str:
    merged = dict(sections)
    merged["display"] = display
    payload = {"schema": 2, "source": "design_csv", "entries": flatten_sections(merged)}
    return json.dumps(payload, ensure_ascii=False, indent=2) + "\n"


def main(argv: List[str]) -> int:
    parser = argparse.ArgumentParser(description="Design/*.csv → game_rules.json")
    parser.add_argument("--check", action="store_true", help="只校验，不写文件")
    args = parser.parse_args(argv)

    sections, display = build_sections()
    problems = validate()

    total_keys = sum(len(body) for body in sections.values()) + len(display)
    print(f"合成 {total_keys} 个键（数值 {total_keys - len(display)} + 文案 {len(display)}）")

    if problems:
        print(f"\n—— 校验失败 {len(problems)} 条 ——")
        for line in problems:
            print("  ✗ " + line)
        print("\n未生成：先修表。")
        return 1

    if args.check:
        print("✓ 校验通过")
        return 0

    text = emit_json(sections, display)
    RULE_JSON.write_text(text, encoding="utf-8")
    print(f"✓ 已生成 {RULE_JSON.relative_to(REPO)}（{len(text.splitlines())} 行）")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
