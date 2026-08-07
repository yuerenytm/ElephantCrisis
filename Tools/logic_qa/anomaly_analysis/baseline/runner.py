from __future__ import annotations

import json
from collections import Counter
from pathlib import Path
from typing import Any, Dict, List, Optional

from elephant_sim.log_io import iter_match_dirs, load_match
from .checks import Violation, check_match_events


def run_baseline(input_dir: Path, out_dir: Path) -> Dict[str, Any]:
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

    summary = {
        "matches": len(matches),
        "passed": sum(1 for m in matches if m["pass"]),
        "failed": sum(1 for m in matches if not m["pass"]),
        "total_violations": total_violations,
        "by_rule": dict(rule_counts),
        "quarantine": quarantine,
    }
    report = {"summary": summary, "matches": matches}
    report_path = out_dir / "baseline_report.json"
    with report_path.open("w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)

    # 仅写路径列表，避免复制大日志
    with (out_dir / "baseline_quarantine.txt").open("w", encoding="utf-8") as f:
        for p in quarantine:
            f.write(p + "\n")

    return report


def match_has_baseline_bug(report: Dict[str, Any], match_id: str) -> bool:
    for m in report.get("matches") or []:
        if m.get("match_id") == match_id:
            return not m.get("pass", True)
    return False


def load_baseline_report(path: Path) -> Optional[Dict[str, Any]]:
    if not path.is_file():
        return None
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)
