#!/usr/bin/env python3
"""Batch match runner: Unity Game LogicSim (source of truth)."""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from elephant_sim.log_io import iter_match_dirs, load_match  # noqa: E402
from elephant_sim.unity_batch import run_unity_logic_sim  # noqa: E402


def _summarize_from_disk(out_dir: Path) -> dict:
    results = []
    for d in iter_match_dirs(out_dir):
        try:
            m = load_match(d)
        except Exception:
            continue
        meta = m.meta
        results.append(
            {
                "seed": meta.get("seed"),
                "path": str(d),
                "winner": meta.get("winner"),
                "reason": meta.get("reason"),
                "full_rounds": meta.get("full_rounds"),
                "events": meta.get("events", len(m.events)),
                "strategies": meta.get("strategies"),
                "source": meta.get("source", "unknown"),
            }
        )
    winners = Counter(r.get("winner") or "none" for r in results)
    reasons = Counter(r.get("reason") or "unknown" for r in results)
    summary = {
        "matches": len(results),
        "winners": dict(winners),
        "reasons": dict(reasons),
        "avg_rounds": sum(r.get("full_rounds", 0) or 0 for r in results) / max(1, len(results)),
        "avg_events": sum(r.get("events", 0) or 0 for r in results) / max(1, len(results)),
        "engine": "game_logic_sim",
    }
    return {"summary": summary, "results": results}


def main() -> int:
    p = argparse.ArgumentParser(description="ElephantCrisis 批量对局：Unity Game LogicSim（真源）")
    p.add_argument("--matches", type=int, default=100, help="对局数量")
    p.add_argument("--out", type=str, default="output", help="输出目录")
    p.add_argument("--seed", type=int, default=1, help="起始 seed（逐局 +1）")
    p.add_argument("--max-rounds", type=int, default=80, help="LogicSim 单局完整回合上限")
    p.add_argument("--unity", type=str, default=None, help="Unity.exe 路径（默认自动探测 / UNITY_EDITOR）")
    p.add_argument(
        "--no-clean",
        action="store_true",
        help="保留 output 中已有 match_*",
    )
    args = p.parse_args()

    out_dir = Path(args.out)
    if not out_dir.is_absolute():
        out_dir = ROOT / out_dir

    unity = Path(args.unity) if args.unity else None
    code = run_unity_logic_sim(
        matches=args.matches,
        out_dir=out_dir,
        base_seed=args.seed,
        max_rounds=args.max_rounds,
        unity_exe=unity,
        clean=not args.no_clean,
    )
    payload = _summarize_from_disk(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    with (out_dir / "batch_summary.json").open("w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)
    print(json.dumps(payload["summary"], ensure_ascii=False, indent=2))
    print(f"Wrote {out_dir / 'batch_summary.json'}")
    return code if code != 0 else (0 if payload["summary"]["matches"] > 0 else 1)


if __name__ == "__main__":
    raise SystemExit(main())
