"""法伤公式规则的回归测试。

背景：这条规则曾经误报过两次，原因都不是规则本身错，而是"数据不够"：
1. 用快照时刻的法抗去核算受击时刻的伤害 —— 天气/地形一变就对不上；
   现在引擎把受击时刻的 mr 写进伤害事件。
2. 没考虑溢出击杀 —— 目标只剩 2 血时吃 10 点法伤，只结算 2 点。
这两条要锁住，否则下次改动又会退回到"红一片但没人看"。
"""
from __future__ import annotations

import sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parent.parent
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from qa.checks import check_match_events  # noqa: E402


def _dmg(t: int, raw: int, dealt: int, mr=None, via="melee_magic", target="human"):
    e = {"t": t, "type": "damage", "actor": "elephant", "target": target,
         "kind": "magic", "raw": raw, "dealt": dealt, "via": via}
    if mr is not None:
        e["mr"] = mr
    return e


def _ids(events):
    return [v.rule_id for v in check_match_events(events)]


def test_exact_formula_passes():
    # raw 12、法抗 45% → 应扣 6
    assert _ids([_dmg(10, 12, 6, mr=45)]) == []


def test_more_damage_than_formula_is_violation():
    # 多扣一律不允许（法抗只能减免）
    assert "dmg_magic_formula" in _ids([_dmg(10, 12, 9, mr=45)])


def test_less_damage_without_death_is_violation():
    # 少扣且没人死 → 说不通
    assert "dmg_magic_formula" in _ids([_dmg(10, 12, 4, mr=45)])


def test_less_damage_with_overkill_death_is_allowed():
    # 少扣但同一时刻有人死亡 → 溢出击杀，合法
    events = [_dmg(20, 10, 2, mr=0), {"t": 20, "type": "death", "role": "human"}]
    assert "dmg_magic_formula" not in _ids(events)


def test_full_absorb_is_allowed():
    # dealt == 0：护盾/免疫全额吸收
    assert _ids([_dmg(10, 12, 0, mr=45)]) == []


def test_mr_zero_and_full_damage_passes():
    assert _ids([_dmg(10, 10, 10, mr=0)]) == []


def test_burning_and_poison_use_event_mr():
    # 着火/中毒也走同一公式，引擎已带上 mr
    assert _ids([_dmg(5, 10, 10, mr=0, via="burning", target="cat")]) == []
    assert "dmg_magic_formula" in _ids([_dmg(6, 10, 3, mr=0, via="poison", target="cat")])
