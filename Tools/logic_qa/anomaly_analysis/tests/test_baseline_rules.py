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
