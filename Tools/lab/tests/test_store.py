"""存储层测试：用内存 SQLite 验证写入/查询/幂等。

数据库是工具链唯一的存储，坏了整条链就停，所以这层必须有测试。
"""
from __future__ import annotations

import sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parent.parent
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from lab.store import Store  # noqa: E402


def _meta(seed: int, winner: str = "elephant", rounds: int = 12) -> dict:
    return {
        "seed": seed,
        "winner": winner,
        "reason": "last_standing",
        "full_rounds": rounds,
        "per_role": {
            "elephant": {"survived": True, "death_round": None},
            "human": {"survived": False, "death_round": 5},
        },
    }


def _events(n: int = 3) -> list:
    return [
        {"t": i + 1, "type": "turn_start", "actor": "elephant"}
        for i in range(n)
    ]


def _new_store() -> Store:
    s = Store("sqlite://")
    s.ensure_schema()
    return s


def test_ensure_schema_is_idempotent():
    s = _new_store()
    s.ensure_schema()
    assert s.list_experiments() == []


def test_create_and_resolve_experiment():
    s = _new_store()
    s.create_experiment("v1", {"matches": 2, "seed_start": 100, "seed_end": 101,
                               "config_fingerprint": "abc123", "code_revision": "deadbee"})
    exp = s.resolve_experiment("v1")
    assert exp is not None
    assert exp["matches"] == 2
    assert exp["config_fingerprint"] == "abc123"


def test_resolve_missing_returns_none():
    assert _new_store().resolve_experiment("nope") is None


def test_add_match_and_load_roundtrip():
    s = _new_store()
    eid = s.create_experiment("v1", {"matches": 1})
    s.add_match(eid, _meta(50000), _events(3))
    s.commit()

    recs = s.load_matches("v1", with_events=True)
    assert len(recs) == 1
    r = recs[0]
    assert r.seed == 50000
    assert r.meta["winner"] == "elephant"
    assert len(r.events) == 3
    assert r.events[0]["type"] == "turn_start"


def test_load_without_events():
    s = _new_store()
    eid = s.create_experiment("v1", {"matches": 1})
    s.add_match(eid, _meta(50000), _events(2))
    s.commit()
    recs = s.load_matches("v1", with_events=False)
    assert recs[0].events == []
    assert recs[0].meta["full_rounds"] == 12


def test_experiments_are_isolated():
    s = _new_store()
    a = s.create_experiment("a", {"matches": 1})
    b = s.create_experiment("b", {"matches": 1})
    s.add_match(a, _meta(1, "cat"), _events(1))
    s.add_match(b, _meta(1, "human"), _events(1))
    s.commit()
    assert s.load_matches("a")[0].meta["winner"] == "cat"
    assert s.load_matches("b")[0].meta["winner"] == "human"


def test_unknown_experiment_raises():
    s = _new_store()
    try:
        s.load_matches("missing", with_events=False)
    except KeyError:
        return
    raise AssertionError("应当抛 KeyError")
