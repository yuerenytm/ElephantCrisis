from __future__ import annotations

from pathlib import Path

import pytest

from anomaly_analysis.baseline.checks import check_match_events
from anomaly_analysis.baseline.runner import run_baseline
from elephant_sim.log_io import load_events


def _ids(viols):
    return {v.rule_id for v in viols}


def test_clean_events_no_violations(clean_events):
    viols = check_match_events(clean_events, match_id="clean")
    assert viols == []


def test_true_damage_mitigation(fixture_dir: Path):
    events = load_events(fixture_dir / "match_bad_true" / "events.jsonl")
    assert "dmg_true_no_mitigation" in _ids(check_match_events(events))


def test_physical_bound(fixture_dir: Path):
    events = load_events(fixture_dir / "match_bad_phys" / "events.jsonl")
    assert "dmg_physical_bound" in _ids(check_match_events(events))


def test_magic_damage_bound_flags():
    """法伤 dealt 超 raw（法抗只能减免不能加成）→ 违规。"""
    events = [
        {"t": 1, "type": "damage", "actor": "cat", "target": "elephant",
         "kind": "magic", "raw": 10, "dealt": 12, "via": "melee_magic"}
    ]
    assert "dmg_magic_bound" in _ids(check_match_events(events))


def test_magic_damage_resist_reduction_ok():
    """法伤 dealt 在 [0, raw] 内（法抗减免/护盾全免）合法。"""
    events = [
        {"t": 1, "type": "damage", "actor": "cat", "target": "elephant",
         "kind": "magic", "raw": 10, "dealt": 4, "via": "melee_magic"}
    ]
    assert "dmg_magic_bound" not in _ids(check_match_events(events))


def _snap_with_mr(mr: int, role: str = "elephant") -> Dict[str, Any]:
    return {
        "t": 1,
        "type": "snapshot",
        "units": [
            {"role": "cat", "hp": 60, "max_hp": 60, "statuses": [], "dead": False, "magic_resist": 50},
            {"role": role, "hp": 100, "max_hp": 100, "statuses": [], "dead": False, "magic_resist": mr},
            {"role": "human", "hp": 90, "max_hp": 90, "statuses": [], "dead": False, "magic_resist": 20},
            {"role": "monkey", "hp": 90, "max_hp": 90, "statuses": [], "dead": False, "magic_resist": 20},
        ],
    }


def test_magic_formula_matches_snapshot_mr():
    """法伤精确公式：dealt 应为 ⌊raw × (1 − mr%)⌋。"""
    events = [
        _snap_with_mr(mr=20),
        {"t": 2, "type": "damage", "actor": "cat", "target": "elephant",
         "kind": "magic", "raw": 9, "dealt": 7, "via": "melee_magic"},  # ⌊9×0.8⌋=7
    ]
    assert "dmg_magic_formula" not in _ids(check_match_events(events))


def test_magic_formula_mismatch_flags():
    """法伤 dealt 与快照法抗不符 → 违规。"""
    events = [
        _snap_with_mr(mr=50),
        {"t": 2, "type": "damage", "actor": "cat", "target": "elephant",
         "kind": "magic", "raw": 10, "dealt": 6, "via": "melee_magic"},  # 应为 ⌊10×0.5⌋=5
    ]
    assert "dmg_magic_formula" in _ids(check_match_events(events))


def test_magic_formula_zero_ok_for_shield():
    """法伤 dealt=0（护盾全额吸收）合法，即便快照法抗推不出 0。"""
    events = [
        _snap_with_mr(mr=20),
        {"t": 2, "type": "damage", "actor": "cat", "target": "elephant",
         "kind": "magic", "raw": 10, "dealt": 0, "via": "melee_magic"},
    ]
    assert "dmg_magic_formula" not in _ids(check_match_events(events))


def test_magic_formula_skipped_without_mr_field():
    """旧日志快照无 magic_resist 时不检精确公式（只查边界）。"""
    events = [
        {"t": 1, "type": "snapshot", "units": [
            {"role": "cat", "hp": 60, "max_hp": 60, "statuses": [], "dead": False},
            {"role": "elephant", "hp": 100, "max_hp": 100, "statuses": [], "dead": False},
            {"role": "human", "hp": 90, "max_hp": 90, "statuses": [], "dead": False},
            {"role": "monkey", "hp": 90, "max_hp": 90, "statuses": [], "dead": False},
        ]},
        {"t": 2, "type": "damage", "actor": "cat", "target": "elephant",
         "kind": "magic", "raw": 10, "dealt": 6, "via": "melee_magic"},
    ]
    assert "dmg_magic_formula" not in _ids(check_match_events(events))


def test_rain_clears_burn(fixture_dir: Path):
    events = load_events(fixture_dir / "match_bad_rain" / "events.jsonl")
    assert "rain_clears_burn" in _ids(check_match_events(events))


def test_dying_no_draw(fixture_dir: Path):
    events = load_events(fixture_dir / "match_bad_dying" / "events.jsonl")
    assert "dying_no_draw" in _ids(check_match_events(events))


def test_dying_heal_clears_then_equip_ok():
    """脚下血瓶自救后解除濒死，随后 equip 不应再报 dying_no_draw。"""
    events = [
        {"t": 1, "type": "dying", "actor": "cat"},
        {
            "t": 2,
            "type": "heal",
            "actor": "cat",
            "item": "small_potion",
            "healed": 9,
            "hp_after": 9,
        },
        {"t": 3, "type": "equip", "actor": "cat", "item": "wood_armor"},
    ]
    assert "dying_no_draw" not in _ids(check_match_events(events))


def test_dying_equip_without_heal_still_fails():
    events = [
        {"t": 1, "type": "dying", "actor": "cat"},
        {"t": 2, "type": "equip", "actor": "cat", "item": "wood_armor"},
    ]
    assert "dying_no_draw" in _ids(check_match_events(events))


def test_true_damage_amulet_full_block_ok():
    """护身符可令真伤 dealt=0；仅部分减伤才违规。"""
    events = [
        {
            "t": 1,
            "type": "damage",
            "actor": "none",
            "target": "cat",
            "kind": "true",
            "raw": 12,
            "dealt": 0,
            "via": "cursed_blade",
        }
    ]
    assert "dmg_true_no_mitigation" not in _ids(check_match_events(events))


def test_no_turn_start_draw_flags_legacy_pattern():
    events = [
        {"t": 1, "type": "draw", "actor": "human", "item": "bomb"},
        {"t": 2, "type": "turn_start", "actor": "human", "round": 1},
    ]
    assert "no_turn_start_draw" in _ids(check_match_events(events))


def test_draw_after_turn_start_ok_for_leader():
    """领袖宣言等：turn_start 之后再 draw 合法。"""
    events = [
        {"t": 1, "type": "turn_start", "actor": "human", "round": 1},
        {"t": 2, "type": "draw", "actor": "human", "item": "bomb"},
    ]
    assert "no_turn_start_draw" not in _ids(check_match_events(events))


def test_hidden_break_recognizes_melee_via():
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "round": 0,
            "weather": "clear",
            "hour": 6,
            "lava_inset": 0,
            "deck_draw": 0,
            "units": [
                {
                    "role": "cat",
                    "hp": 60,
                    "max_hp": 60,
                    "statuses": ["hidden"],
                    "dead": False,
                },
                {"role": "human", "hp": 90, "max_hp": 90, "statuses": [], "dead": False},
                {"role": "elephant", "hp": 100, "max_hp": 100, "statuses": [], "dead": False},
                {"role": "monkey", "hp": 90, "max_hp": 90, "statuses": [], "dead": False},
            ],
        },
        {
            "t": 2,
            "type": "damage",
            "actor": "cat",
            "target": "human",
            "kind": "physical",
            "raw": 10,
            "dealt": 4,
            "via": "melee",
        },
    ]
    assert "hidden_break_on_attack" in _ids(check_match_events(events))


def test_hidden_break_via_leave_jungle_ok():
    """离林破隐（丛林来源隐匿）后再攻击不算违规。"""
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "units": [
                {
                    "role": "cat",
                    "hp": 60,
                    "max_hp": 60,
                    "statuses": ["hidden"],
                    "dead": False,
                },
                {"role": "human", "hp": 90, "max_hp": 90, "statuses": [], "dead": False},
                {"role": "elephant", "hp": 100, "max_hp": 100, "statuses": [], "dead": False},
                {"role": "monkey", "hp": 90, "max_hp": 90, "statuses": [], "dead": False},
            ],
        },
        {
            "t": 2,
            "type": "status",
            "status": "hidden",
            "actor": "cat",
            "op": "leave_jungle",
        },
        {
            "t": 3,
            "type": "damage",
            "actor": "cat",
            "target": "human",
            "kind": "magic",
            "raw": 10,
            "dealt": 4,
            "via": "melee_magic",
        },
    ]
    assert "hidden_break_on_attack" not in _ids(check_match_events(events))


def test_hidden_break_recognizes_melee_magic_via():
    """猫法伤普攻（via=melee_magic）同样须在攻击前破隐。"""
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "units": [
                {
                    "role": "cat",
                    "hp": 60,
                    "max_hp": 60,
                    "statuses": ["hidden"],
                    "dead": False,
                },
                {"role": "human", "hp": 90, "max_hp": 90, "statuses": [], "dead": False},
                {"role": "elephant", "hp": 100, "max_hp": 100, "statuses": [], "dead": False},
                {"role": "monkey", "hp": 90, "max_hp": 90, "statuses": [], "dead": False},
            ],
        },
        {
            "t": 2,
            "type": "damage",
            "actor": "cat",
            "target": "human",
            "kind": "magic",
            "raw": 10,
            "dealt": 4,
            "via": "melee_magic",
        },
    ]
    assert "hidden_break_on_attack" in _ids(check_match_events(events))


def test_quasi_crossbow_requires_equipped_flags():
    """弩已蓄力但未装备弩（卸下应清除蓄力）→ 违规。"""
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "units": [
                {
                    "role": "human",
                    "dead": False,
                    "crossbow_charged": True,
                    "bag_items": [{"kind": "crossbow", "charges": 0, "equipped": False}],
                },
                {"role": "elephant", "dead": False},
                {"role": "monkey", "dead": False},
                {"role": "cat", "dead": False},
            ],
        }
    ]
    assert "quasi_crossbow_requires_equipped" in _ids(check_match_events(events))


def test_quasi_crossbow_charged_with_equipped_ok():
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "units": [
                {
                    "role": "human",
                    "dead": False,
                    "crossbow_charged": True,
                    "bag_items": [{"kind": "crossbow", "charges": 0, "equipped": True}],
                },
                {"role": "elephant", "dead": False},
                {"role": "monkey", "dead": False},
                {"role": "cat", "dead": False},
            ],
        }
    ]
    assert "quasi_crossbow_requires_equipped" not in _ids(check_match_events(events))


def test_quasi_motorcycle_requires_equipped_flags():
    """摩托车发动中但未装备 → 违规。"""
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "units": [
                {
                    "role": "human",
                    "dead": False,
                    "motorcycle_active": 2,
                    "bag_items": [],
                },
                {"role": "elephant", "dead": False},
                {"role": "monkey", "dead": False},
                {"role": "cat", "dead": False},
            ],
        }
    ]
    assert "quasi_motorcycle_requires_equipped" in _ids(check_match_events(events))


def test_quasi_nonneg_flags_negative():
    """红牛待执行次数等准状态计数器为负 → 违规。"""
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "units": [
                {
                    "role": "cat",
                    "dead": False,
                    "pending_extra_actions": -1,
                },
                {"role": "elephant", "dead": False},
                {"role": "monkey", "dead": False},
                {"role": "human", "dead": False},
            ],
        }
    ]
    assert "quasi_nonneg" in _ids(check_match_events(events))


def test_quasi_fields_absent_no_violations():
    """旧引擎（无准状态字段）快照不应误报。"""
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "units": [
                {"role": "human", "dead": False, "bag": ["crossbow", "arrow"]},
                {"role": "elephant", "dead": False},
                {"role": "monkey", "dead": False},
                {"role": "cat", "dead": False},
            ],
        }
    ]
    ids = _ids(check_match_events(events))
    assert "quasi_nonneg" not in ids
    assert "quasi_crossbow_requires_equipped" not in ids
    assert "quasi_motorcycle_requires_equipped" not in ids


def test_dead_no_action(fixture_dir: Path):
    events = load_events(fixture_dir / "match_bad_dead" / "events.jsonl")
    assert "dead_no_action" in _ids(check_match_events(events))


def test_hp_bounds(fixture_dir: Path):
    events = load_events(fixture_dir / "match_bad_hp" / "events.jsonl")
    assert "hp_bounds" in _ids(check_match_events(events))


def test_lava_monotonic(fixture_dir: Path):
    events = load_events(fixture_dir / "match_bad_lava" / "events.jsonl")
    assert "lava_shrink_monotonic" in _ids(check_match_events(events))


def test_runner_writes_report(fixture_dir: Path, tmp_path: Path):
    out = tmp_path / "reports"
    report = run_baseline(fixture_dir, out)
    assert (out / "baseline_report.json").is_file()
    assert report["summary"]["matches"] >= 7
    assert report["summary"]["failed"] >= 6
    assert report["summary"]["passed"] >= 1


@pytest.mark.slow
def test_real_output_baseline_optional(output_dir: Path, tmp_path: Path):
    matches = list(output_dir.glob("match_*/events.jsonl")) if output_dir.is_dir() else []
    if not matches:
        pytest.skip("no sim/output matches")
    report = run_baseline(output_dir, tmp_path / "reports")
    # 现有引擎健康时期望 0 违规；若有误报再收紧规则
    assert report["summary"]["failed"] == 0, report["summary"]


def _four_units(**vis_kw):
    """四个存活单位，共用同一 vis（及可选 bag_items/statuses）。"""
    return [
        {
            "role": r,
            "hp": 90,
            "max_hp": 90,
            "statuses": vis_kw.get("statuses", []),
            "dead": False,
            "vis": vis_kw["vis"],
            "bag_items": vis_kw.get("bag_items", []),
        }
        for r in ("elephant", "human", "monkey", "cat")
    ]


@pytest.mark.parametrize(
    "hour,weather,vis",
    [
        (6, "clear", 40),   # 清晨晴：无限
        (12, "clear", 40),  # 白天晴
        (18, "clear", 40),  # 黄昏晴
        (0, "clear", 5),    # 黑夜晴
        (6, "rain", 40),    # 清晨雨：无限
        (12, "rain", 40),
        (18, "rain", 40),
        (0, "rain", 4),     # 黑夜雨
        (6, "fog", 6),      # 清晨雾
        (12, "fog", 8),     # 白天雾
        (18, "fog", 6),     # 黄昏雾
        (0, "fog", 3),      # 黑夜雾
    ],
)
def test_vis_period_weather_matrix_ok(hour, weather, vis):
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "hour": hour,
            "weather": weather,
            "units": _four_units(vis=vis),
        }
    ]
    assert "vis_period_weather" not in _ids(check_match_events(events))


def test_vis_period_weather_wrong_flags():
    """白天晴天应为无限(40)，写成 8 → 违规。"""
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "hour": 12,
            "weather": "clear",
            "units": _four_units(vis=8),
        }
    ]
    assert "vis_period_weather" in _ids(check_match_events(events))


def test_vis_dying_overrides_period():
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "hour": 12,
            "weather": "clear",
            "units": [
                {
                    "role": "elephant",
                    "hp": 0,
                    "max_hp": 100,
                    "statuses": ["dying"],
                    "dead": False,
                    "vis": 1,
                    "bag_items": [],
                },
                {"role": "human", "hp": 90, "max_hp": 90, "statuses": [], "dead": False, "vis": 40, "bag_items": []},
                {"role": "monkey", "hp": 90, "max_hp": 90, "statuses": [], "dead": False, "vis": 40, "bag_items": []},
                {"role": "cat", "hp": 60, "max_hp": 60, "statuses": [], "dead": False, "vis": 40, "bag_items": []},
            ],
        }
    ]
    assert "vis_period_weather" not in _ids(check_match_events(events))


def test_vis_night_vision_and_raincoat():
    """黑夜夜视：晴+3=8、雨+2=6；雨衣使雨按晴再+3=8。"""
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "hour": 0,
            "weather": "rain",
            "units": [
                {
                    "role": "elephant",
                    "hp": 100,
                    "max_hp": 100,
                    "statuses": [],
                    "dead": False,
                    "vis": 8,  # 雨衣→晴矩阵5 + 夜视+3
                    "bag_items": [
                        {"kind": "night_vision", "charges": 1, "equipped": True},
                        {"kind": "rubber_raincoat", "charges": 3, "equipped": True},
                    ],
                },
                {"role": "human", "hp": 90, "max_hp": 90, "statuses": [], "dead": False, "vis": 6, "bag_items": [
                    {"kind": "night_vision", "charges": 1, "equipped": True},
                ]},
                {"role": "monkey", "hp": 90, "max_hp": 90, "statuses": [], "dead": False, "vis": 4, "bag_items": []},
                {"role": "cat", "hp": 60, "max_hp": 60, "statuses": [], "dead": False, "vis": 4, "bag_items": []},
            ],
        }
    ]
    assert "vis_period_weather" not in _ids(check_match_events(events))


def test_vis_telescope_fog_bonus():
    """望远镜：白天雾 8+2=10；无镜仍为 8。"""
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "hour": 12,
            "weather": "fog",
            "units": [
                {
                    "role": "elephant",
                    "hp": 100,
                    "max_hp": 100,
                    "statuses": [],
                    "dead": False,
                    "vis": 10,
                    "bag_items": [{"kind": "telescope", "charges": 1, "equipped": True}],
                },
                {"role": "human", "hp": 90, "max_hp": 90, "statuses": [], "dead": False, "vis": 8, "bag_items": []},
                {"role": "monkey", "hp": 90, "max_hp": 90, "statuses": [], "dead": False, "vis": 8, "bag_items": []},
                {"role": "cat", "hp": 60, "max_hp": 60, "statuses": [], "dead": False, "vis": 8, "bag_items": []},
            ],
        }
    ]
    assert "vis_period_weather" not in _ids(check_match_events(events))


def test_vis_skips_without_vis_field():
    """旧日志无 vis 字段时不检。"""
    events = [
        {
            "t": 1,
            "type": "snapshot",
            "hour": 12,
            "weather": "clear",
            "units": [
                {"role": "elephant", "hp": 100, "max_hp": 100, "statuses": [], "dead": False},
                {"role": "human", "hp": 90, "max_hp": 90, "statuses": [], "dead": False},
                {"role": "monkey", "hp": 90, "max_hp": 90, "statuses": [], "dead": False},
                {"role": "cat", "hp": 60, "max_hp": 60, "statuses": [], "dead": False},
            ],
        }
    ]
    assert "vis_period_weather" not in _ids(check_match_events(events))
