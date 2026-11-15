#!/usr/bin/env python3
"""数值健康入口：指标汇总 + 两批数据的配对比较。

用法：
    # 跑一批新数据并看指标
    python Tools/balance/run.py --name tune_v2 --matches 200 --seed 50000

    # 只看已有数据
    python Tools/balance/run.py --data Tools/sim/output/tune_v2

    # 与基线对比（同种子，逐局配对）
    python Tools/balance/run.py --data Tools/sim/output/tune_v2 --baseline Tools/sim/output/base

退出码：仅统计，不阻断（数值是设计选择，不是错误）。
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(REPO_ROOT / "Tools"))

from lab.experiment import DEFAULT_OUTPUT, Experiment  # noqa: E402
from lab.report import kv_block, write_markdown  # noqa: E402

from balance.metrics import aggregate_matches  # noqa: E402
from balance.stats import paired_diff, summarize, wilson_interval  # noqa: E402


def _load_meta_by_seed(data_dir: Path) -> dict[int, dict]:
    out = {}
    for meta_path in data_dir.glob("match_*/meta.json"):
        try:
            meta = json.loads(meta_path.read_text(encoding="utf-8"))
        except Exception:
            continue
        seed = meta.get("seed")
        if seed is not None:
            out[int(seed)] = meta
    return out


def _compare(cand_dir: Path, base_dir: Path) -> dict:
    """同种子逐局配对：赢/没赢的翻转次数。"""
    cand = _load_meta_by_seed(cand_dir)
    base = _load_meta_by_seed(base_dir)
    common = sorted(set(cand) & set(base))
    pairs = []
    for seed in common:
        a = base[seed].get("winner")
        b = cand[seed].get("winner")
        pairs.append((a is not None, b is not None))
    result = paired_diff(pairs)
    result["compared"] = len(common)
    return result


def main() -> int:
    p = argparse.ArgumentParser(description="数值健康：指标与配对比较")
    p.add_argument("--name", default=None, help="实验名（会先跑一批新数据）")
    p.add_argument("--matches", type=int, default=200)
    p.add_argument("--seed", type=int, default=50000)
    p.add_argument("--collect", default="balance", choices=["balance", "logic", "both"])
    p.add_argument("--data", default=None)
    p.add_argument("--baseline", default=None, help="基线数据目录（同种子配对比较）")
    p.add_argument("--out", default=None)
    args = p.parse_args()

    data_dir = Path(args.data) if args.data else None
    if args.name:
        exp = Experiment(name=args.name, matches=args.matches, seed=args.seed, collect=args.collect)
        code = exp.run()
        if code != 0:
            print(f"[balance] 跑批失败，退出码 {code}")
            return code
        data_dir = exp.dir
    if data_dir is None:
        data_dir = DEFAULT_OUTPUT
    if not data_dir.exists():
        print(f"[balance] 数据目录不存在：{data_dir}")
        return 2

    out_dir = Path(args.out) if args.out else (data_dir / "reports")
    out_dir.mkdir(parents=True, exist_ok=True)

    print(f"[balance] 指标：{data_dir}")
    report = aggregate_matches(data_dir)
    summary = report["summary"]

    rows = [("对局数", summary["matches"]),
            ("平均回合", summary["avg_full_rounds"]),
            ("结束原因", summary["end_reasons"])]
    rounds = summary.get("full_rounds") or {}
    if rounds:
        rows.append(("回合分布", f"中位 {rounds.get('median')} / p90 {rounds.get('p90')}"))

    print("  " + " / ".join(f"{k}: {v}" for k, v in rows[:2]))
    print(f"  回合：{rounds}")

    print("  胜率（含 95% 区间）：")
    role_rows = []
    for role, info in report["by_role"].items():
        n = info["matches"]
        wins = info["wins"]
        lo, hi = wilson_interval(wins, n)
        line = f"{role}: {info['win_rate']:.1%}（{lo:.0%}~{hi:.0%}）" if n else f"{role}: 无数据"
        print(f"    {line}")
        role_rows.append((role, f"{info['win_rate']:.1%} [{lo:.0%}~{hi:.0%}]"))

    sections = [("概况", kv_block(rows)), ("胜率与区间", kv_block(role_rows))]

    if args.baseline:
        base_dir = Path(args.baseline)
        print(f"[balance] 与基线配对比较：{base_dir}")
        cmp = _compare(data_dir, base_dir)
        print(f"  共同种子 {cmp['compared']} 局：仅 B 赢 {cmp['only_b']}，"
              f"仅 A 赢 {cmp['only_a']}，都赢 {cmp['both']}，都没赢 {cmp['neither']}")
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
                        f"数值指标 · {data_dir.name}", sections)
    print(f"[balance] 报告：{md}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
