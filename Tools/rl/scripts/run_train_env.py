#!/usr/bin/env python3
"""Launch Unity RL training environment.

支持两种环境来源：
- 默认：Unity Editor（-batchmode -nographics 无头，省美术渲染）
- --env-path：独立 build 的可执行文件（广场模式，配合 num_envs 多开）
"""
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
    p.add_argument(
        "--headless",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="无头模式（-batchmode -nographics），默认开；--no-headless 可看画面调试",
    )
    p.add_argument(
        "--env-path",
        type=str,
        default=None,
        help="广场模式：直接运行 build 出的 Player 可执行文件，而非 Unity Editor",
    )
    args = p.parse_args()

    log = RL_ROOT / "unity_rl_train.log"
    common = [
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
        common.extend(["-rlLeagueDir", str(Path(args.league_dir).resolve())])
        common.extend(["-rlGhostRatio", str(args.ghost_ratio)])

    if args.env_path:
        # 广场模式：直接跑 build 出的 Player
        # 注意：Player 不能带 -executeMethod（那是 Editor 专属）；RlTrainingRunner 的
        # RuntimeInitializeOnLoadMethod 会检测 -rlTrain 自动进入训练。
        exe = Path(args.env_path).resolve()
        if not exe.is_file():
            print(f"ERROR: build exe not found: {exe}", file=sys.stderr)
            return 127
        player_args = [
            "-logFile",
            str(log),
            "-rlTrain",
            "-rlCurriculum",
            args.curriculum,
            "-rlRole",
            args.role,
        ]
        if args.league_dir:
            player_args.extend(["-rlLeagueDir", str(Path(args.league_dir).resolve())])
            player_args.extend(["-rlGhostRatio", str(args.ghost_ratio)])
        cmd = [str(exe)] + player_args
        print("Launching RL Player (广场模式):")
        print(" ", " ".join(cmd))
        return subprocess.call(cmd)

    unity = Path(args.unity) if args.unity else find_unity_editor()
    if unity is None or not unity.is_file():
        print("ERROR: set UNITY_EDITOR to Unity.exe", file=sys.stderr)
        return 127

    project = game_project_path()
    cmd = [str(unity), "-projectPath", str(project)]
    if args.headless:
        cmd.extend(["-batchmode", "-nographics"])
    cmd.extend(common)

    print("Launching Unity RL env:")
    print(" ", " ".join(cmd))
    print("NOTE: start mlagents-learn first; close other Editor on this project.")
    print("NOTE: no maxRounds — match ends by lava / win rules only.")
    return subprocess.call(cmd)


if __name__ == "__main__":
    raise SystemExit(main())
