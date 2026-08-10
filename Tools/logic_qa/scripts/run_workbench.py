#!/usr/bin/env python3
"""启动逻辑 QA Streamlit 工作台。"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "app" / "streamlit_app.py"


def main() -> int:
    cmd = [
        sys.executable,
        "-m",
        "streamlit",
        "run",
        str(APP),
        "--server.port",
        "8502",
        "--browser.gatherUsageStats",
        "false",
    ]
    # 默认尝试打开浏览器；无 GUI/CI 时可设 LOGIC_QA_HEADLESS=1
    import os

    if os.environ.get("LOGIC_QA_HEADLESS", "").strip() in ("1", "true", "yes"):
        cmd.extend(["--server.headless", "true"])
    return subprocess.call(cmd, cwd=str(ROOT))


if __name__ == "__main__":
    raise SystemExit(main())
