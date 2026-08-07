"""把 logic_qa 与同级 sim 加入 sys.path（二者均在 tools/ 下）。"""
from __future__ import annotations

import sys
from pathlib import Path

LOGIC_QA_ROOT = Path(__file__).resolve().parent
TOOLS_ROOT = LOGIC_QA_ROOT.parent
REPO_ROOT = TOOLS_ROOT.parent
SIM_ROOT = TOOLS_ROOT / "sim"


def ensure_path() -> None:
    for p in (LOGIC_QA_ROOT, SIM_ROOT):
        s = str(p)
        if s not in sys.path:
            sys.path.insert(0, s)
