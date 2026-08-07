#!/usr/bin/env python3
"""Fixed-seed RL evaluation harness (Unity -rlEval).

Reports episode winners to stdout / JSON. Does not replace Tools/sim balance reports.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from collections import Counter
from pathlib import Path

SIM_ROOT = Path(__file__).resolve().parents[2] / "sim"
sys.path.insert(0, str(SIM_ROOT))
from elephant_sim.unity_batch import find_unity_editor, game_project_path  # noqa: E402

RL_ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    p = argparse.ArgumentParser(description="Evaluate RL / mixed seats via Unity -rlEval")
    p.add_argument("--unity", type=str, default=None)
    p.add_argument("--episodes", type=int, default=20)
    p.add_argument("--seed", type=int, default=100)
    p.add_argument("--role", default="human", help="bootstrap learning seat under eval")
    p.add_argument("--curriculum", choices=["bootstrap", "selfplay"], default="bootstrap")
    p.add_argument("--out", type=str, default=str(RL_ROOT / "reports" / "rl_eval.json"))
    args = p.parse_args()

    unity = Path(args.unity) if args.unity else find_unity_editor()
    if unity is None or not unity.is_file():
        print("ERROR: set UNITY_EDITOR to Unity.exe", file=sys.stderr)
        return 127

    project = game_project_path()
    log = RL_ROOT / "unity_rl_eval.log"
    cmd = [
        str(unity),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(project),
        "-executeMethod",
        "RlTrainingBatch.Run",
        "-logFile",
        str(log),
        "-rlEval",
        "-rlCurriculum",
        args.curriculum,
        "-rlRole",
        args.role,
        "-episodes",
        str(args.episodes),
        "-seed",
        str(args.seed),
    ]
    print(" ", " ".join(cmd))
    code = subprocess.call(cmd)

    winners: Counter[str] = Counter()
    if log.is_file():
        text = log.read_text(encoding="utf-8", errors="ignore")
        for m in re.finditer(r"eval episode .* winner=(\w+|)", text):
            w = m.group(1) or "none"
            winners[w] += 1

    payload = {
        "seed": args.seed,
        "episodes_requested": args.episodes,
        "curriculum": args.curriculum,
        "role": args.role,
        "unity_exit": code,
        "winners": dict(winners),
        "disclaimer": "RL eval under current Game AI/heuristic mix; not human balance. No maxRounds.",
        "log": str(log),
    }
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(payload, ensure_ascii=False, indent=2))
    return 0 if code == 0 else code


if __name__ == "__main__":
    raise SystemExit(main())
