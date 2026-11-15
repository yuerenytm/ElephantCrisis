"""把 Design/*.csv 反向合成为配置文件，并证明它与原 game_rules.yaml 完全等价。

保守切换策略（本脚本的存在理由）：

1. **原 yaml 不删不改**，删掉生成物就能回到旧路径；
2. 生成物是**同格式 yaml**（game_rules.gen.yaml），游戏侧解析器一行不动，
   只改"读哪个文件"——所以"玩法与修改前一致"几乎由结构本身保证；
3. 切换前先证明"CSV 能无损还原 yaml"：逐键比对 + 整文档比对，两边都 0 差异才写文件。

为什么不是 JSON：现有 GameRulesConfig 是手写行解析器，roles / spawn_cells 另有专属
正则。换成 JSON 要重写读取路径，风险远大于收益。等本步稳定后，再单独做"解析器换
JSON"，那时才是低风险的。

用法::

    python Tools/config/build.py            # 校验 + 生成 + 比对
    python Tools/config/build.py --check     # 只校验与比对，不写文件
"""

from __future__ import annotations

import argparse
import csv
import sys
from pathlib import Path
from typing import Dict, List, Tuple

import yaml

REPO = Path(__file__).resolve().parents[2]
DESIGN = REPO / "Design"
RULE_YAML = REPO / "Game" / "Assets" / "StreamingAssets" / "Config" / "game_rules.yaml"
RULE_GEN = REPO / "Game" / "Assets" / "StreamingAssets" / "Config" / "game_rules.gen.yaml"

# 输出顺序（与原 yaml 一致，便于人工对照）
SECTION_ORDER = [
    "grid", "roles", "combat", "clock", "visibility", "weather",
    "lava", "match", "deck", "items", "status", "skills", "leader", "terrain",
]

# 列名 → yaml 键名（唯一映射，不许有歧义；表里就按这些列存参数）
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


# ---------------------------------------------------------------- 读取


def read_csv(name: str) -> List[dict]:
    path = DESIGN / name
    if not path.is_file():
        raise SystemExit(f"缺少 {path}，请先跑 build_tables.py")
    with path.open(encoding="utf-8-sig", newline="") as handle:
        return [{k: (v or "").strip() for k, v in row.items()} for row in csv.DictReader(handle)]


def typed(text: str):
    """CSV 单元格 → JSON 值：空串→None，整数→int，小数→float，其余→字符串。"""
    if text == "":
        return None
    for cast in (int, float):
        try:
            return cast(text)
        except ValueError:
            pass
    return text


def split_params(text: str) -> Dict[str, object]:
    """``a=1; b=2`` → ``{"a":1,"b":2}``。"""
    out: Dict[str, object] = {}
    for chunk in text.split(";"):
        if "=" not in chunk:
            continue
        key, value = chunk.split("=", 1)
        out[key.strip()] = typed(value.strip())
    return out


# ---------------------------------------------------------------- 反向合成


def build_sections() -> Tuple[Dict[str, dict], List[str]]:
    """从六张表还原出 yaml 的 section 结构；返回 (sections, 校验错误)。"""
    errors: List[str] = []
    sections: Dict[str, dict] = {"roles": {}, "deck": {}, "items": {}, "status": {}, "terrain": {},
                                 "grid": {"spawn_cells": {}}}

    # rules.csv：其余一切全局旋钮（也包含没能归属到条目、但带真实数值的键）
    for row in read_csv("rules.csv"):
        section, key, value = row["分组"], row["键"], typed(row["值"])
        sections.setdefault(section, {})[key] = value

    # roles.csv
    role_ids = set()
    for row in read_csv("roles.csv"):
        role_id = row["id"]
        if role_id in role_ids:
            errors.append(f"roles.csv: id 重复 {role_id}")
        role_ids.add(role_id)
        if not row.get("名字"):
            errors.append(f"roles.csv: {role_id} 缺名字")
        stats = {key: typed(row.get(col, "")) for col, key in ROLE_PARAMS.items()}
        if not isinstance(stats.get("hp"), int) or stats["hp"] <= 0:
            errors.append(f"roles.csv: {role_id} 血量必须为正整数（现为 {stats.get('hp')}）")
        sections["roles"][role_id] = stats
        sections["grid"]["spawn_cells"][role_id] = [typed(row.get("出生x", "")),
                                                    typed(row.get("出生y", ""))]

    # cards.csv → deck（印数）
    card_ids = set()
    for row in read_csv("cards.csv"):
        card_id = row["id"]
        if card_id in card_ids:
            errors.append(f"cards.csv: id 重复 {card_id}")
        card_ids.add(card_id)
        if not row.get("名字"):
            errors.append(f"cards.csv: {card_id} 缺名字")
        if row.get("装备槽", "") not in USABLE_SLOTS:
            errors.append(f"cards.csv: {card_id} 装备槽非法（{row.get('装备槽')}）")
        if row.get("使用方式", "") not in USABLE_KINDS:
            errors.append(f"cards.csv: {card_id} 使用方式非法（{row.get('使用方式')}）")
        count = typed(row.get("印数", "0"))
        if not isinstance(count, int) or count < 0:
            errors.append(f"cards.csv: {card_id} 印数必须为非负整数（现为 {count}）")
            count = 0
        sections["deck"][card_id] = count

    # card_stats.csv → items
    owned_items = set()
    for row in read_csv("card_stats.csv"):
        card_id, param = row["卡牌"], row["参数"]
        if card_id not in card_ids:
            errors.append(f"card_stats.csv: 引用了不存在的卡牌 {card_id}")
            continue
        key = card_id if param == "value" else f"{card_id}_{param}"
        sections["items"][key] = typed(row["值"])
        owned_items.add(key)

    # tiles.csv → terrain（空单元格跳过，保持与 yaml 同形）
    for row in read_csv("tiles.csv"):
        tile_id = row["id"]
        for col, param in TILE_PARAMS.items():
            if row.get(col, "") == "":
                continue
            sections["terrain"][f"{tile_id}_{param}"] = typed(row[col])
        sections["terrain"].update(
            {f"{tile_id}_{k}": v for k, v in split_params(row.get("其它参数", "")).items()}
        )

    # statuses.csv → status（列名即 yaml 键名，保证可逆）
    for row in read_csv("statuses.csv"):
        for key, value in row.items():
            if key in {"id", "名字", "提示文案", "其它参数"} or value == "":
                continue
            sections["status"][f"{row['id']}_{key}"] = typed(value)
        sections["status"].update(
            {f"{row['id']}_{k}": v for k, v in split_params(row.get("其它参数", "")).items()}
        )

    # 引用完整性：只有 card_stats.csv 里出现的参数，才要求卡牌存在
    # （rules.csv 里的遗留键是"还没归到具体卡"的，允许存在并原样保留）
    for key in owned_items:
        owner = next((c for c in sorted(card_ids, key=len, reverse=True)
                      if key == c or key.startswith(c + "_")), None)
        if owner is None:
            errors.append(f"card_stats.csv: 参数 {key} 找不到所属卡牌")

    return sections, errors


# ---------------------------------------------------------------- 与 yaml 比对


def flatten(data: dict, prefix: str = "") -> Dict[str, object]:
    out: Dict[str, object] = {}
    for key, value in data.items():
        path = f"{prefix}.{key}" if prefix else key
        if isinstance(value, dict):
            out.update(flatten(value, path))
        else:
            out[path] = value
    return out


def same(a, b) -> bool:
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
        return abs(float(a) - float(b)) < 1e-9
    return a == b


def diff_against_yaml(sections: dict) -> List[str]:
    yaml_data = yaml.safe_load(RULE_YAML.read_text(encoding="utf-8-sig")) or {}
    left, right = flatten(sections), flatten(yaml_data)
    problems = []
    for key in sorted(set(left) | set(right)):
        if key not in left:
            problems.append(f"yaml 有、表里没有：{key} = {right[key]!r}")
        elif key not in right:
            problems.append(f"表里有、yaml 没有：{key} = {left[key]!r}")
        elif not same(left[key], right[key]):
            problems.append(f"值不同：{key} · 表={left[key]!r} yaml={right[key]!r}")
    return problems


# ---------------------------------------------------------------- 生成 yaml


def format_scalar(value) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, float):
        return repr(value)
    return str(value)


def emit_yaml(sections: dict) -> str:
    """按原 yaml 的写法生成文档。

    必须匹配 GameRulesConfig 的行解析器：roles 用内联映射、spawn_cells 用
    ``[x, y]`` 列表，其余为 ``key: value`` 标量。
    """
    lines = [
        "# 本文件由 Tools/config/build.py 从 Design/*.csv 生成，请勿手改。",
        "# 权威数据：Design/roles.csv 等六张表；改了表要重跑 build.py。",
        "# 回退方式：删除本文件，游戏会自动读回 game_rules.yaml。",
        "",
    ]

    ordered = [name for name in SECTION_ORDER if name in sections]
    ordered += [name for name in sections if name not in SECTION_ORDER]

    for name in ordered:
        body = sections.get(name) or {}
        if not body:
            continue
        lines.append(f"{name}:")
        if name == "grid":
            for key, value in body.items():
                if key == "spawn_cells":
                    continue
                lines.append(f"  {key}: {format_scalar(value)}")
            spawn = body.get("spawn_cells") or {}
            if spawn:
                lines.append("  spawn_cells:")
                for role in ("elephant", "human", "monkey", "cat"):
                    if role in spawn:
                        x, y = spawn[role]
                        lines.append(f"    {role}: [{x}, {y}]")
        elif name == "roles":
            for role, stats in body.items():
                lines.append(
                    "  {role}: {{ move: {move}, hp: {hp}, atk: {atk}, def: {defence}, "
                    "bag: {bag}, magic_resist: {magic_resist} }}".format(
                        role=role,
                        move=stats.get("move"),
                        hp=stats.get("hp"),
                        atk=stats.get("atk"),
                        defence=stats.get("def"),
                        bag=stats.get("bag"),
                        magic_resist=stats.get("magic_resist"),
                    )
                )
        else:
            for key, value in body.items():
                lines.append(f"  {key}: {format_scalar(value)}")
        lines.append("")

    return "\n".join(lines)


def diff_documents(generated: str) -> List[str]:
    """把生成文档与原件都用 YAML 解析后整篇比对（比逐键比对更强）。"""
    try:
        left = yaml.safe_load(generated) or {}
    except yaml.YAMLError as exc:  # 生成器写坏了要立刻知道
        return [f"生成的 yaml 无法解析：{exc}"]
    right = yaml.safe_load(RULE_YAML.read_text(encoding="utf-8-sig")) or {}
    if left == right:
        return []
    keys = sorted(set(flatten(left)) | set(flatten(right)))
    left_flat, right_flat = flatten(left), flatten(right)
    return [
        f"{key}: 生成={left_flat.get(key)!r} 原={right_flat.get(key)!r}"
        for key in keys
        if not same(left_flat.get(key), right_flat.get(key))
    ]


# ---------------------------------------------------------------- 主流程


def main(argv: List[str]) -> int:
    parser = argparse.ArgumentParser(description="CSV → game_rules.gen.yaml，并与原 yaml 比对")
    parser.add_argument("--check", action="store_true", help="只校验比对，不写文件")
    args = parser.parse_args(argv)

    sections, errors = build_sections()
    diffs = diff_against_yaml(sections)
    generated = emit_yaml(sections)
    doc_diffs = diff_documents(generated)

    print(f"反向合成：{len(flatten(sections))} 个键")
    if errors:
        print(f"\n—— 校验失败 {len(errors)} 条 ——")
        for line in errors:
            print("  ✗ " + line)
    for label, items in (("与 yaml 逐键不一致", diffs), ("整文档比对不一致", doc_diffs)):
        if items:
            print(f"\n—— {label} {len(items)} 处 ——")
            for line in items[:30]:
                print("  · " + line)
            if len(items) > 30:
                print(f"  …… 另有 {len(items) - 30} 处")

    if errors or diffs or doc_diffs:
        print("\n未生成。CSV 目前还不能无损还原 yaml，先修表结构。")
        return 1

    print("\n✓ 逐键一致，且整文档解析结果完全相等（0 差异）")
    if args.check:
        return 0

    RULE_GEN.write_text(generated, encoding="utf-8")
    print(f"已生成 {RULE_GEN.relative_to(REPO)}（{len(generated.splitlines())} 行）")
    print("回退：删除该文件即可回到 game_rules.yaml")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
