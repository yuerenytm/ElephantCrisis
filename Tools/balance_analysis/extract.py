from __future__ import annotations

from typing import Any, Dict

from elephant_sim.log_io import MatchLog
from elephant_sim.models import ROLE_ORDER

ROLES = [r.value for r in ROLE_ORDER]


def extract_match(match: MatchLog) -> Dict[str, Any]:
    """从单局 meta.json 提取角色级第一优先级指标。

    balance 只读 sim 写好的宏观结果（per_role 死亡回合），不读 events.jsonl。
    """
    meta = match.meta or {}
    winner = meta.get("winner")
    reason = meta.get("reason")
    full_rounds = int(meta.get("full_rounds") or 0)
    strategies = meta.get("strategies") or {}

    per_role: Dict[str, Any] = {}
    per_role_meta = meta.get("per_role") or {}
    for role in ROLES:
        pr = per_role_meta.get(role) or {}
        survived = bool(pr.get("survived", True))
        death_round = pr.get("death_round")
        per_role[role] = {
            "won": winner == role,
            "died": not survived,
            "survival_rounds": full_rounds if death_round is None else int(death_round),
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
