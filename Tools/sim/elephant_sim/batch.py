"""跑批入口：调用 Tools/sim_runner（dotnet 无头跑批）。

规则来自 Game/Assets/Scripts/Simulation —— 与游戏运行时同一份源码，
所以这里跑出来的就是游戏的结果，只是不需要启动 Unity。

历史上的 `unity_batch.py` 已删除：它驱动的 Unity 侧跑批模块在
"引擎无关"改造后不再存在。
"""
from __future__ import annotations

import shutil
import subprocess
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent.parent
RUNNER_DIR = REPO_ROOT / "Tools" / "sim_runner"


def run_logic_sim(
    matches: int,
    out_dir: Path,
    base_seed: int = 1,
    max_rounds: int = 80,
    clean: bool = True,
    collect: str = "both",
    **_ignored,
) -> int:
    """跑一批对局，返回进程退出码（0 = 成功且至少产生一局）。

    参数名与旧接口保持一致，便于老调用方平滑迁移；
    `unity_exe` / `timeout` 已无意义，传入会被忽略。
    """
    if not RUNNER_DIR.exists():
        print(f"[batch] 找不到跑批器目录：{RUNNER_DIR}")
        return 2

    if clean and out_dir.exists():
        for d in out_dir.glob("match_*"):
            shutil.rmtree(d, ignore_errors=True)

    cmd = [
        "dotnet", "run", "--project", str(RUNNER_DIR), "-c", "Release", "--",
        "-matches", str(matches),
        "-seed", str(base_seed),
        "-maxRounds", str(max_rounds),
        "-collect", collect,
        "-out", str(out_dir),
    ]
    print("[batch] " + " ".join(cmd))
    try:
        return subprocess.run(cmd, cwd=str(REPO_ROOT)).returncode
    except FileNotFoundError:
        print("[batch] 找不到 dotnet，请确认 .NET SDK 已安装并在 PATH 中")
        return 2
