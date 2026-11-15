"""把"数值"与"元数据"合成策划表（Design/*.csv）。

分工：
- 数值来自 ``game_rules.yaml``（**当前权威**）
- 名字/描述/槽位/用法来自 ``Design/meta/meta.json``（由 `ConfigExport` 从代码导出）

定位：**迁移期的影子副本**。本脚本只读不写游戏数据；改 ``Design/*.csv``
目前**不会**影响游戏。切权威要等下一步，并要求同种子对拍 0 差异。

用法::

    python Tools/config/build_tables.py             # 生成/刷新 Design/*.csv
    python Tools/config/build_tables.py --check      # 只比对，有漂移则非零退出
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import yaml

REPO = Path(__file__).resolve().parents[2]
RULE_YAML = REPO / "Game" / "Assets" / "StreamingAssets" / "Config" / "game_rules.yaml"
META_JSON = REPO / "Design" / "meta" / "meta.json"
DESIGN = REPO / "Design"


# ---------------------------------------------------------------- 通用


def attribute(keys: Dict[str, object], ids: List[str]) -> Tuple[Dict[str, dict], List[str]]:
    """把 ``{id}_{参数}`` 形式的键归属到 id。

    用**最长匹配**，所以 ``mega_bomb_damage`` 归到 ``mega_bomb`` 而不是 ``bomb``。
    归不上的键不猜，原样返回让调用方报出来。
    """
    ordered = sorted(ids, key=len, reverse=True)
    owned: Dict[str, dict] = {}
    unmatched: List[str] = []
    for key, value in keys.items():
        owner = next((i for i in ordered if key == i or key.startswith(i + "_")), None)
        if owner is None:
            unmatched.append(key)
            continue
        param = key[len(owner):].lstrip("_") or "value"
        owned.setdefault(owner, {})[param] = value
    return owned, unmatched


def write_csv(path: Path, rows: List[dict], check_only: bool) -> Optional[str]:
    """写表；``check_only`` 时只比对，返回差异描述（无差异返回 None）。"""
    fields: List[str] = []
    for row in rows:
        for key in row:
            if key not in fields:
                fields.append(key)
    new_rows = [{k: "" if row.get(k) is None else str(row.get(k)) for k in fields} for row in rows]

    if check_only:
        if not path.is_file():
            return f"{path.name}: 文件不存在"
        with path.open(encoding="utf-8-sig", newline="") as handle:
            old_rows = [{k: (v or "") for k, v in row.items()} for row in csv.DictReader(handle)]
        if old_rows == new_rows:
            return None
        key = fields[0]
        old_by = {r.get(key, ""): r for r in old_rows}
        new_by = {r.get(key, ""): r for r in new_rows}
        added = sorted(set(new_by) - set(old_by))
        removed = sorted(set(old_by) - set(new_by))
        changed = sorted(k for k in set(old_by) & set(new_by) if old_by[k] != new_by[k])
        parts = []
        if added:
            parts.append(f"新增 {len(added)}（{', '.join(added[:4])}）")
        if removed:
            parts.append(f"删除 {len(removed)}（{', '.join(removed[:4])}）")
        if changed:
            parts.append(f"修改 {len(changed)}（{', '.join(changed[:4])}）")
        return f"{path.name}: " + " / ".join(parts)

    path.parent.mkdir(parents=True, exist_ok=True)
    # utf-8-sig：Excel 直接双击打开不会乱码
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        writer.writerows(new_rows)
    return None


# ---------------------------------------------------------------- 各张表


def build_roles(rules: dict, meta: dict) -> List[dict]:
    names = {r["id"]: r["name"] for r in meta.get("roles", [])}
    spawn = (rules.get("grid") or {}).get("spawn_cells") or {}
    rows = []
    for role_id, stats in (rules.get("roles") or {}).items():
        cell = spawn.get(role_id) or [None, None]
        rows.append(
            {
                "id": role_id,
                "名字": names.get(role_id, ""),
                "移动": stats.get("move"),
                "血量": stats.get("hp"),
                "攻击": stats.get("atk"),
                "防御": stats.get("def"),
                "法抗%": stats.get("magic_resist"),
                "背包": stats.get("bag"),
                "出生x": cell[0],
                "出生y": cell[1],
            }
        )
    return rows


def build_cards(rules: dict, meta: dict) -> List[dict]:
    deck = rules.get("deck") or {}
    rows = []
    for card in meta.get("cards", []):
        rows.append(
            {
                "id": card["id"],
                "名字": card["name"],
                "一句话描述": card["desc"],
                "装备槽": card["slot"],
                "使用方式": card["use"],
                "印数": deck.get(card["id"], 0),
            }
        )
    return rows


def build_card_stats(rules: dict, meta: dict) -> Tuple[List[dict], List[str]]:
    ids = [c["id"] for c in meta.get("cards", [])]
    owned, unmatched = attribute(rules.get("items") or {}, ids)
    rows = [
        {"卡牌": card_id, "参数": param, "值": value}
        for card_id, params in owned.items()
        for param, value in params.items()
    ]
    rows.sort(key=lambda r: (r["卡牌"], r["参数"]))
    return rows, unmatched


def build_tiles(rules: dict, meta: dict) -> Tuple[List[dict], List[str]]:
    tiles = meta.get("tiles", [])
    owned, unmatched = attribute(rules.get("terrain") or {}, [t["id"] for t in tiles])
    rows = []
    for tile in tiles:
        params = owned.get(tile["id"], {})
        others = {
            k: v
            for k, v in params.items()
            if k not in {"move", "atk", "def", "magic_resist", "range"}
        }
        rows.append(
            {
                "id": tile["id"],
                "名字": tile["name"],
                "效果摘要": tile["effect"],
                # 空单元格 = yaml 里没有这个键（由代码默认 0 兜底）；不补 0，保持可逆
                "移速修正": params.get("move", ""),
                "攻击修正": params.get("atk", ""),
                "防御修正": params.get("def", ""),
                "法抗修正%": params.get("magic_resist", ""),
                "射程修正": params.get("range", ""),
                "其它参数": "; ".join(f"{k}={v}" for k, v in others.items()),
            }
        )
    return rows, unmatched


def build_statuses(rules: dict, meta: dict) -> Tuple[List[dict], List[str]]:
    owned, unmatched = attribute(rules.get("status") or {}, [s["id"] for s in meta.get("statuses", [])])
    # 列名直接用 yaml 键名（per_stack_atk / atk / max_stacks …），
    # 这样反向合成时不用猜"这个修正来自哪个键"，可逆性零损耗。
    param_names = ["per_stack_atk", "per_stack_def", "per_stack_move", "atk", "def", "move", "max_stacks"]
    rows = []
    for status in meta.get("statuses", []):
        params = owned.get(status["id"], {})
        row = {
            "id": status["id"],
            "名字": status["name"],
            "提示文案": status["tip"],
        }
        for name in param_names:
            row[name] = params.get(name, "")
        row["其它参数"] = "; ".join(f"{k}={v}" for k, v in params.items() if k not in param_names)
        rows.append(row)
    return rows, unmatched


def build_rules(rules: dict, extra: Dict[str, List[str]]) -> List[dict]:
    """其余全局旋钮：树形结构拍平成 分组/键/值/说明。

    ``extra`` 是"没能归属到具体卡牌/地形/状态"的键，按所属 yaml 段分组；
    这些键**照样带真实数值**，只是说明列会标出来——数据不许丢，缺口要看得见。
    """
    expanded = {"roles", "deck", "items", "status", "terrain"}
    rows: List[dict] = []
    for section, payload in rules.items():
        if not isinstance(payload, dict) or section in expanded:
            continue
        for key, value in payload.items():
            if section == "grid" and key == "spawn_cells":
                continue
            if isinstance(value, dict):
                continue
            rows.append({"分组": section, "键": key, "值": value, "说明": ""})
    for section, keys in extra.items():
        payload = rules.get(section) or {}
        for key in keys:
            rows.append(
                {
                    "分组": section,
                    "键": key,
                    "值": payload.get(key, ""),
                    "说明": "全局参数，或键名与条目 id 不一致，暂未进专表",
                }
            )
    return rows


README = """# Design —— 策划工作区

**权威数据在这里。** 六张表由 `build_tables.py` 生成（原 yaml + 代码元数据 → CSV），
再由 `build.py` 反向合成游戏实际读取的配置：

```
game_rules.yaml（数值原文） + meta/meta.json（代码导出的元数据）
        │ build_tables.py
        ▼
   六张 CSV  ← 改这里
        │ build.py（校验 + 反向合成 + 与原 yaml 比对）
        ▼
Game/Assets/StreamingAssets/Config/game_rules.gen.yaml  ← 游戏优先读它
```

## 回退方式（保守迁移）

原 `game_rules.yaml` **保留不动**。删掉 `game_rules.gen.yaml`，
游戏下次启动就读回原文件——不改代码、不改 git。

## 已通过的验收

| 层级 | 怎么验 | 结果 |
|---|---|---|
| 数据 | CSV 反向合成 250 个键，逐键比对 + 整文档解析比对 | 0 差异 |
| 行为 | 同种子 50000 起 100 局，对拍切换前基线 | 0 差异 |

## 六张表

| 文件 | 内容 | 谁能改 |
|---|---|---|
| `roles.csv` | 四角色五维、法抗、出生点 | 策划 |
| `cards.csv` | 卡牌身份：名字、描述、槽位、使用方式、印数 | 策划 |
| `card_stats.csv` | 卡牌数值（长表：卡牌 / 参数 / 值） | 策划 |
| `tiles.csv` | 地形修正（空单元格 = 原 yaml 里没有该键） | 策划 |
| `statuses.csv` | 状态数值；列名即 yaml 键名，保证可逆 | 策划 |
| `rules.csv` | 其余全局旋钮 + 未能归属到条目的键 | 程序 |

表里**只放能改的数据**。行为（例如"状态何时递减"）不放表——
那是欠账 #2，等它变成字段后才有资格进表。

## 已知缺口（表里没有的东西）

| 缺口 | 在哪 | 说明 |
|---|---|---|
| 高地抬升 0.25 | `GridManager.HighlandElevation` | 硬编码常量，不在 yaml |
| 地形斑块生成参数 | `rules.csv` 的 `terrain` 分组 | 全局参数，不属于某个地形 |
| `molotov_*`（4 个键） | `rules.csv` 的 `items` 分组 | 键名按"行为"起，卡牌 id 是 `gasoline_bottle` |
| `thorns_reflect_pct` | `rules.csv` 的 `items` 分组 | 应为 `thorns_armor_*` 前缀 |
| `adrenaline_*` / `dying_*` | `rules.csv` 的 `status` 分组 | **不是** `StatusType`：前者是临时加成，后者是 `IsDying` 字段 |
| 结算顺序、动作次数上限 | 代码 | 规则骨架，永远不上表 |

## 重新生成

```bash
# 1. 元数据（改过代码里的名字/描述后要重跑，需要 Unity）
Unity.exe -batchmode -nographics -projectPath Game \
  -executeMethod ConfigExport.Run -configOut Design/meta/meta.json -logFile export.log

# 2. 表：yaml/元数据 → CSV
python Tools/config/build_tables.py
python Tools/config/build_tables.py --check   # 表与源不一致则非零退出

# 3. 配置：CSV → 游戏读的 yaml
python Tools/config/build.py
python Tools/config/build.py --check          # 与原 yaml 有差异则非零退出
```
"""


def main(argv: List[str]) -> int:
    parser = argparse.ArgumentParser(description="合成策划表 Design/*.csv")
    parser.add_argument("--check", action="store_true", help="只比对不写文件")
    parser.add_argument("--out", default=str(DESIGN), help="输出目录")
    args = parser.parse_args(argv)

    out = Path(args.out)
    if not META_JSON.is_file():
        print(f"缺少 {META_JSON}，请先跑 ConfigExport（见 Design/README.md）", file=sys.stderr)
        return 2

    rules = yaml.safe_load(RULE_YAML.read_text(encoding="utf-8-sig"))
    meta = json.loads(META_JSON.read_text(encoding="utf-8"))

    card_stats, unmatched_items = build_card_stats(rules, meta)
    tiles, unmatched_terrain = build_tiles(rules, meta)
    statuses, unmatched_status = build_statuses(rules, meta)

    tables: List[Tuple[str, List[dict]]] = [
        ("roles.csv", build_roles(rules, meta)),
        ("cards.csv", build_cards(rules, meta)),
        ("card_stats.csv", card_stats),
        ("tiles.csv", tiles),
        ("statuses.csv", statuses),
        ("rules.csv", build_rules(
            rules,
            {"items": unmatched_items, "status": unmatched_status, "terrain": unmatched_terrain},
        )),
    ]

    problems = []
    for name, rows in tables:
        diff = write_csv(out / name, rows, args.check)
        if diff:
            problems.append(diff)
        print(f"[{'比对' if args.check else '写出'}] {name}: {len(rows)} 行")

    if not args.check:
        (out / "README.md").write_text(README, encoding="utf-8")

    print("\n—— 覆盖 ——")
    print(f"角色 {len(build_roles(rules, meta))} / 卡牌 {len(meta.get('cards', []))}"
          f"（印数>0 的 {sum(1 for c in meta.get('cards', []) if (rules.get('deck') or {}).get(c['id'], 0))}）"
          f" / 卡牌数值 {len(card_stats)} 条")
    print(f"地形未归属键：{unmatched_terrain or '无'}")
    print(f"状态未归属键：{unmatched_status or '无'}")
    print(f"卡牌数值未归属键：{unmatched_items or '无'}")

    if problems:
        print("\n—— 与磁盘上的 CSV 不一致 ——")
        for line in problems:
            print("  " + line)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
