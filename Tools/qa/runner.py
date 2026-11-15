from __future__ import annotations

import json
from collections import Counter
from pathlib import Path
from typing import Any, Dict, List, Optional

from lab.events import iter_match_dirs, load_match

from .checks import Violation, check_match_events

# 参考项（advisory）：这些规则依赖快照细节，无法做到零误报，因此不参与门禁。
# - dmg_magic_formula：用快照时刻的法抗去核算受击时刻的伤害，
#   天气/地形改变法抗后必然对不上；引擎公式本身（floor(raw*(1-mr))）与规则一致。
# - quasi_crossbow_requires_equipped：弩可能在使用后被移除，快照看不到中间态。
# 需要严格模式（--strict）时才计入失败。
ADVISORY_RULES = {
    "dmg_magic_formula",
    "quasi_crossbow_requires_equipped",
}


def run_baseline(input_dir: Path, out_dir: Path) -> Dict[str, Any]:
    """对目录下所有对局执行基线规则（不变量断言）。

    产出：
      baseline_report.json      每局违规明细 + summary（by_rule 计数）
      baseline_quarantine.txt   仅违规对局路径（不复制大日志）

    规则本身在 checks.py 里，本文件只负责"逐局跑一遍 + 汇总"。
    """
    out_dir.mkdir(parents=True, exist_ok=True)
    quarantine: List[str] = []
    matches: List[Dict[str, Any]] = []
    rule_counts: Counter = Counter()
    total_violations = 0

    for match_dir in iter_match_dirs(input_dir):
        log = load_match(match_dir)
        viols = check_match_events(log.events, match_id=log.match_id)
        total_violations += len(viols)
        for v in viols:
            rule_counts[v.rule_id] += 1
        entry = {
            "match_id": log.match_id,
            "path": str(match_dir),
            "meta": log.meta,
            "violation_count": len(viols),
            "violations": [v.to_dict() for v in viols],
            "pass": len(viols) == 0,
        }
        matches.append(entry)
        if viols:
            quarantine.append(str(match_dir))

    advisory = sum(n for r, n in rule_counts.items() if r in ADVISORY_RULES)
    blocking = total_violations - advisory
    summary = {
        "matches": len(matches),
        "passed": sum(1 for m in matches if m["pass"]),
        "failed": sum(1 for m in matches if not m["pass"]),
        "total_violations": total_violations,
        "advisory_violations": advisory,
        "blocking_violations": blocking,
        "by_rule": dict(rule_counts),
        "advisory_rules": sorted(ADVISORY_RULES),
        "quarantine": quarantine,
    }
    report = {"summary": summary, "matches": matches}

    with (out_dir / "baseline_report.json").open("w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)

    # 仅写路径列表，避免复制大日志
    with (out_dir / "baseline_quarantine.txt").open("w", encoding="utf-8") as f:
        for p in quarantine:
            f.write(p + "\n")

    return report


def match_has_baseline_bug(report: Dict[str, Any], match_id: str) -> bool:
    """查询某局在基线报告里是否有违规（报告缺该局时视为通过）。"""
    for m in report.get("matches") or []:
        if m.get("match_id") == match_id:
            return not m.get("pass", True)
    return False


def load_baseline_report(path: Path) -> Optional[Dict[str, Any]]:
    """读取基线报告；文件不存在返回 None。"""
    if not path.is_file():
        return None
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)
