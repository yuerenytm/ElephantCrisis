#!/usr/bin/env python3
"""Launch Unity Editor PlayMode with -perfAuto; JSONL under persistentDataPath/ElephantPerf/."""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

SIM_ROOT = Path(__file__).resolve().parents[2] / "sim"
sys.path.insert(0, str(SIM_ROOT))
from elephant_sim.unity_batch import find_unity_editor, game_project_path  # noqa: E402


def main() -> int:
    p = argparse.ArgumentParser(
        description="客户端性能采集（与 LogicSim 逻辑 JSONL 完全隔离）"
    )
    p.add_argument("--unity", type=str, default=None, help="Unity.exe")
    args = p.parse_args()

    unity = Path(args.unity) if args.unity else find_unity_editor()
    if unity is None or not unity.is_file():
        print("ERROR: set UNITY_EDITOR to Unity.exe", file=sys.stderr)
        return 127

    project = game_project_path()
    log = Path(__file__).resolve().parents[1] / "unity_perf.log"
    cmd = [
        str(unity),
        "-batchmode",
        "-projectPath",
        str(project),
        "-executeMethod",
        "PerfAutoEditorBatch.Run",
        "-logFile",
        str(log),
        "-perfAuto",
        "-perfCollect",
    ]
    print("Launching Unity perf auto-run")
    print(" ", " ".join(cmd))
    print("Sessions: Application.persistentDataPath/ElephantPerf/session_*/events.jsonl")
    print("Analyze:  python Tools/perf_analysis/scripts/... (see README)")
    return subprocess.run(cmd, check=False).returncode


if __name__ == "__main__":
    raise SystemExit(main())
