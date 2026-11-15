#!/usr/bin/env python3
"""数值健康入口：从数据库读实验，出指标 + 与基线配对比较。

用法：
    python Tools/balance/run.py --experiment tune_v2
    python Tools/balance/run.py --experiment tune_v2 --baseline base_v1
    python Tools/balance/run.py --experiment tune_v2 --dsn "mysql://user:pw@host/elephant_crisis"

退出码：0（只做统计，不阻断）。
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "Tools"))

from lab.report import kv_block, write_markdown  # noqa: E402
from lab.run import DEFAULT_DSN  # noqa: E402
from lab.store import Store  # noqa: E402

from balance.metrics import aggregate_logs  # noqa: E402
from balance.stats import paired_diff, wilson_interval  # noqa: E402

REPORT_DIR = REPO_ROOT / "Tools" / "lab" / "reports"


def main() -> int:
    p = argparse.ArgumentParser(description="数值健康：指标与配对比较（数据来自数据库）")
    p.add_argument("--experiment", required=True)
    p.add_argument("--baseline", default=None)
    p.add_argument("--dsn", default=None)
    p.add_argument("--out", default=None, help="报告目录（默认 Tools/lab/reports/<实验>）")
    args = p.parse_args()

    store = Store(args.dsn or DEFAULT_DSN)
    try:
        records = store.load_matches(args.experiment, with_events=False)
    except KeyError as e:
        print(f"[balance] {e}")
        return 2
    if not records:
        print(f"[balance] 实验 {args.experiment} 没有数据")
        return 2

    out_dir = Path(args.out) if args.out else REPORT_DIR / args.experiment
    out_dir.mkdir(parents=True, exist_ok=True)

    report = aggregate_logs(records)
    summary = report["summary"]
    rounds = summary.get("full_rounds") or {}

    print(f"[balance] 实验 {args.experiment}：{summary['matches']} 局，"
          f"平均回合 {summary['avg_full_rounds']}")
    print(f"  回合分布：中位 {rounds.get('median')} / p90 {rounds.get('p90')}")

    print("  胜率（含 95% 区间）：")
    role_rows = []
    for role, info in report["by_role"].items():
        n, wins = info["matches"], info["wins"]
        if not n:
            print(f"    {role}: 无数据")
            continue
        lo, hi = wilson_interval(wins, n)
        print(f"    {role}: {info['win_rate']:.1%}（{lo:.0%}~{hi:.0%}）")
        role_rows.append((role, f"{info['win_rate']:.1%} [{lo:.0%}~{hi:.0%}]"))

    sections = [
        ("概况", kv_block([
            ("对局数", summary["matches"]),
            ("平均回合", summary["avg_full_rounds"]),
            ("回合中位/p90", f"{rounds.get('median')} / {rounds.get('p90')}"),
            ("结束原因", summary["end_reasons"]),
        ])),
        ("胜率与 95% 区间", kv_block(role_rows)),
    ]

    if args.baseline:
        try:
            base = store.load_matches(args.baseline, with_events=False)
        except KeyError as e:
            print(f"[balance] {e}")
            return 2
        base_by_seed = {int(m.meta["seed"]): m.meta for m in base if m.meta.get("seed") is not None}
        pairs = []
        for rec in records:
            seed = rec.meta.get("seed")
            if seed is None or int(seed) not in base_by_seed:
                continue
            pairs.append((base_by_seed[int(seed)].get("winner") is not None,
                          rec.meta.get("winner") is not None))
        cmp = paired_diff(pairs)
        cmp["compared"] = len(pairs)
        print(f"[balance] 与 {args.baseline} 配对（共同种子 {cmp['compared']} 局）："
              f"仅候选赢 {cmp['only_b']}，仅基线赢 {cmp['only_a']}，"
              f"都赢 {cmp['both']}，都没赢 {cmp['neither']}")
        sections.append(("与基线比较", kv_block([
            ("共同种子", cmp["compared"]),
            ("仅候选赢", cmp["only_b"]),
            ("仅基线赢", cmp["only_a"]),
        ])))
        with (out_dir / "compare.json").open("w", encoding="utf-8") as f:
            json.dump(cmp, f, ensure_ascii=False, indent=2)

    with (out_dir / "balance_metrics.json").open("w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    md = write_markdown(out_dir / "balance_report.md",
                        f"数值指标 · {args.experiment}", sections)
    print(f"[balance] 报告：{md}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
