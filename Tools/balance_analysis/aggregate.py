from __future__ import annotations

import json
import math
from collections import Counter
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence

from elephant_sim.log_io import iter_matches
from elephant_sim.models import ROLE_ORDER

from extract import ROLES, extract_match


def _percentile(sorted_vals: Sequence[float], p: float) -> Optional[float]:
    if not sorted_vals:
        return None
    if len(sorted_vals) == 1:
        return float(sorted_vals[0])
    # nearest-rank style, p in [0, 100]
    k = (len(sorted_vals) - 1) * (p / 100.0)
    f = math.floor(k)
    c = math.ceil(k)
    if f == c:
        return float(sorted_vals[int(k)])
    d0 = sorted_vals[int(f)] * (c - k)
    d1 = sorted_vals[int(c)] * (k - f)
    return float(d0 + d1)


def _survival_stats(values: List[int]) -> Dict[str, Any]:
    if not values:
        return {
            "mean": None,
            "p25": None,
            "median": None,
            "p75": None,
            "min": None,
            "max": None,
            "n": 0,
        }
    s = sorted(values)
    return {
        "mean": round(sum(s) / len(s), 3),
        "p25": round(_percentile(s, 25) or 0.0, 3),
        "median": round(_percentile(s, 50) or 0.0, 3),
        "p75": round(_percentile(s, 75) or 0.0, 3),
        "min": int(s[0]),
        "max": int(s[-1]),
        "n": len(s),
    }


def aggregate_records(records: Iterable[Dict[str, Any]]) -> Dict[str, Any]:
    rows = list(records)
    n = len(rows)
    reasons = Counter(r.get("reason") or "unknown" for r in rows)
    lengths = [int(r.get("full_rounds") or 0) for r in rows]
    strategy_counts: Counter = Counter()
    for r in rows:
        strat = r.get("strategies") or {}
        # 整局策略签名，便于声明「混合/强制」
        sig = ",".join(f"{role}:{strat.get(role, '?')}" for role in ROLES)
        strategy_counts[sig] += 1
        for role in ROLES:
            strategy_counts[f"role:{role}:{strat.get(role, '?')}"] += 1

    by_role: Dict[str, Any] = {}
    for role in ROLES:
        wins = 0
        deaths = 0
        survived_end = 0
        survivals: List[int] = []
        for r in rows:
            pr = (r.get("per_role") or {}).get(role) or {}
            if pr.get("won"):
                wins += 1
            if pr.get("died"):
                deaths += 1
            if pr.get("survived_to_end"):
                survived_end += 1
            if "survival_rounds" in pr:
                survivals.append(int(pr["survival_rounds"]))
        by_role[role] = {
            "matches": n,
            "wins": wins,
            "win_rate": round(wins / n, 4) if n else None,
            "deaths": deaths,
            "death_rate": round(deaths / n, 4) if n else None,
            "survived_to_end": survived_end,
            "survive_to_end_rate": round(survived_end / n, 4) if n else None,
            "survival_rounds": _survival_stats(survivals),
        }

    role_strat = {role: Counter() for role in ROLES}
    for r in rows:
        strat = r.get("strategies") or {}
        for role in ROLES:
            role_strat[role][strat.get(role) or "unknown"] += 1

    return {
        "summary": {
            "matches": n,
            "avg_full_rounds": round(sum(lengths) / n, 3) if n else None,
            "full_rounds": _survival_stats(lengths),
            "end_reasons": dict(reasons),
            "roles": [r.value for r in ROLE_ORDER],
            "disclaimer": (
                "指标基于当前仿真 AI 与种子集合，反映该策略分布下的相对强弱，"
                "不等于真人平衡结论。"
            ),
        },
        "by_role": by_role,
        "strategy_usage": {
            role: dict(cnt) for role, cnt in role_strat.items()
        },
        "match_strategy_signatures_top": dict(strategy_counts.most_common(12)),
    }


def aggregate_matches(input_dir: Path) -> Dict[str, Any]:
    records = [extract_match(m) for m in iter_matches(input_dir)]
    report = aggregate_records(records)
    report["input_dir"] = str(input_dir.resolve())
    return report


def write_report(report: Dict[str, Any], out_dir: Path) -> Path:
    out_dir.mkdir(parents=True, exist_ok=True)
    path = out_dir / "balance_report.json"
    with path.open("w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    return path


def format_console_table(report: Dict[str, Any]) -> str:
    summary = report.get("summary") or {}
    lines = [
        f"matches={summary.get('matches')}  avg_full_rounds={summary.get('avg_full_rounds')}",
        f"end_reasons={summary.get('end_reasons')}",
        "",
        f"{'role':<10} {'win%':>7} {'death%':>8} {'surv_mean':>10} {'p25':>6} {'med':>6} {'p75':>6}",
        "-" * 60,
    ]
    for role in ROLES:
        row = (report.get("by_role") or {}).get(role) or {}
        surv = row.get("survival_rounds") or {}
        wr = row.get("win_rate")
        dr = row.get("death_rate")
        lines.append(
            f"{role:<10} "
            f"{(wr * 100 if wr is not None else 0):>6.1f}% "
            f"{(dr * 100 if dr is not None else 0):>7.1f}% "
            f"{(surv.get('mean') if surv.get('mean') is not None else 0):>10.2f} "
            f"{(surv.get('p25') if surv.get('p25') is not None else 0):>6.1f} "
            f"{(surv.get('median') if surv.get('median') is not None else 0):>6.1f} "
            f"{(surv.get('p75') if surv.get('p75') is not None else 0):>6.1f}"
        )
    lines.append("")
    lines.append(str(summary.get("disclaimer") or ""))
    return "\n".join(lines)
