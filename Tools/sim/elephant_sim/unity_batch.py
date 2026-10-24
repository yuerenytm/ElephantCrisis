"""启动 Unity 的 LogicSim 批处理，产出 match_*/ 日志目录。

本模块只负责「环境探测 + 拼命令行 + 收尾报错」，不含任何游戏规则：
真正的模拟由 Game 侧的 LogicSimRunner 在 Unity 进程内完成。
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import List, Optional


def repo_root() -> Path:
    """仓库根目录（由本文件位置向上推三级：elephant_sim → sim → Tools → 根）。"""
    return Path(__file__).resolve().parents[3]


def game_project_path() -> Path:
    """Unity 工程目录：仓库根下的 Game/。"""
    return repo_root() / "Game"


def find_unity_editor() -> Optional[Path]:
    """探测 Unity.exe 路径，找不到返回 None。

    优先级：
    1. 环境变量 UNITY_EDITOR / UNITY_PATH（显式指定优先）
    2. Unity Hub 默认安装目录（取版本号最大者）
    3. 常见自定义/便携安装：D:\\Unity、E:\\Unity、C:\\Unity、LOCALAPPDATA
    4. 工程 ProjectSettings/ProjectVersion.txt 里的版本号精确匹配
    """
    env = os.environ.get("UNITY_EDITOR") or os.environ.get("UNITY_PATH")
    if env:
        p = Path(env)
        if p.is_file():
            return p

    candidates: List[Path] = []

    # Unity Hub default installs on Windows
    hub = Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")) / "Unity" / "Hub" / "Editor"
    if hub.is_dir():
        for child in sorted(hub.iterdir(), reverse=True):
            exe = child / "Editor" / "Unity.exe"
            if exe.is_file():
                candidates.append(exe)

    # Common custom / portable layouts (e.g. D:\Unity\Unity Engine\Editor\Unity.exe)
    for root in (
        Path(r"D:\Unity"),
        Path(r"E:\Unity"),
        Path(r"C:\Unity"),
        Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "Unity",
    ):
        if not root.is_dir():
            continue
        direct = root / "Unity Engine" / "Editor" / "Unity.exe"
        if direct.is_file():
            candidates.append(direct)
        editor = root / "Editor" / "Unity.exe"
        if editor.is_file():
            candidates.append(editor)
        for exe in root.glob("**/Editor/Unity.exe"):
            if exe.is_file():
                candidates.append(exe)

    # ProjectVersion hint (Hub layout)
    ver_file = game_project_path() / "ProjectSettings" / "ProjectVersion.txt"
    if ver_file.is_file() and hub.is_dir():
        text = ver_file.read_text(encoding="utf-8", errors="ignore")
        for line in text.splitlines():
            if line.startswith("m_EditorVersion:"):
                ver = line.split(":", 1)[1].strip()
                exact = hub / ver / "Editor" / "Unity.exe"
                if exact.is_file():
                    return exact

    # de-dupe preserving order
    seen = set()
    uniq: List[Path] = []
    for c in candidates:
        key = str(c.resolve())
        if key in seen:
            continue
        seen.add(key)
        uniq.append(c)
    return uniq[0] if uniq else None


def find_running_unity() -> List[int]:
    """返回正在运行的 Unity.exe 进程 PID 列表（仅 Windows）。

    空列表表示没有 Unity 在跑；非 Windows 平台恒为空。
    """
    pids: List[int] = []
    if os.name != "nt":
        return pids
    try:
        out = subprocess.run(
            ["tasklist", "/FI", "IMAGENAME eq Unity.exe", "/FO", "CSV", "/NH"],
            capture_output=True,
            text=True,
            timeout=15,
        )
    except Exception:
        return pids
    for line in out.stdout.splitlines():
        line = line.strip()
        if not line or line.lower().startswith("info"):
            continue
        # CSV: "Unity.exe","1234","Console","1","1,234,567 K"
        parts = [p.strip().strip('"') for p in line.split(",")]
        if len(parts) >= 2 and parts[0].lower() == "unity.exe":
            try:
                pids.append(int(parts[1]))
            except ValueError:
                continue
    return pids


def clear_match_outputs(out_dir: Path) -> int:
    """删除输出目录下所有 match_* 子目录和旧 batch_summary.json。

    unity_logic_sim.log 不删（追加写入，便于排查历史）。
    返回删除的目录数。
    """
    if not out_dir.is_dir():
        return 0
    removed = 0
    for p in out_dir.iterdir():
        if p.is_dir() and p.name.startswith("match_"):
            shutil.rmtree(p, ignore_errors=True)
            removed += 1
    summary = out_dir / "batch_summary.json"
    if summary.is_file():
        summary.unlink(missing_ok=True)
    return removed


def run_unity_logic_sim(
    matches: int,
    out_dir: Path,
    base_seed: int = 1,
    max_rounds: int = 80,
    unity_exe: Optional[Path] = None,
    clean: bool = True,
    collect: str = "both",
    timeout: Optional[float] = None,
) -> int:
    """以 Unity batchmode 跑一批 LogicSim，返回进程退出码。

    Args:
        matches: 对局数量。
        out_dir: 产物目录（会在其中创建 match_<seed>/）。
        base_seed: 起始种子，逐局 +1。
        max_rounds: 单局完整回合上限。
        unity_exe: 指定 Unity.exe；缺省自动探测。
        clean: 是否先清空旧的 match_*。
        collect: balance / logic / both，透传给 Game 侧决定写哪些文件。
        timeout: 整体超时秒数；None 表示不限制（默认）。

    Returns:
        0 成功；127 找不到 Unity 或工程；128 有 Unity 正在运行；
        124 超时；其余为 Unity 进程自身的退出码。
    """
    unity = unity_exe or find_unity_editor()
    if unity is None or not Path(unity).is_file():
        print(
            "ERROR: Unity Editor not found. Set UNITY_EDITOR to Unity.exe path.",
            file=sys.stderr,
        )
        return 127

    project = game_project_path()
    if not (project / "ProjectSettings").is_dir():
        print(f"ERROR: Game project not found at {project}", file=sys.stderr)
        return 127

    # 启动 batchmode 前先检测：已打开的 Unity Editor 会让 batchmode 直接失败。
    allow_running = os.environ.get("ELEPHANT_ALLOW_RUNNING_UNITY", "") in ("1", "true", "yes")
    running = find_running_unity()
    if running and not allow_running:
        print(
            "\n" + "=" * 72 + "\n"
            "!! 检测到 Unity Editor 正在运行，请先关闭后再跑批量仿真。\n"
            f"!! 正在运行的 Unity.exe 进程 PID: {running}\n"
            "!! 若不关闭，Unity -batchmode 会报 'another Unity instance is running' 并直接失败。\n"
            "!! 如果确定这些是别的工程、不影响本工程，可设环境变量 ELEPHANT_ALLOW_RUNNING_UNITY=1 跳过此检查。\n"
            + "=" * 72 + "\n",
            file=sys.stderr,
        )
        return 128

    out_dir = out_dir.resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    if clean:
        n = clear_match_outputs(out_dir)
        if n:
            print(f"Cleared {n} old match_* under {out_dir}")

    log_file = out_dir / "unity_logic_sim.log"
    cmd = [
        str(unity),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(project),
        "-executeMethod",
        "LogicSimEditorBatch.Run",
        "-logFile",
        str(log_file),
        "-logicSim",
        "-matches",
        str(matches),
        "-seed",
        str(base_seed),
        "-maxRounds",
        str(max_rounds),
        "-collect",
        str(collect),
        "-out",
        str(out_dir),
    ]

    print("Launching Unity LogicSim:")
    print(" ", " ".join(cmd))
    try:
        proc = subprocess.run(cmd, check=False, timeout=timeout)
    except subprocess.TimeoutExpired:
        print(
            f"ERROR: Unity batchmode 超过 {timeout} 秒未结束，已终止。日志：{log_file}",
            file=sys.stderr,
        )
        return 124

    print(f"Unity exit code: {proc.returncode} (log: {log_file})")
    if proc.returncode != 0 and log_file.is_file():
        text = log_file.read_text(encoding="utf-8", errors="ignore")
        for needle in (
            "another Unity instance is running",
            "Multiple Unity instances cannot open",
            "Scripts have compiler errors",
            "executeMethod method",
            "Fatal error",
        ):
            if needle.lower() in text.lower():
                # print a short hint from the log
                for line in text.splitlines():
                    if needle.lower() in line.lower():
                        print(f"HINT: {line.strip()}", file=sys.stderr)
                        break
                break
    return proc.returncode
