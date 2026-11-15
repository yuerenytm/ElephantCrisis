#!/usr/bin/env python3
"""批量对局：调用 Tools/sim_runner（dotnet，无 Unity）。

规则来自 Game/Assets/Scripts/Simulation —— 与游戏运行时同一份源码，
所以这里跑出来的结果就是游戏的结果，只是不需要启动引擎。
"""
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from elephant_sim.log_io import iter_match_dirs, load_match  # noqa: E402

REPO_ROOT = ROOT.parent.parent
RUNNER_DIR = REPO_ROOT / "Tools" / "sim_runner"


def _summarize_from_disk(out_dir: Path) -> dict:
    """从磁盘重读所有 match_*，汇总局数、胜者分布、平均回合与事件数。

    刻意不依赖本次进程内存：手动放进来的旧数据也能被统计。
    """
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
        "engine": "core_dotnet",
    }
    return {"summary": summary, "results": results}


def _run_dotnet(matches: int, out_dir: Path, seed: int, max_rounds: int,
                collect: str, clean: bool) -> int:
    """调用 dotnet 跑批器；返回其退出码（0 成功）。"""
    if not RUNNER_DIR.exists():
        print(f"找不到跑批器目录：{RUNNER_DIR}", file=sys.stderr)
        return 2

    if clean and out_dir.exists():
        for d in out_dir.glob("match_*"):
            shutil.rmtree(d, ignore_errors=True)

    cmd = [
        "dotnet", "run", "--project", str(RUNNER_DIR), "-c", "Release", "--",
        "-matches", str(matches),
        "-seed", str(seed),
        "-maxRounds", str(max_rounds),
        "-collect", collect,
        "-out", str(out_dir),
    ]
    print("执行：", " ".join(cmd))
    return subprocess.run(cmd, cwd=str(REPO_ROOT)).returncode


def main() -> int:
    """命令行入口：解析参数 → 调 dotnet 跑批器 → 汇总写 batch_summary.json。"""
    p = argparse.ArgumentParser(description="ElephantCrisis 批量对局（dotnet 无头跑批）")
    p.add_argument("--matches", type=int, default=100, help="对局数量")
    p.add_argument("--out", type=str, default="output", help="输出目录")
    p.add_argument("--seed", type=int, default=1, help="起始 seed（逐局 +1）")
    p.add_argument("--max-rounds", type=int, default=100, help="单局完整回合上限")
    p.add_argument(
        "--collect",
        type=str,
        default="both",
        choices=["balance", "logic", "both"],
        help="采集模式：balance=仅宏观(meta)，logic=仅日志(events)，both=两者",
    )
    p.add_argument(
        "--no-clean",
        action="store_true",
        help="保留 output 中已有 match_*",
    )
    args = p.parse_args()

    out_dir = Path(args.out)
    if not out_dir.is_absolute():
        out_dir = ROOT / out_dir

    code = _run_dotnet(
        matches=args.matches,
        out_dir=out_dir,
        seed=args.seed,
        max_rounds=args.max_rounds,
        collect=args.collect,
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
