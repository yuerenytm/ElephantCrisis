from __future__ import annotations

from typing import Any, Dict, List, Optional

from elephant_sim.log_io import MatchLog
from elephant_sim.models import ROLE_ORDER

ROLES = [r.value for r in ROLE_ORDER]


def extract_match(match: MatchLog) -> Dict[str, Any]:
    """从单局日志提取角色级第一优先级指标。"""
    meta = match.meta or {}
    winner = meta.get("winner")
    reason = meta.get("reason")
    full_rounds = int(meta.get("full_rounds") or 0)
    strategies = meta.get("strategies") or {}

    # 扫描过程中的「已完成完整回合数」；开局为 0
    current_full_round = 0
    death_round: Dict[str, int] = {}
    died: Dict[str, bool] = {r: False for r in ROLES}

    for e in match.events:
        typ = e.get("type")
        if typ == "clock":
            current_full_round = int(e.get("full_round") or current_full_round)
        elif typ == "turn_start":
            # turn_start.round 为进行中的回合号（从 1 起），存活用「已完成」更贴近 full_rounds
            r = int(e.get("round") or 0)
            if r > 0:
                current_full_round = max(current_full_round, r - 1)
        elif typ == "death":
            actor = e.get("actor")
            if actor in died and not died[actor]:
                died[actor] = True
                death_round[actor] = current_full_round
        elif typ == "match_end":
            if reason is None:
                reason = e.get("reason")
            if winner is None:
                winner = e.get("winner")

    if full_rounds <= 0:
        full_rounds = current_full_round

    per_role: Dict[str, Any] = {}
    for role in ROLES:
        survived = not died[role]
        survival = full_rounds if survived else int(death_round.get(role, full_rounds))
        per_role[role] = {
            "won": winner == role,
            "died": died[role],
            "survival_rounds": survival,
            "survived_to_end": survived,
            "strategy": strategies.get(role),
        }

    return {
        "match_id": match.match_id,
        "path": str(match.path),
        "seed": meta.get("seed"),
        "winner": winner,
        "reason": reason or "unknown",
        "full_rounds": full_rounds,
        "strategies": strategies,
        "per_role": per_role,
    }


def extract_from_events(
    events: List[Dict[str, Any]],
    meta: Optional[Dict[str, Any]] = None,
    match_id: str = "match",
) -> Dict[str, Any]:
    """测试用：不依赖磁盘。"""
    from pathlib import Path

    log = MatchLog(match_id=match_id, path=Path("."), events=events, meta=meta or {})
    return extract_match(log)
