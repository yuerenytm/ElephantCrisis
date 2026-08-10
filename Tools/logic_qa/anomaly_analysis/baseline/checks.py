from __future__ import annotations

from dataclasses import asdict, dataclass, field
from typing import Any, Dict, List, Optional, Set


ROLES = ("elephant", "human", "monkey", "cat")


@dataclass
class Violation:
    rule_id: str
    severity: str
    t: int
    message: str
    context: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


def check_match_events(events: List[Dict[str, Any]], match_id: str = "") -> List[Violation]:
    """对单局事件流执行全部基线规则。"""
    viols: List[Violation] = []
    viols.extend(_check_damage_rules(events))
    viols.extend(_check_hp_bounds(events))
    viols.extend(_check_rain_clears_burn(events))
    viols.extend(_check_dying_no_draw(events))
    viols.extend(_check_dead_no_action(events))
    viols.extend(_check_bag_over_cap_end(events))
    viols.extend(_check_lava_shrink_monotonic(events))
    viols.extend(_check_hidden_break_on_attack(events))
    viols.extend(_check_dmg_hp_consistent(events))
    viols.extend(_check_no_turn_start_draw(events))
    for v in viols:
        v.context.setdefault("match_id", match_id)
    return viols


def _unit_map(snapshot: Dict[str, Any]) -> Dict[str, Dict[str, Any]]:
    out: Dict[str, Dict[str, Any]] = {}
    for u in snapshot.get("units") or []:
        role = u.get("role")
        if role:
            out[role] = u
    return out


def _statuses(u: Dict[str, Any]) -> Set[str]:
    return set(u.get("statuses") or [])


def _check_damage_rules(events: List[Dict[str, Any]]) -> List[Violation]:
    viols: List[Violation] = []
    for e in events:
        if e.get("type") != "damage":
            continue
        t = int(e.get("t", 0))
        kind = e.get("kind")
        raw = e.get("raw")
        dealt = e.get("dealt")
        if raw is None or dealt is None:
            continue
        try:
            raw_i = int(raw)
            dealt_i = int(dealt)
        except (TypeError, ValueError):
            continue

        if kind == "true" and dealt_i != raw_i and dealt_i != 0:
            # 真伤不可减防/护盾；仅允许全额（dealt==raw）或护身符等完全免伤（dealt==0）
            viols.append(
                Violation(
                    "dmg_true_no_mitigation",
                    "critical",
                    t,
                    f"真伤 dealt({dealt_i}) 既非 raw({raw_i}) 也非 0（部分减伤非法）",
                    {"target": e.get("target"), "actor": e.get("actor"), "via": e.get("via")},
                )
            )
        if kind == "physical":
            if dealt_i < 0 or dealt_i > raw_i:
                viols.append(
                    Violation(
                        "dmg_physical_bound",
                        "critical",
                        t,
                        f"物伤 dealt({dealt_i}) 不在 [0, raw={raw_i}]",
                        {"target": e.get("target"), "actor": e.get("actor")},
                    )
                )
    return viols


def _check_hp_bounds(events: List[Dict[str, Any]]) -> List[Violation]:
    viols: List[Violation] = []
    for e in events:
        if e.get("type") != "snapshot":
            continue
        t = int(e.get("t", 0))
        for u in e.get("units") or []:
            if u.get("dead"):
                continue
            role = u.get("role")
            hp = u.get("hp")
            max_hp = u.get("max_hp")
            if hp is None or max_hp is None:
                continue
            statuses = _statuses(u)
            if "dying" in statuses and int(hp) != 0:
                viols.append(
                    Violation(
                        "hp_bounds",
                        "critical",
                        t,
                        f"{role} 濒死但 hp={hp}",
                        {"role": role, "hp": hp},
                    )
                )
            if int(hp) < 0 or int(hp) > int(max_hp):
                viols.append(
                    Violation(
                        "hp_bounds",
                        "critical",
                        t,
                        f"{role} hp={hp} 超出 [0, {max_hp}]",
                        {"role": role, "hp": hp, "max_hp": max_hp},
                    )
                )
    return viols


def _check_rain_clears_burn(events: List[Dict[str, Any]]) -> List[Violation]:
    viols: List[Violation] = []
    for e in events:
        if e.get("type") != "snapshot":
            continue
        if e.get("weather") != "rain":
            continue
        t = int(e.get("t", 0))
        for u in e.get("units") or []:
            if u.get("dead"):
                continue
            if "burning" in _statuses(u):
                viols.append(
                    Violation(
                        "rain_clears_burn",
                        "critical",
                        t,
                        f"雨天仍有 burning: {u.get('role')}",
                        {"role": u.get("role")},
                    )
                )
    return viols


def _check_dying_no_draw(events: List[Dict[str, Any]]) -> List[Violation]:
    """
    规则真源：濒死不可抽牌 / 不可使用卡牌（脚下血瓶自救除外，记为 heal 而非 draw）。
    注意：濒死仍可移动（移速=1）与近战，旧断言 dying_no_move 已废弃。
    heal 且 hp_after>=1 视为解除濒死，须移出濒死集合，否则会误报治疗后的 equip 等。
    """
    viols: List[Violation] = []
    dying: Set[str] = set()
    for e in events:
        typ = e.get("type")
        t = int(e.get("t", 0))
        if typ == "snapshot":
            dying = set()
            for u in e.get("units") or []:
                if u.get("dead"):
                    continue
                if "dying" in _statuses(u):
                    dying.add(u["role"])
            continue
        if typ == "dying":
            actor = e.get("actor")
            if actor:
                dying.add(actor)
            continue
        if typ == "death":
            actor = e.get("actor")
            if actor in dying:
                dying.discard(actor)
            continue
        if typ == "heal":
            actor = e.get("actor")
            if actor in dying:
                try:
                    hp_after = int(e.get("hp_after", 0))
                except (TypeError, ValueError):
                    hp_after = 0
                # 脚下血瓶自救：治疗本身合法；血量回升则解除濒死
                if hp_after >= 1:
                    dying.discard(actor)
            continue
        actor = e.get("actor")
        if actor not in dying:
            continue
        if typ == "draw":
            viols.append(
                Violation(
                    "dying_no_draw",
                    "critical",
                    t,
                    f"濒死单位仍抽牌: {actor}",
                    {"actor": actor, "item": e.get("item")},
                )
            )
        if typ in ("equip", "bomb", "discard") or (
            typ == "damage" and e.get("via") in ("shoot", "bomb")
        ):
            viols.append(
                Violation(
                    "dying_no_draw",
                    "critical",
                    t,
                    f"濒死单位仍使用卡牌类行动: {actor} type={typ}",
                    {"actor": actor, "type": typ, "via": e.get("via")},
                )
            )
    return viols


def _check_dead_no_action(events: List[Dict[str, Any]]) -> List[Violation]:
    viols: List[Violation] = []
    dead: Set[str] = set()
    for e in events:
        typ = e.get("type")
        t = int(e.get("t", 0))
        if typ == "death":
            actor = e.get("actor")
            if actor:
                dead.add(actor)
            continue
        if typ == "snapshot":
            for u in e.get("units") or []:
                if u.get("dead") or "dead" in _statuses(u):
                    dead.add(u["role"])
            continue
        actor = e.get("actor")
        if actor not in dead:
            continue
        if typ in ("move", "draw") or (typ == "damage" and actor in ROLES):
            viols.append(
                Violation(
                    "dead_no_action",
                    "critical",
                    t,
                    f"死亡后仍有动作: {actor} type={typ}",
                    {"actor": actor, "type": typ},
                )
            )
    return viols


def _check_bag_over_cap_end(events: List[Dict[str, Any]]) -> List[Violation]:
    """某回合出现 overweight 后，该回合 turn_end 后的 snapshot 应 weight<=cap。"""
    viols: List[Violation] = []
    pending_actors: Set[str] = set()
    current_actor: Optional[str] = None

    for e in events:
        typ = e.get("type")
        t = int(e.get("t", 0))
        if typ == "turn_start":
            current_actor = e.get("actor")
            continue
        if typ == "overweight" and e.get("actor"):
            pending_actors.add(e["actor"])
            continue
        if typ == "turn_end":
            # 下一 snapshot 再验
            continue
        if typ == "snapshot" and pending_actors:
            units = _unit_map(e)
            for role in list(pending_actors):
                u = units.get(role)
                if u is None or u.get("dead"):
                    pending_actors.discard(role)
                    continue
                weight = float(u.get("weight", 0))
                cap = float(u.get("cap", 0))
                if weight > cap + 0.001:
                    viols.append(
                        Violation(
                            "bag_over_cap_end",
                            "major",
                            t,
                            f"{role} 回合末仍超重 weight={weight} > cap={cap}",
                            {"role": role, "weight": weight, "cap": cap},
                        )
                    )
                pending_actors.discard(role)
    return viols


def _check_lava_shrink_monotonic(events: List[Dict[str, Any]]) -> List[Violation]:
    viols: List[Violation] = []
    last_inset = 0
    for e in events:
        if e.get("type") == "lava_shrink":
            inset = int(e.get("inset", 0))
            t = int(e.get("t", 0))
            if inset < last_inset:
                viols.append(
                    Violation(
                        "lava_shrink_monotonic",
                        "critical",
                        t,
                        f"lava_inset 回退 {last_inset} -> {inset}",
                        {"prev": last_inset, "inset": inset},
                    )
                )
            last_inset = inset
        elif e.get("type") == "snapshot":
            inset = int(e.get("lava_inset", last_inset))
            t = int(e.get("t", 0))
            if inset < last_inset:
                viols.append(
                    Violation(
                        "lava_shrink_monotonic",
                        "critical",
                        t,
                        f"snapshot lava_inset 回退 {last_inset} -> {inset}",
                        {"prev": last_inset, "inset": inset},
                    )
                )
            last_inset = max(last_inset, inset)
    return viols


def _check_hidden_break_on_attack(events: List[Dict[str, Any]]) -> List[Violation]:
    """
    简化可观测规则：若某角色主动伤害（melee / melee_pierce / bow / bomb 等）
    且攻击前最近 snapshot 该角色有 hidden，则中间须有 status break_attack。
    无来源真伤（actor=none，如诅咒之刃）不检此项。
    """
    viols: List[Violation] = []
    last_hidden: Dict[str, bool] = {r: False for r in ROLES}
    # 记录自上次 snapshot 以来的 break_attack
    broke_since_snap: Set[str] = set()
    attack_vias = {None, "melee", "melee_pierce", "bow", "bomb", "shoot"}

    for e in events:
        typ = e.get("type")
        t = int(e.get("t", 0))
        if typ == "snapshot":
            broke_since_snap.clear()
            for u in e.get("units") or []:
                role = u.get("role")
                if not role:
                    continue
                last_hidden[role] = (not u.get("dead")) and ("hidden" in _statuses(u))
            continue
        if typ == "status" and e.get("status") == "hidden" and e.get("op") in ("break_attack",):
            actor = e.get("actor")
            if actor:
                broke_since_snap.add(actor)
                last_hidden[actor] = False
            continue
        if typ != "damage":
            continue
        actor = e.get("actor")
        if actor not in ROLES:
            continue
        via = e.get("via")
        if via not in attack_vias:
            continue
        if last_hidden.get(actor) and actor not in broke_since_snap:
            viols.append(
                Violation(
                    "hidden_break_on_attack",
                    "major",
                    t,
                    f"{actor} 隐匿状态下攻击但未见 break_attack",
                    {"actor": actor, "via": via},
                )
            )
            last_hidden[actor] = False
    return viols


def _check_no_turn_start_draw(events: List[Dict[str, Any]]) -> List[Violation]:
    """
    现行规则：取消行动开始摸牌。旧引擎顺序为 DrawFor → EmitTurnStart，
    故「同一 actor 的 draw 紧挨在 turn_start 之前」视为回归违规。
    领袖宣言 / 技能摸牌发生在 turn_start 之后，不受本条约束。
    """
    viols: List[Violation] = []
    prev_meaningful: Optional[Dict[str, Any]] = None
    for e in events:
        typ = e.get("type")
        if typ in (None, "snapshot", "clock", "weather"):
            continue
        if typ == "turn_start" and prev_meaningful is not None:
            if (
                prev_meaningful.get("type") == "draw"
                and prev_meaningful.get("actor")
                and prev_meaningful.get("actor") == e.get("actor")
            ):
                t = int(e.get("t", 0))
                actor = e.get("actor")
                viols.append(
                    Violation(
                        "no_turn_start_draw",
                        "critical",
                        t,
                        f"疑似行动开始摸牌回归: {actor} 在 turn_start 前有 draw",
                        {"actor": actor, "item": prev_meaningful.get("item")},
                    )
                )
        prev_meaningful = e
    return viols


def _check_dmg_hp_consistent(events: List[Dict[str, Any]]) -> List[Violation]:
    """
    在两次 snapshot 之间，对每个角色累计 damage.dealt / heal.healed，
    与 snapshot hp 变化对照。濒死/死亡跳变放宽：若中间有 dying/death 则跳过该角色。
    """
    viols: List[Violation] = []
    prev_snap: Optional[Dict[str, Any]] = None
    pending_dmg: Dict[str, int] = {r: 0 for r in ROLES}
    pending_heal: Dict[str, int] = {r: 0 for r in ROLES}
    skip_roles: Set[str] = set()

    def flush(curr: Dict[str, Any]) -> None:
        nonlocal prev_snap, pending_dmg, pending_heal, skip_roles
        if prev_snap is None:
            prev_snap = curr
            pending_dmg = {r: 0 for r in ROLES}
            pending_heal = {r: 0 for r in ROLES}
            skip_roles = set()
            return
        prev_u = _unit_map(prev_snap)
        curr_u = _unit_map(curr)
        t = int(curr.get("t", 0))
        for role in ROLES:
            if role in skip_roles:
                continue
            pu, cu = prev_u.get(role), curr_u.get(role)
            if pu is None or cu is None:
                continue
            if pu.get("dead") or cu.get("dead"):
                continue
            if "dying" in _statuses(pu) or "dying" in _statuses(cu):
                continue
            try:
                php = int(pu["hp"])
                chp = int(cu["hp"])
            except (KeyError, TypeError, ValueError):
                continue
            expected = php - pending_dmg.get(role, 0) + pending_heal.get(role, 0)
            # 夹在 [0, max_hp]
            max_hp = int(cu.get("max_hp", cu.get("hp", expected)))
            expected_clamped = max(0, min(max_hp, expected))
            # 若 expected<=0 可能进入濒死，上面已跳过 dying；此处仅校验存活路径
            if chp != expected_clamped and chp != expected:
                # 允许护盾完全抵消等导致 dealt=0 已计入；若不一致则报
                if pending_dmg.get(role, 0) or pending_heal.get(role, 0):
                    viols.append(
                        Violation(
                            "dmg_hp_consistent",
                            "major",
                            t,
                            f"{role} hp 与区间伤害/治疗不一致: {php} -{pending_dmg.get(role,0)} +{pending_heal.get(role,0)} => expect~{expected_clamped}, got {chp}",
                            {
                                "role": role,
                                "prev_hp": php,
                                "hp": chp,
                                "dmg": pending_dmg.get(role, 0),
                                "heal": pending_heal.get(role, 0),
                            },
                        )
                    )
        prev_snap = curr
        pending_dmg = {r: 0 for r in ROLES}
        pending_heal = {r: 0 for r in ROLES}
        skip_roles = set()

    for e in events:
        typ = e.get("type")
        if typ == "snapshot":
            flush(e)
            continue
        if typ == "damage":
            target = e.get("target")
            if target in pending_dmg:
                try:
                    pending_dmg[target] += int(e.get("dealt", 0))
                except (TypeError, ValueError):
                    pass
            continue
        if typ == "heal":
            actor = e.get("actor")
            if actor in pending_heal:
                try:
                    pending_heal[actor] += int(e.get("healed", 0))
                except (TypeError, ValueError):
                    pass
            continue
        if typ in ("dying", "death"):
            actor = e.get("actor")
            if actor:
                skip_roles.add(actor)
    return viols
