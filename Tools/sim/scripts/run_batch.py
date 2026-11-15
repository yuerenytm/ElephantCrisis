#!/usr/bin/env python3
"""兼容入口：跑一批对局并写 batch_summary.json。

等价于 `python Tools/lab/run.py`，保留这个名字是因为文档和习惯都在用它。
真正的实现在 Tools/lab/ 里。
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent.parent
sys.path.insert(0, str(REPO_ROOT / "Tools"))

from lab.events import iter_match_dirs, load_match  # noqa: E402
from lab.runner import run_batch  # noqa: E402


def _summarize(out_dir: Path) -> dict:
    results = []
    for d in iter_match_dirs(out_dir):
        try:
            m = load_match(d)
        except Exception:
            continue
        meta = m.meta
        results.append({
            "seed": meta.get("seed"),
            "path": str(d),
            "winner": meta.get("winner"),
            "reason": meta.get("reason"),
            "full_rounds": meta.get("full_rounds"),
            "events": meta.get("events", len(m.events)),
            "source": meta.get("source", "unknown"),
        })
    winners = Counter(r["winner"] or "none" for r in results)
    return {
        "summary": {
            "matches": len(results),
            "winners": dict(winners),
            "avg_rounds": sum(r["full_rounds"] or 0 for r in results) / max(1, len(results)),
            "engine": "core_dotnet",
        },
        "results": results,
    }


def main() -> int:
    p = argparse.ArgumentParser(description="跑一批对局（dotnet 无头）")
    p.add_argument("--matches", type=int, default=100)
    p.add_argument("--out", default="output")
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--max-rounds", type=int, default=100)
    p.add_argument("--collect", default="both", choices=["balance", "logic", "both"])
    p.add_argument("--no-clean", action="store_true")
    args = p.parse_args()

    out_dir = Path(args.out)
    if not out_dir.is_absolute():
        out_dir = REPO_ROOT / "Tools" / "sim" / out_dir

    code = run_batch(out_dir, matches=args.matches, seed=args.seed,
                     max_rounds=args.max_rounds, collect=args.collect,
                     clean=not args.no_clean)

    payload = _summarize(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    with (out_dir / "batch_summary.json").open("w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)
    print(json.dumps(payload["summary"], ensure_ascii=False, indent=2))
    print(f"Wrote {out_dir / 'batch_summary.json'}")
    return code if code != 0 else (0 if payload["summary"]["matches"] > 0 else 1)


if __name__ == "__main__":
    raise SystemExit(main())
