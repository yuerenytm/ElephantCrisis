#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
from pathsetup import SIM_ROOT, ensure_path  # noqa: E402

ensure_path()

from anomaly_analysis.baseline.runner import run_baseline  # noqa: E402


def main() -> int:
    p = argparse.ArgumentParser(description="逻辑 QA · 基线规则校验")
    p.add_argument("--input", type=str, default=str(SIM_ROOT / "output"))
    p.add_argument("--out", type=str, default=str(ROOT / "reports"))
    args = p.parse_args()

    input_dir = _abs(args.input)
    out_dir = _abs(args.out)

    report = run_baseline(input_dir, out_dir)
    print(json.dumps(report["summary"], ensure_ascii=False, indent=2))
    print(f"Wrote {out_dir / 'baseline_report.json'}")
    return 1 if report["summary"].get("failed", 0) else 0


def _abs(p: str) -> Path:
    path = Path(p)
    return path if path.is_absolute() else (Path.cwd() / path).resolve()


if __name__ == "__main__":
    raise SystemExit(main())
