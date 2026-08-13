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
from anomaly_analysis.detection.runner import run_anomaly, train_anomaly_model  # noqa: E402
from elephant_sim.unity_batch import run_unity_logic_sim  # noqa: E402


def main() -> int:
    p = argparse.ArgumentParser(description="逻辑 QA：batch → baseline → (train) → anomaly")
    p.add_argument("--matches", type=int, default=0, help=">0 时先跑 Unity LogicSim 批量对局")
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--input", type=str, default=str(SIM_ROOT / "output"))
    p.add_argument("--reports", type=str, default=str(ROOT / "reports"))
    p.add_argument("--model", type=str, default=str(ROOT / "models"))
    p.add_argument("--train", action="store_true", help="基线后重新训练 IF 模型")
    p.add_argument("--skip-anomaly", action="store_true")
    args = p.parse_args()

    input_dir = _abs(args.input)
    reports = _abs(args.reports)
    model_dir = _abs(args.model)

    if args.matches > 0:
        print(f"[1/4] batch {args.matches} matches engine=unity")
        code = run_unity_logic_sim(
            matches=args.matches,
            out_dir=input_dir,
            base_seed=args.seed,
            clean=True,
            collect="logic",
        )
        if code != 0:
            print(f"Unity LogicSim failed: exit {code}", file=sys.stderr)
            return code
    else:
        print("[1/4] skip batch (use existing logs)")

    print("[2/4] baseline")
    base = run_baseline(input_dir, reports)
    print(json.dumps(base["summary"], ensure_ascii=False, indent=2))

    if args.train:
        print("[3/4] train anomaly model")
        train_summary = train_anomaly_model(
            input_dir,
            model_dir,
            baseline_report_path=reports / "baseline_report.json",
        )
        print(json.dumps(train_summary, ensure_ascii=False, indent=2))
    else:
        print("[3/4] skip train")

    if args.skip_anomaly:
        print("[4/4] skip anomaly")
        return 1 if base["summary"].get("failed", 0) else 0

    if not (model_dir / "iforest.joblib").is_file():
        print("[4/4] no model found, training once...")
        train_anomaly_model(
            input_dir,
            model_dir,
            baseline_report_path=reports / "baseline_report.json",
        )

    print("[4/4] anomaly scan")
    anom = run_anomaly(
        input_dir,
        model_dir,
        reports,
        baseline_report_path=reports / "baseline_report.json",
    )
    print(json.dumps(anom["summary"], ensure_ascii=False, indent=2))

    unified = {"baseline": base["summary"], "anomaly": anom["summary"]}
    with (reports / "qa_summary.json").open("w", encoding="utf-8") as f:
        json.dump(unified, f, ensure_ascii=False, indent=2)
    print(f"Wrote {reports / 'qa_summary.json'}")
    return 1 if base["summary"].get("failed", 0) else 0


def _abs(p: str) -> Path:
    path = Path(p)
    return path if path.is_absolute() else (Path.cwd() / path).resolve()


if __name__ == "__main__":
    raise SystemExit(main())
