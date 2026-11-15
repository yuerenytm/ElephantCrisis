"""跑批执行：调用 Tools/sim_runner（dotnet），不启动 Unity。

规则来自 Game/Assets/Scripts/Simulation（与游戏同一份源码），
所以这里产出的就是游戏的结果，只是不需要引擎。
"""
from __future__ import annotations

import shutil
import subprocess
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
RUNNER_DIR = REPO_ROOT / "Tools" / "runner" / "SimRunner"


def run_batch(
    out_dir: Path,
    *,
    matches: int,
    seed: int = 1,
    max_rounds: int = 100,
    collect: str = "both",
    clean: bool = True,
) -> int:
    """跑一批对局，返回退出码（0 = 成功）。"""
    if not RUNNER_DIR.exists():
        print(f"[batch] 找不到跑批器：{RUNNER_DIR}")
        return 2

    if clean and out_dir.exists():
        for d in out_dir.glob("match_*"):
            shutil.rmtree(d, ignore_errors=True)

    cmd = [
        "dotnet", "run", "--project", str(RUNNER_DIR), "-c", "Release", "--",
        "-matches", str(matches),
        "-seed", str(seed),
        "-maxRounds", str(max_rounds),
        "-collect", collect,
        "-out", str(out_dir),
    ]
    try:
        return subprocess.run(cmd, cwd=str(REPO_ROOT)).returncode
    except FileNotFoundError:
        print("[batch] 找不到 dotnet，请确认 .NET SDK 已安装")
        return 2
