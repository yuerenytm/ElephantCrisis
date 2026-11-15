#!/usr/bin/env python3
"""健康检查入口：不变量断言 + 基线等价性。

用法：
    # 跑一批新数据（会自动调用 dotnet 跑批器）
    python Tools/qa/run.py --name check_v1 --matches 100 --seed 50000

    # 只检查已有数据
    python Tools/qa/run.py --data Tools/sim/output/check_v1

    # 与基线逐局对拍（证明重构没改行为）
    python Tools/qa/run.py --data Tools/sim/output/after --baseline Tools/sim/output/before

退出码：0 = 全部通过；1 = 有不变量违规或对拍差异。
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

from qa.equivalence import compare, format_console  # noqa: E402
from qa.runner import run_baseline  # noqa: E402


def main() -> int:
    p = argparse.ArgumentParser(description="健康检查：不变量 + 等价性")
    p.add_argument("--name", default=None, help="实验名（会先跑一批新数据）")
    p.add_argument("--matches", type=int, default=100)
    p.add_argument("--seed", type=int, default=50000)
    p.add_argument("--collect", default="both", choices=["balance", "logic", "both"])
    p.add_argument("--data", default=None, help="要检查的数据目录")
    p.add_argument("--baseline", default=None, help="基线数据目录（给了就做等价性对拍）")
    p.add_argument("--out", default=None, help="报告输出目录（默认写到数据目录下 reports/）")
    p.add_argument("--strict", action="store_true", help="把参考项也计入失败")
    args = p.parse_args()

    # 1. 需要的话先跑一批
    data_dir = Path(args.data) if args.data else None
    if args.name:
        exp = Experiment(name=args.name, matches=args.matches, seed=args.seed, collect=args.collect)
        code = exp.run()
        if code != 0:
            print(f"[qa] 跑批失败，退出码 {code}")
            return code
        data_dir = exp.dir
    if data_dir is None:
        data_dir = DEFAULT_OUTPUT
    if not data_dir.exists():
        print(f"[qa] 数据目录不存在：{data_dir}")
        return 2

    out_dir = Path(args.out) if args.out else (data_dir / "reports")
    failed = 0

    # 2. 不变量
    print(f"[qa] 检查不变量：{data_dir}")
    report = run_baseline(data_dir, out_dir)
    s = report["summary"]
    print(f"  对局 {s['matches']} / 通过 {s['passed']} / 失败 {s['failed']} / "
          f"违规 {s['total_violations']}（其中阻断 {s['blocking_violations']}，参考 {s['advisory_violations']}）")
    if s["by_rule"]:
        for rule, n in sorted(s["by_rule"].items(), key=lambda kv: -kv[1]):
            print(f"    {rule}: {n}")
    blocking = s["total_violations"] if args.strict else s["blocking_violations"]
    failed += 1 if blocking else 0

    # 3. 等价性
    if args.baseline:
        base_dir = Path(args.baseline)
        print(f"[qa] 与基线对拍：{data_dir} vs {base_dir}")
        result = compare(data_dir, base_dir)
        print(format_console(result))
        with (out_dir / "equivalence.json").open("w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
        failed += 1 if result["summary"]["differing"] else 0

    # 4. Markdown 报告
    sections = [
        ("不变量", kv_block([
            ("对局数", s["matches"]),
            ("通过", s["passed"]),
            ("违规总数", s["total_violations"]),
            ("按规则", s["by_rule"] or "无"),
        ])),
    ]
    if args.baseline:
        e = result["summary"]
        sections.append(("等价性", kv_block([
            ("对比局数", e["compared"]),
            ("一致", e["identical"]),
            ("不同", e["differing"]),
        ])))
    md = write_markdown(out_dir / "qa_report.md", f"健康检查 · {data_dir.name}", sections)
    print(f"[qa] 报告：{md}")

    print("[qa] 结果：通过" if failed == 0 else "[qa] 结果：未通过")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
