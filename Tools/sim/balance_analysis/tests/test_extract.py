from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from balance_analysis.aggregate import aggregate_records
from balance_analysis.extract import extract_from_events


def test_extract_survival_and_winner():
    events = [
        {"t": 1, "type": "match_start", "seed": 1},
        {"t": 2, "type": "clock", "hour": 8, "full_round": 1},
        {"t": 3, "type": "clock", "hour": 10, "full_round": 2},
        {"t": 4, "type": "death", "actor": "cat"},
        {"t": 5, "type": "clock", "hour": 12, "full_round": 3},
        {"t": 6, "type": "death", "actor": "human"},
        {"t": 7, "type": "match_end", "reason": "last_standing", "winner": "elephant"},
    ]
    meta = {"winner": "elephant", "reason": "last_standing", "full_rounds": 3}
    row = extract_from_events(events, meta=meta, match_id="m1")
    assert row["winner"] == "elephant"
    assert row["per_role"]["cat"]["died"] is True
    assert row["per_role"]["cat"]["survival_rounds"] == 2
    assert row["per_role"]["human"]["survival_rounds"] == 3
    assert row["per_role"]["elephant"]["survived_to_end"] is True
    assert row["per_role"]["elephant"]["survival_rounds"] == 3


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
