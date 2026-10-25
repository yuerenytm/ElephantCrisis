from __future__ import annotations

from typing import Any, Dict, List, Optional, Sequence, Tuple

import numpy as np

ROLES = ("elephant", "human", "monkey", "cat")
WEATHERS = ("clear", "rain", "fog")
TILES = ("normal", "sand", "swamp", "ice", "jungle", "highland", "lava")
STATUS_FLAGS = ("dying", "dead", "burning", "poison", "stun", "hidden", "trip")


def _build_feature_names(use_hp_delta: bool) -> List[str]:
    """生成特征名列表，顺序必须与向量拼装顺序一致。

    Args:
        use_hp_delta: 是否追加每帧 HP 变化量（d_hp）特征。

    Returns:
        长度等于单帧特征维度的名字列表（默认 87）。

    """
    names = [
        "round",
        "hour",
        *[f"weather_{w}" for w in WEATHERS],
        "lava_inset",
        "deck_draw",
    ]
    for role in ROLES:
        prefix = f"{role}_"
        names.extend(
            [
                prefix + "hp",
                prefix + "max_hp",
                prefix + "atk",
                prefix + "def",
                prefix + "move",
                prefix + "vis",
                prefix + "weight",
                prefix + "cap",
                prefix + "bag_count",
                prefix + "status_count",
                *[prefix + "st_" + s for s in STATUS_FLAGS],
                prefix + "tile_id",
                prefix + "dead_flag",
            ]
        )
        if use_hp_delta:
            names.append(prefix + "d_hp")
    return names


FEATURE_NAMES = _build_feature_names(True)


def _tile_id(tile: str) -> int:
    """地形名 → 整数编号（未知地形归 0）。

    用编号而不是 one-hot，是为了不让 7 种地形把维度撑大；代价是
    地形之间有了"距离"，孤立森林会把它当有序数值看待。

    """
    try:
        return TILES.index(tile)
    except ValueError:
        return 0


def _unit_vector(u: Optional[Dict[str, Any]], prev_hp: Optional[float], use_hp_delta: bool) -> List[float]:
    """把一个角色摊平成固定长度的数值向量。

    死亡或缺失的角色也输出等长向量（只把 dead 标记置 1），
    这样每一帧的维度恒定，矩阵才能对齐。

    Args:
        u: 快照里的单个单位字典。
        prev_hp: 上一帧该角色的 HP；None 表示第一帧。
        use_hp_delta: 是否追加 d_hp 特征。

    """
    if u is None or u.get("dead"):
        # dead / missing
        vec = [0.0] * (10 + len(STATUS_FLAGS) + 2)
        vec[10 + STATUS_FLAGS.index("dead")] = 1.0  # st_dead
        vec[-1] = 1.0  # dead_flag
        if use_hp_delta:
            vec.append(0.0)
        return vec

    statuses = set(u.get("statuses") or [])
    hp = float(u.get("hp", 0))
    bag = u.get("bag") or []
    vec = [
        hp,
        float(u.get("max_hp", 0)),
        float(u.get("atk", 0)),
        float(u.get("def", 0)),
        float(u.get("move", 0)),
        float(u.get("vis", 0)),
        float(u.get("weight", 0)),
        float(u.get("cap", 0)),
        float(len(bag)),
        float(len(statuses)),
        *[1.0 if s in statuses else 0.0 for s in STATUS_FLAGS],
        float(_tile_id(str(u.get("tile", "normal")))),
        1.0 if "dead" in statuses else 0.0,
    ]
    if use_hp_delta:
        if prev_hp is None:
            vec.append(0.0)
        else:
            vec.append(hp - prev_hp)
    return vec


def snapshot_to_vector(
    snap: Dict[str, Any],
    prev_hps: Optional[Dict[str, float]] = None,
    use_hp_delta: bool = True,
) -> np.ndarray:
    """把一帧快照转成一个特征向量。

    拼装顺序：全局 7 维（回合/小时/天气 one-hot/熔岩圈/牌堆）
    + 四个角色各 20 维（固定顺序 ROLES）。

    Args:
        snap: snapshot 事件。
        prev_hps: 上一帧各角色 HP，用于计算 d_hp。
        use_hp_delta: 是否包含 d_hp。

    Returns:
        一维 float64 数组。

    """
    weather = str(snap.get("weather", "clear"))
    w_oh = [1.0 if weather == w else 0.0 for w in WEATHERS]
    global_part = [
        float(snap.get("round", 0)),
        float(snap.get("hour", 0)),
        *w_oh,
        float(snap.get("lava_inset", 0)),
        float(snap.get("deck_draw", 0)),
    ]
    units = {u.get("role"): u for u in (snap.get("units") or []) if u.get("role")}
    prev_hps = prev_hps or {}
    parts = list(global_part)
    for role in ROLES:
        parts.extend(_unit_vector(units.get(role), prev_hps.get(role), use_hp_delta))
    return np.asarray(parts, dtype=np.float64)


def extract_match_features(
    events: Sequence[Dict[str, Any]],
    match_id: str = "",
    use_hp_delta: bool = True,
) -> Tuple[np.ndarray, List[Dict[str, Any]]]:
    """把一局的 events 转成"每帧一行"的特征矩阵。

    只取 type == "snapshot" 的事件；同时返回每个快照的定位信息
    （match_id / t / round / hour / weather），便于报告里指到具体帧。

    Returns:
        (X, meta)：X 形状 (帧数, 特征数)；无快照时返回空矩阵。

    """
    rows: List[np.ndarray] = []
    meta: List[Dict[str, Any]] = []
    prev_hps: Dict[str, float] = {}
    for e in events:
        if e.get("type") != "snapshot":
            continue
        vec = snapshot_to_vector(e, prev_hps, use_hp_delta=use_hp_delta)
        rows.append(vec)
        meta.append(
            {
                "match_id": match_id,
                "t": e.get("t"),
                "round": e.get("round"),
                "hour": e.get("hour"),
                "weather": e.get("weather"),
            }
        )
        prev_hps = {}
        for u in e.get("units") or []:
            role = u.get("role")
            if role and not u.get("dead") and "hp" in u:
                prev_hps[role] = float(u["hp"])
    if not rows:
        names = _build_feature_names(use_hp_delta)
        return np.zeros((0, len(names)), dtype=np.float64), []
    return np.vstack(rows), meta


def extract_snapshots_matrix(
    matches: Sequence[Tuple[str, Sequence[Dict[str, Any]]]],
    use_hp_delta: bool = True,
) -> Tuple[np.ndarray, List[Dict[str, Any]], List[str]]:
    """matches: list of (match_id, events)."""
    all_x: List[np.ndarray] = []
    all_meta: List[Dict[str, Any]] = []
    for match_id, events in matches:
        x, m = extract_match_features(events, match_id=match_id, use_hp_delta=use_hp_delta)
        if len(x):
            all_x.append(x)
            all_meta.extend(m)
    names = _build_feature_names(use_hp_delta)
    if not all_x:
        return np.zeros((0, len(names)), dtype=np.float64), [], names
    return np.vstack(all_x), all_meta, names
