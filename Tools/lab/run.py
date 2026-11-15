#!/usr/bin/env python3
"""跑批入口：跑一批对局并写 batch_summary.json。

    python Tools/lab/run.py --name base_v1 --matches 100 --seed 50000

实验元数据（配置指纹、git 版本、种子区间）会写进 <out>/manifest.json。
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "Tools"))

from lab.events import iter_match_dirs, load_match  # noqa: E402
from lab.experiment import DEFAULT_OUTPUT, Experiment  # noqa: E402


def summarize(out_dir: Path) -> dict:
    rows = []
    for d in iter_match_dirs(out_dir):
        try:
            m = load_match(d)
        except Exception:
            continue
        meta = m.meta
        rows.append({
            "seed": meta.get("seed"),
            "winner": meta.get("winner"),
            "reason": meta.get("reason"),
            "full_rounds": meta.get("full_rounds"),
        })
    winners = Counter(r["winner"] or "none" for r in rows)
    return {
        "matches": len(rows),
        "winners": dict(winners),
        "avg_rounds": sum(r["full_rounds"] or 0 for r in rows) / max(1, len(rows)),
    }


def main() -> int:
    p = argparse.ArgumentParser(description="跑一批对局（dotnet 无头跑批器）")
    p.add_argument("--name", required=True, help="实验名（输出目录名）")
    p.add_argument("--matches", type=int, default=100)
    p.add_argument("--seed", type=int, default=50000)
    p.add_argument("--max-rounds", type=int, default=100)
    p.add_argument("--collect", default="both", choices=["balance", "logic", "both"])
    p.add_argument("--out", default=None, help="自定义输出根目录")
    p.add_argument("--no-clean", action="store_true")
    args = p.parse_args()

    out_root = Path(args.out) if args.out else DEFAULT_OUTPUT
    exp = Experiment(
        name=args.name,
        matches=args.matches,
        seed=args.seed,
        collect=args.collect,
        max_rounds=args.max_rounds,
        out_dir=out_root / args.name,
    )
    code = exp.run(clean=not args.no_clean)
    if code != 0:
        print(f"[lab] 跑批失败，退出码 {code}")
        return code

    summary = summarize(exp.dir)
    with (exp.dir / "batch_summary.json").open("w", encoding="utf-8") as f:
        json.dump(summary, f, ensure_ascii=False, indent=2)
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    print(f"[lab] 数据：{exp.dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
