from __future__ import annotations

import json
from pathlib import Path

import pytest

LOGIC_QA_ROOT = Path(__file__).resolve().parent.parent.parent
TOOLS_ROOT = LOGIC_QA_ROOT.parent
REPO_ROOT = TOOLS_ROOT.parent
SIM_ROOT = TOOLS_ROOT / "sim"


@pytest.fixture
def sim_root() -> Path:
    return SIM_ROOT


@pytest.fixture
def logic_qa_root() -> Path:
    return LOGIC_QA_ROOT


@pytest.fixture
def output_dir(sim_root: Path) -> Path:
    return sim_root / "output"


def _snap(t: int, units, weather="clear", hour=6, round_=0, lava=0, deck=40):
    return {
        "t": t,
        "type": "snapshot",
        "round": round_,
        "weather": weather,
        "hour": hour,
        "lava_inset": lava,
        "deck_draw": deck,
        "units": units,
    }


def _unit(role, hp, max_hp=None, statuses=None, weight=0, cap=15, dead=False, **kw):
    if dead:
        return {"role": role, "dead": True}
    u = {
        "role": role,
        "cell": [0, 0],
        "hp": hp,
        "max_hp": max_hp if max_hp is not None else hp,
        "atk": 8,
        "def": 6,
        "move": 4,
        "vis": 8,
        "bag": [],
        "weight": weight,
        "cap": cap,
        "statuses": statuses or [],
        "skill_level": 1,
        "tile": "normal",
    }
    u.update(kw)
    return u


@pytest.fixture
def clean_events():
    units0 = [
        _unit("elephant", 100, 100),
        _unit("human", 90, 90),
        _unit("monkey", 90, 90),
        _unit("cat", 60, 60),
    ]
    units1 = [
        _unit("elephant", 80, 100),
        _unit("human", 90, 90),
        _unit("monkey", 90, 90),
        _unit("cat", 60, 60),
    ]
    return [
        {"t": 1, "type": "match_start", "seed": 1},
        _snap(2, units0),
        {"t": 3, "type": "turn_start", "actor": "human", "round": 1, "hour": 6},
        {
            "t": 4,
            "type": "damage",
            "actor": "human",
            "target": "elephant",
            "kind": "physical",
            "raw": 30,
            "dealt": 20,
            "hp_after": 80,
        },
        {"t": 5, "type": "turn_end", "actor": "human", "skipped": False},
        _snap(6, units1, round_=0),
        {"t": 7, "type": "lava_shrink", "inset": 1, "cells": 68},
        _snap(8, units1, lava=1, round_=5),
        {"t": 9, "type": "match_end", "reason": "last_standing", "winner": "human"},
    ]


@pytest.fixture
def fixture_dir(tmp_path: Path, clean_events):
    """写入干净 + 若干违规迷你对局。"""
    clean = tmp_path / "match_clean"
    clean.mkdir()
    _write_jsonl(clean / "events.jsonl", clean_events)
    (clean / "meta.json").write_text(json.dumps({"seed": 1}), encoding="utf-8")

    bad_true = tmp_path / "match_bad_true"
    bad_true.mkdir()
    ev = [
        {"t": 1, "type": "match_start"},
        _snap(
            2,
            [
                _unit("elephant", 100),
                _unit("human", 90),
                _unit("monkey", 90),
                _unit("cat", 60),
            ],
        ),
        {
            "t": 3,
            "type": "damage",
            "actor": "lava",
            "target": "cat",
            "kind": "true",
            "raw": 20,
            "dealt": 10,
            "hp_after": 50,
        },
    ]
    _write_jsonl(bad_true / "events.jsonl", ev)

    bad_rain = tmp_path / "match_bad_rain"
    bad_rain.mkdir()
    ev = [
        _snap(
            1,
            [
                _unit("elephant", 100, statuses=["burning"]),
                _unit("human", 90),
                _unit("monkey", 90),
                _unit("cat", 60),
            ],
            weather="rain",
        )
    ]
    _write_jsonl(bad_rain / "events.jsonl", ev)

    bad_dying = tmp_path / "match_bad_dying"
    bad_dying.mkdir()
    ev = [
        _snap(
            1,
            [
                _unit("elephant", 0, 100, statuses=["dying"]),
                _unit("human", 90),
                _unit("monkey", 90),
                _unit("cat", 60),
            ],
        ),
        # 濒死允许 move；非法的是抽牌/用卡
        {"t": 2, "type": "draw", "actor": "elephant", "item": "bomb"},
    ]
    _write_jsonl(bad_dying / "events.jsonl", ev)

    bad_dead = tmp_path / "match_bad_dead"
    bad_dead.mkdir()
    ev = [
        {"t": 1, "type": "death", "actor": "cat"},
        {"t": 2, "type": "draw", "actor": "cat", "item": "bomb"},
    ]
    _write_jsonl(bad_dead / "events.jsonl", ev)

    bad_phys = tmp_path / "match_bad_phys"
    bad_phys.mkdir()
    ev = [
        {
            "t": 1,
            "type": "damage",
            "actor": "human",
            "target": "cat",
            "kind": "physical",
            "raw": 5,
            "dealt": 9,
            "hp_after": 51,
        }
    ]
    _write_jsonl(bad_phys / "events.jsonl", ev)

    bad_hp = tmp_path / "match_bad_hp"
    bad_hp.mkdir()
    ev = [
        _snap(
            1,
            [
                _unit("elephant", 120, 100),
                _unit("human", 90),
                _unit("monkey", 90),
                _unit("cat", 60),
            ],
        )
    ]
    _write_jsonl(bad_hp / "events.jsonl", ev)

    bad_lava = tmp_path / "match_bad_lava"
    bad_lava.mkdir()
    ev = [
        {"t": 1, "type": "lava_shrink", "inset": 2, "cells": 1},
        {"t": 2, "type": "lava_shrink", "inset": 1, "cells": 1},
    ]
    _write_jsonl(bad_lava / "events.jsonl", ev)

    return tmp_path


def _write_jsonl(path: Path, events):
    with path.open("w", encoding="utf-8") as f:
        for e in events:
            f.write(json.dumps(e, ensure_ascii=False) + "\n")
