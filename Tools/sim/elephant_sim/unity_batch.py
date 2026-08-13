"""Launch Unity LogicSim (Game scripts) to produce match_*/events.jsonl."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import List, Optional


def repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def game_project_path() -> Path:
    return repo_root() / "Game"


def find_unity_editor() -> Optional[Path]:
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
    """返回正在运行的 Unity.exe 进程 PID 列表（Windows）。空列表 = 没有 Unity 在跑。"""
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
) -> int:
    """
    Run Game LogicSim via Unity Editor batchmode.
    Returns process exit code.
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
    proc = subprocess.run(cmd, check=False)
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
