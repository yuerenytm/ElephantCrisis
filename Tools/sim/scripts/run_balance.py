#!/usr/bin/env python3
"""数值平衡统计：胜率 / 死亡率 / 存活回合 / 终局原因。造数=Unity Game LogicSim。"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from balance_analysis.aggregate import (  # noqa: E402
    aggregate_matches,
    format_console_table,
    write_report,
)
from elephant_sim.unity_batch import run_unity_logic_sim  # noqa: E402


def main() -> int:
    p = argparse.ArgumentParser(description="对局平衡统计（真源：Game LogicSim）")
    p.add_argument("--input", type=str, default="output", help="对局日志目录")
    p.add_argument("--out", type=str, default="reports", help="报告输出目录")
    p.add_argument("--matches", type=int, default=0, help=">0 时先批量仿真再统计")
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--max-rounds", type=int, default=80)
    args = p.parse_args()

    input_dir = _abs(args.input)
    out_dir = _abs(args.out)

    if args.matches and args.matches > 0:
        print(f"[1/2] batch {args.matches} matches engine=unity → {input_dir}")
        code = run_unity_logic_sim(
            matches=args.matches,
            out_dir=input_dir,
            base_seed=args.seed,
            max_rounds=args.max_rounds,
            clean=True,
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


def _abs(p: str) -> Path:
    path = Path(p)
    return path if path.is_absolute() else (ROOT / path)


if __name__ == "__main__":
    raise SystemExit(main())
