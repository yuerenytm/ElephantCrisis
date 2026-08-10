from __future__ import annotations

from typing import Any, Dict, List, Optional, Tuple

ROLES = ("elephant", "human", "monkey", "cat")
ROLE_COLOR = {
    "elephant": "#6b6b75",
    "human": "#4a7fd4",
    "monkey": "#d4893c",
    "cat": "#e0b030",
}


def extract_snapshots(events: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    return [e for e in events if e.get("type") == "snapshot"]


def events_between(
    events: List[Dict[str, Any]],
    t_from: Optional[int],
    t_to: int,
) -> List[Dict[str, Any]]:
    out = []
    for e in events:
        t = e.get("t")
        if t is None:
            continue
        if t_from is not None and t <= t_from:
            continue
        if t > t_to:
            break
        if e.get("type") == "snapshot":
            continue
        out.append(e)
    return out


def _quasi_text(u: Dict[str, Any]) -> str:
    parts = []
    if u.get("crossbow_charged"):
        parts.append("弩蓄力")
    mc = u.get("motorcycle_active") or 0
    if mc > 0:
        parts.append(f"摩托{mc}")
    rx = u.get("pending_extra_actions") or 0
    if rx > 0:
        parts.append(f"红牛×{rx}")
    ad = u.get("adrenaline_rounds") or 0
    if ad > 0:
        parts.append(f"肾上腺素{ad}")
    ss = u.get("skill_shield") or 0
    if ss > 0:
        parts.append(f"技能盾{ss}")
    ams = u.get("amulet_shield") or 0
    if ams > 0:
        parts.append(f"护符盾{ams}")
    sc = u.get("skill_cooldown") or 0
    if sc > 0:
        parts.append(f"CD{sc}")
    dr = u.get("dying_rounds")
    if dr:
        parts.append(f"濒死{dr}")
    return " ".join(parts) or "-"


def unit_table_rows(snapshot: Dict[str, Any]) -> List[Dict[str, Any]]:
    rows = []
    for u in snapshot.get("units") or []:
        role = u.get("role", "?")
        if u.get("dead"):
            rows.append(
                {
                    "role": role,
                    "hp": "DEAD",
                    "atk": "-",
                    "def": "-",
                    "move": "-",
                    "cell": "-",
                    "tile": "-",
                    "statuses": "dead",
                    "bag": "",
                    "quasi": "-",
                }
            )
            continue
        rows.append(
            {
                "role": role,
                "hp": f"{u.get('hp')}/{u.get('max_hp')}",
                "atk": u.get("atk"),
                "def": u.get("def"),
                "move": u.get("move"),
                "cell": str(u.get("cell")),
                "tile": u.get("tile"),
                "statuses": ",".join(u.get("statuses") or []) or "-",
                "bag": ",".join(u.get("bag") or []) or "-",
                "quasi": _quasi_text(u),
            }
        )
    return rows


def grid_figure(snapshot: Dict[str, Any], width: int = 18, height: int = 18):
    """Plotly 散点图画角色位置。"""
    import plotly.graph_objects as go

    fig = go.Figure()
    # 网格底
    fig.add_trace(
        go.Scatter(
            x=[x for x in range(width) for _ in range(height)],
            y=[y for _ in range(width) for y in range(height)],
            mode="markers",
            marker=dict(size=4, color="rgba(200,200,200,0.25)"),
            hoverinfo="skip",
            showlegend=False,
        )
    )
    for u in snapshot.get("units") or []:
        role = u.get("role")
        if not role or u.get("dead"):
            continue
        cell = u.get("cell") or [0, 0]
        fig.add_trace(
            go.Scatter(
                x=[cell[0]],
                y=[cell[1]],
                mode="markers+text",
                name=role,
                text=[role[:3]],
                textposition="top center",
                marker=dict(size=18, color=ROLE_COLOR.get(role, "#888")),
            )
        )
    lava = snapshot.get("lava_inset") or 0
    fig.update_layout(
        title=f"round={snapshot.get('round')} hour={snapshot.get('hour')} "
        f"weather={snapshot.get('weather')} lava_inset={lava}",
        xaxis=dict(range=[-0.5, width - 0.5], title="x", scaleanchor="y", scaleratio=1),
        yaxis=dict(range=[height - 0.5, -0.5], title="y"),
        height=520,
        margin=dict(l=40, r=20, t=50, b=40),
        legend=dict(orientation="h"),
    )
    return fig


def summarize_event(e: Dict[str, Any]) -> str:
    typ = e.get("type")
    t = e.get("t")
    if typ == "damage":
        return f"t={t} damage {e.get('actor')}→{e.get('target')} {e.get('kind')} raw={e.get('raw')} dealt={e.get('dealt')}"
    if typ == "move":
        return f"t={t} move {e.get('actor')} {e.get('from_cell')}→{e.get('to_cell')}"
    if typ == "draw":
        return f"t={t} draw {e.get('actor')} {e.get('item')}"
    if typ == "status":
        return f"t={t} status {e.get('actor')} {e.get('status')} {e.get('op')}"
    if typ == "turn_start":
        return f"t={t} turn_start {e.get('actor')} round={e.get('round')}"
    if typ == "match_end":
        return f"t={t} match_end {e.get('reason')} winner={e.get('winner')}"
    return f"t={t} {typ} { {k:v for k,v in e.items() if k not in ('t','type')} }"
