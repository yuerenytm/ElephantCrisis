#!/usr/bin/env python3
"""数值平衡统计：胜率 / 死亡率 / 存活回合 / 终局原因。造数=Unity Game LogicSim。"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent  # balance_analysis
SIM = ROOT.parent / "sim"  # Tools/sim
for p in (ROOT, SIM):
    s = str(p)
    if s not in sys.path:
        sys.path.insert(0, s)

from aggregate import (  # noqa: E402
    aggregate_matches,
    format_console_table,
    write_report,
)
from elephant_sim.unity_batch import run_unity_logic_sim  # noqa: E402

SIM_OUT = SIM / "output"


def main() -> int:
    p = argparse.ArgumentParser(description="对局平衡统计（真源：Game LogicSim）")
    p.add_argument("--input", type=str, default=str(SIM_OUT), help="对局日志目录（默认 sim/output）")
    p.add_argument("--out", type=str, default=str(ROOT / "reports"), help="报告输出目录")
    p.add_argument("--matches", type=int, default=0, help=">0 时先批量仿真再统计")
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--max-rounds", type=int, default=80)
    args = p.parse_args()

    input_dir = Path(args.input)
    if not input_dir.is_absolute():
        input_dir = (ROOT / input_dir).resolve()
    out_dir = Path(args.out)
    if not out_dir.is_absolute():
        out_dir = (ROOT / out_dir).resolve()

    if args.matches and args.matches > 0:
        print(f"[1/2] batch {args.matches} matches engine=unity → {input_dir}")
        code = run_unity_logic_sim(
            matches=args.matches,
            out_dir=input_dir,
            base_seed=args.seed,
            max_rounds=args.max_rounds,
            clean=True,
            collect="balance",
        )
        if code != 0:
            print(f"Unity LogicSim failed: {code}", file=sys.stderr)
            return code
    else:
        print(f"[1/2] skip batch, use {input_dir}")

    print("[2/2] aggregate")
    report = aggregate_matches(input_dir)
    write_report(report, out_dir)
    print(format_console_table(report))
    print(f"Wrote {out_dir / 'balance_report.json'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
