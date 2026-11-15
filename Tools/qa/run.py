#!/usr/bin/env python3
"""健康检查入口：从数据库读一批对局，跑不变量与等价性。

用法：
    # 检查某个实验（不变量）
    python Tools/qa/run.py --experiment base_v1

    # 与另一个实验逐局对拍（证明重构没改行为）
    python Tools/qa/run.py --experiment after --baseline before

    # 生产库
    python Tools/qa/run.py --experiment base_v1 --dsn "mysql://user:pw@host/elephant_crisis"

退出码：0 = 通过；1 = 有阻断违规或对拍差异；2 = 实验不存在/参数错误。
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

from qa.equivalence import compare, format_console  # noqa: E402
from qa.runner import run_checks  # noqa: E402

REPORT_DIR = REPO_ROOT / "Tools" / "lab" / "reports"


def main() -> int:
    p = argparse.ArgumentParser(description="健康检查：不变量 + 等价性（数据来自数据库）")
    p.add_argument("--experiment", required=True, help="实验名")
    p.add_argument("--baseline", default=None, help="基线实验名（给了就做等价性对拍）")
    p.add_argument("--dsn", default=None)
    p.add_argument("--strict", action="store_true", help="把参考项也计入失败")
    p.add_argument("--out", default=None, help="报告目录（默认 Tools/lab/reports/<实验>）")
    args = p.parse_args()

    store = Store(args.dsn or DEFAULT_DSN)
    try:
        records = store.load_matches(args.experiment, with_events=True)
    except KeyError as e:
        print(f"[qa] {e}")
        return 2
    if not records:
        print(f"[qa] 实验 {args.experiment} 没有对局数据")
        return 2

    out_dir = Path(args.out) if args.out else REPORT_DIR / args.experiment
    out_dir.mkdir(parents=True, exist_ok=True)

    print(f"[qa] 实验 {args.experiment}：{len(records)} 局")
    report = run_checks(records, out_dir)
    s = report["summary"]
    print(f"  通过 {s['passed']} / 失败 {s['failed']} / 违规 {s['total_violations']}"
          f"（阻断 {s['blocking_violations']}，参考 {s['advisory_violations']}）")
    for rule, n in sorted(s["by_rule"].items(), key=lambda kv: -kv[1])[:10]:
        mark = "（参考）" if rule in s["advisory_rules"] else ""
        print(f"    {rule}: {n} {mark}")

    failed = 1 if (s["total_violations"] if args.strict else s["blocking_violations"]) else 0
    sections = [("不变量", kv_block([
        ("对局数", s["matches"]),
        ("通过", s["passed"]),
        ("违规总数", s["total_violations"]),
        ("其中阻断", s["blocking_violations"]),
        ("按规则", s["by_rule"] or "无"),
    ]))]

    if args.baseline:
        try:
            base = store.load_matches(args.baseline, with_events=False)
        except KeyError as e:
            print(f"[qa] {e}")
            return 2
        result = compare([r.meta for r in records], [r.meta for r in base])
        print(f"[qa] 与基线 {args.baseline} 对拍：")
        print(format_console(result))
        with (out_dir / "equivalence.json").open("w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
        e = result["summary"]
        sections.append(("等价性", kv_block([
            ("对比局数", e["compared"]), ("一致", e["identical"]), ("不同", e["differing"]),
        ])))
        failed += 1 if e["differing"] else 0

    md = write_markdown(out_dir / "qa_report.md", f"健康检查 · {args.experiment}", sections)
    print(f"[qa] 报告：{md}")
    print("[qa] 结果：通过" if failed == 0 else "[qa] 结果：未通过")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
