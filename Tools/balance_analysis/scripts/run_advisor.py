#!/usr/bin/env python3
"""预处理平衡报告 + 规则 →（可选）调用 LLM 给出数值建议。"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TOOLS = ROOT.parent
REPO = TOOLS.parent
SIM = TOOLS / "sim"
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from llm_client import chat_completion, load_advisor_config  # noqa: E402
from preprocess import (  # noqa: E402
    build_context,
    context_to_prompt,
    load_json,
    write_context_artifacts,
)


def main() -> int:
    p = argparse.ArgumentParser(description="数值平衡顾问（预处理 + LLM）")
    p.add_argument(
        "--balance-report",
        type=str,
        default=str(ROOT / "reports" / "balance_report.json"),
        help="balance_analysis 产出的 balance_report.json",
    )
    p.add_argument("--out", type=str, default=str(ROOT / "reports"))
    p.add_argument("--config", type=str, default=str(ROOT / "config" / "advisor.yaml"))
    p.add_argument("--rules", type=str, default=None, help="game_rules.yaml 路径")
    p.add_argument("--role-doc", type=str, default=None)
    p.add_argument("--overview-doc", type=str, default=None)
    p.add_argument("--dry-run", action="store_true", help="只写预处理上下文与 prompt，不调 API")
    p.add_argument("--matches", type=int, default=0, help=">0 时先调用 sim run_balance 生成报告")
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--sim-out", type=str, default=str(SIM / "output"))
    args = p.parse_args()

    out_dir = Path(args.out)
    if not out_dir.is_absolute():
        out_dir = (ROOT / out_dir).resolve()
    cfg = load_advisor_config(Path(args.config) if args.config else None)

    report_path = Path(args.balance_report)
    if not report_path.is_absolute():
        report_path = (Path.cwd() / report_path).resolve()

    if args.matches and args.matches > 0:
        print(f"[0] run sim balance: matches={args.matches} (Unity LogicSim)")
        sim_out = Path(args.sim_out)
        if not sim_out.is_absolute():
            sim_out = (Path.cwd() / sim_out).resolve()
        cmd = [
            sys.executable,
            str(ROOT / "scripts" / "run_balance.py"),
            "--matches",
            str(args.matches),
            "--seed",
            str(args.seed),
            "--input",
            str(sim_out),
            "--out",
            str(report_path.parent),
        ]
        rc = subprocess.call(cmd, cwd=str(ROOT))
        if rc != 0:
            print("run_balance failed", file=sys.stderr)
            return rc
        report_path = report_path.parent / "balance_report.json"

    if not report_path.is_file():
        print(
            f"找不到平衡报告: {report_path}\n"
            "请先: cd Tools/balance_analysis && python scripts/run_balance.py --matches 100",
            file=sys.stderr,
        )
        return 1

    report = load_json(report_path)
    rules = Path(args.rules) if args.rules else None
    role_doc = Path(args.role_doc) if args.role_doc else None
    overview_doc = Path(args.overview_doc) if args.overview_doc else None
    context = build_context(
        report,
        rules_path=rules,
        role_doc_path=role_doc,
        overview_doc_path=overview_doc,
        cfg=cfg,
    )
    prompt = context_to_prompt(context)
    paths = write_context_artifacts(context, prompt, out_dir)
    print(f"Wrote {paths['context']}")
    print(f"Wrote {paths['prompt']}")
    print(
        f"Context size ~{paths['prompt'].stat().st_size // 1024} KB "
        f"(preprocessed; raw match logs NOT included)"
    )

    if args.dry_run:
        print("dry-run: skip API call")
        return 0

    print("[api] requesting suggestions...")
    text = chat_completion(prompt, cfg=cfg)
    md_path = out_dir / "advisor_suggestions.md"
    md_path.write_text(text, encoding="utf-8")
    meta_path = out_dir / "advisor_suggestions.json"
    with meta_path.open("w", encoding="utf-8") as f:
        json.dump(
            {
                "balance_report": str(report_path),
                "suggestions_path": str(md_path),
                "model_note": "see env BALANCE_ADVISOR_MODEL / config/advisor.yaml",
            },
            f,
            ensure_ascii=False,
            indent=2,
        )
    print(f"Wrote {md_path}")
    print("--- suggestions preview ---")
    print(text[:1200] + ("…" if len(text) > 1200 else ""))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
