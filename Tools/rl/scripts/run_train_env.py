#!/usr/bin/env python3
"""Launch Unity PlayMode RL training environment (-rlTrain)."""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

SIM_ROOT = Path(__file__).resolve().parents[2] / "sim"
sys.path.insert(0, str(SIM_ROOT))
from elephant_sim.unity_batch import find_unity_editor, game_project_path  # noqa: E402

RL_ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    p = argparse.ArgumentParser(description="Unity RL train env (connects to mlagents-learn)")
    p.add_argument("--unity", type=str, default=None)
    p.add_argument("--curriculum", choices=["bootstrap", "selfplay"], default="bootstrap")
    p.add_argument("--role", default="human", help="bootstrap learning role")
    p.add_argument("--league-dir", type=str, default=None)
    p.add_argument("--ghost-ratio", type=float, default=0.3)
    args = p.parse_args()

    unity = Path(args.unity) if args.unity else find_unity_editor()
    if unity is None or not unity.is_file():
        print("ERROR: set UNITY_EDITOR to Unity.exe", file=sys.stderr)
        return 127

    project = game_project_path()
    log = RL_ROOT / "unity_rl_train.log"
    cmd = [
        str(unity),
        "-projectPath",
        str(project),
        "-executeMethod",
        "RlTrainingBatch.Run",
        "-logFile",
        str(log),
        "-rlTrain",
        "-rlCurriculum",
        args.curriculum,
        "-rlRole",
        args.role,
    ]
    if args.league_dir:
        cmd.extend(["-rlLeagueDir", str(Path(args.league_dir).resolve())])
        cmd.extend(["-rlGhostRatio", str(args.ghost_ratio)])

    print("Launching Unity RL env:")
    print(" ", " ".join(cmd))
    print("NOTE: start mlagents-learn first; close other Editor on this project.")
    print("NOTE: no maxRounds — match ends by lava / win rules only.")
    return subprocess.call(cmd)


if __name__ == "__main__":
    raise SystemExit(main())
