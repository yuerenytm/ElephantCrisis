#!/usr/bin/env python3
"""转发到 logic_qa/scripts/run_anomaly.py。"""
from __future__ import annotations

import runpy
import sys
from pathlib import Path

target = Path(__file__).resolve().parents[2] / "scripts" / "run_anomaly.py"
sys.argv[0] = str(target)
runpy.run_path(str(target), run_name="__main__")
