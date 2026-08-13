from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]  # balance_analysis
SIM = ROOT.parent / "sim"  # Tools/sim
for p in (ROOT, SIM):
    s = str(p)
    if s not in sys.path:
        sys.path.insert(0, s)

from elephant_sim.log_io import MatchLog  # noqa: E402

from aggregate import aggregate_records  # noqa: E402
from extract import extract_match  # noqa: E402


def test_extract_from_meta_per_role():
    meta = {
        "winner": "elephant",
        "reason": "last_standing",
        "full_rounds": 12,
        "per_role": {
            "elephant": {"death_round": None, "survived": True},
            "human": {"death_round": 5, "survived": False},
            "monkey": {"death_round": 9, "survived": False},
            "cat": {"death_round": 3, "survived": False},
        },
    }
    log = MatchLog(match_id="m1", path=Path("."), events=[], meta=meta)
    row = extract_match(log)
    assert row["winner"] == "elephant"
    assert row["per_role"]["cat"]["died"] is True
    assert row["per_role"]["cat"]["survival_rounds"] == 3
    assert row["per_role"]["human"]["survival_rounds"] == 5
    assert row["per_role"]["elephant"]["survived_to_end"] is True
    assert row["per_role"]["elephant"]["survival_rounds"] == 12


def test_aggregate_win_death_rates():
    rows = []
    for i, winner in enumerate(["elephant", "elephant", "cat", "monkey"]):
        rows.append(
            {
                "match_id": f"m{i}",
                "winner": winner,
                "reason": "last_standing",
                "full_rounds": 10 + i,
                "strategies": {
                    "elephant": "greedy",
                    "human": "greedy",
                    "monkey": "greedy",
                    "cat": "greedy",
                },
                "per_role": {
                    "elephant": {
                        "won": winner == "elephant",
                        "died": winner != "elephant",
                        "survival_rounds": 10 if winner != "elephant" else 12,
                        "survived_to_end": winner == "elephant",
                    },
                    "human": {
                        "won": False,
                        "died": True,
                        "survival_rounds": 5,
                        "survived_to_end": False,
                    },
                    "monkey": {
                        "won": winner == "monkey",
                        "died": winner != "monkey",
                        "survival_rounds": 8,
                        "survived_to_end": winner == "monkey",
                    },
                    "cat": {
                        "won": winner == "cat",
                        "died": winner != "cat",
                        "survival_rounds": 6,
                        "survived_to_end": winner == "cat",
                    },
                },
            }
        )
    report = aggregate_records(rows)
    assert report["summary"]["matches"] == 4
    assert report["by_role"]["elephant"]["wins"] == 2
    assert report["by_role"]["elephant"]["win_rate"] == 0.5
    assert report["by_role"]["human"]["death_rate"] == 1.0
    assert report["summary"]["end_reasons"]["last_standing"] == 4
