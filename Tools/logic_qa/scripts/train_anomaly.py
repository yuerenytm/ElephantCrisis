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

from anomaly_analysis.detection.runner import train_anomaly_model  # noqa: E402
from elephant_sim.unity_batch import run_unity_logic_sim  # noqa: E402

PKG = ROOT / "anomaly_analysis"


def main() -> int:
    p = argparse.ArgumentParser(description="逻辑 QA · 训练 Isolation Forest")
    p.add_argument("--input", type=str, default=str(SIM_ROOT / "output"))
    p.add_argument("--out", type=str, default=str(ROOT / "models"))
    p.add_argument("--config", type=str, default=None)
    p.add_argument("--matches", type=int, default=0, help=">0 时用 Game LogicSim 造数")
    p.add_argument("--seed", type=int, default=1000)
    p.add_argument("--baseline-report", type=str, default=None)
    p.add_argument("--no-baseline-filter", action="store_true")
    args = p.parse_args()

    input_dir = _abs(args.input)
    model_dir = _abs(args.out)
    cfg = Path(args.config) if args.config else PKG / "config" / "anomaly.yaml"
    if not cfg.is_absolute():
        cfg = _abs(str(cfg))

    if args.matches and args.matches > 0:
        print(f"generating {args.matches} Game LogicSim matches → {input_dir}")
        code = run_unity_logic_sim(
            matches=args.matches, out_dir=input_dir, base_seed=args.seed, clean=True
        )
        if code != 0:
            return code

    br = _abs(args.baseline_report) if args.baseline_report else None

    summary = train_anomaly_model(
        input_dir=input_dir,
        model_dir=model_dir,
        cfg_path=cfg,
        baseline_report_path=br,
        only_baseline_pass=not args.no_baseline_filter,
    )
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0


def _abs(p: str) -> Path:
    path = Path(p)
    return path if path.is_absolute() else (Path.cwd() / path).resolve()


if __name__ == "__main__":
    raise SystemExit(main())
