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

from anomaly_analysis.detection.runner import run_anomaly  # noqa: E402


def main() -> int:
    p = argparse.ArgumentParser(description="逻辑 QA · Isolation Forest 扫描")
    p.add_argument("--input", type=str, default=str(SIM_ROOT / "output"))
    p.add_argument("--model", type=str, default=str(ROOT / "models"))
    p.add_argument("--out", type=str, default=str(ROOT / "reports"))
    p.add_argument("--baseline-report", type=str, default=None)
    args = p.parse_args()

    input_dir = _abs(args.input)
    model_dir = _abs(args.model)
    out_dir = _abs(args.out)
    br = None
    if args.baseline_report:
        br = _abs(args.baseline_report)
    else:
        cand = out_dir / "baseline_report.json"
        if cand.is_file():
            br = cand

    report = run_anomaly(input_dir, model_dir, out_dir, baseline_report_path=br)
    print(json.dumps(report["summary"], ensure_ascii=False, indent=2))
    print(f"Wrote {out_dir / 'anomaly_report.json'}")
    return 0


def _abs(p: str) -> Path:
    path = Path(p)
    return path if path.is_absolute() else (Path.cwd() / path).resolve()


if __name__ == "__main__":
    raise SystemExit(main())
