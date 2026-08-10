#!/usr/bin/env python3
"""分析 Unity 客户端性能 JSONL → 卡顿秒 IF 筛查 + 可疑原因共现报告。"""
from __future__ import annotations

import argparse
import json
import math
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

try:
    import yaml
except ImportError:
    yaml = None

try:
    import numpy as np
    from sklearn.ensemble import IsolationForest
except ImportError:
    np = None
    IsolationForest = None


ROOT = Path(__file__).resolve().parent.parent

ACTION_KINDS = [
    "combat",
    "aoe_bomb",
    "aoe_flame",
    "util_flash",
    "place_hazard",
    "skill",
    "item",
    "move",
    "turn",
    "world_hazard",
    "log_other",
]

HINTS = {
    "resource": "优先排查同步 Resources.Load / 大图解码：预加载、异步加载、压缩纹理、拆分图集。",
    "aoe_bomb": "炸弹多目标结算易引发连锁 UI/特效刷新：批量结算、合并战报、延迟刷新视野。",
    "aoe_flame": "火焰喷射多格命中：减少逐目标 Instantiate，复用 VFX，合并状态写入。",
    "util_flash": "闪光弹后 VisibilityService.RefreshWorld：检查全图重算频率，改为脏区刷新。",
    "combat": "近战/射击当帧若伴随立绘/UI：检查 RolePortrait 加载与伤害飘字。",
    "skill": "技能多步交互（抢夺/强化/隐匿）：检查状态机切换与目标高亮重建成本。",
    "place_hazard": "放置陷阱后 Hazard/地图刷新：避免整表 Rebuild。",
    "world_hazard": "熔岩/天气/DoT 结算：回合开始批量处理时注意 GC 与 UI 刷屏。",
    "turn": "回合切换文案/选中态刷新：检查全单位 SetSelected 与日志 UI。",
    "memory": "该秒 mono 上涨明显：排查托管分配（字符串拼接、临时 List、LINQ）。",
    "item": "道具使用路径：检查背包 UI 全量 Rebuild。",
}


def load_config(path: Optional[Path]) -> Dict[str, Any]:
    cfg_path = path or (ROOT / "config" / "client_perf.yaml")
    defaults: Dict[str, Any] = {
        "hitch_ms": 33.3,
        "recent_load_window_ms": 100.0,
        "recent_action_window_ms": 500.0,
        "analyze": {
            "top_k_loads": 20,
            "top_k_hitches": 30,
            "top_k_causes": 15,
            "cooccur_pad_ms": 250.0,
            "isolation_forest": {
                "enabled": True,
                "contamination": 0.08,
                "n_estimators": 200,
                "random_state": 42,
                "min_seconds": 8,
            },
        },
    }
    if yaml is None or not cfg_path.is_file():
        return defaults
    with cfg_path.open("r", encoding="utf-8") as f:
        data = yaml.safe_load(f) or {}
    for k in ("hitch_ms", "recent_load_window_ms", "recent_action_window_ms"):
        if k in data:
            defaults[k] = data[k]
    if "analyze" in data:
        defaults["analyze"] = {**defaults["analyze"], **(data.get("analyze") or {})}
        if "isolation_forest" in (data.get("analyze") or {}):
            defaults["analyze"]["isolation_forest"] = {
                **defaults["analyze"]["isolation_forest"],
                **(data["analyze"]["isolation_forest"] or {}),
            }
    return defaults


def load_events(session_dir: Path) -> List[Dict[str, Any]]:
    path = session_dir / "events.jsonl"
    if not path.is_file():
        raise FileNotFoundError(path)
    rows = []
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def _sec_index(t_ms: float) -> int:
    return int(math.floor(max(0.0, t_ms) / 1000.0))


def build_seconds(events: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """优先用 second_sample；否则从 hitch/frame/load/action 聚合。"""
    samples = [e for e in events if e.get("type") == "second_sample"]
    if samples:
        out = []
        for i, e in enumerate(samples):
            t = float(e.get("t_ms", 0))
            out.append(
                {
                    "sec": _sec_index(t),
                    "t_ms": t,
                    "frames": int(e.get("frames", 0)),
                    "fps": float(e.get("fps", 0)),
                    "avg_dt_ms": float(e.get("avg_dt_ms", 0)),
                    "max_dt_ms": float(e.get("max_dt_ms", 0)),
                    "hitch_count": int(e.get("hitch_count", 0)),
                    "load_count": int(e.get("load_count", 0)),
                    "action_count": int(e.get("action_count", 0)),
                    "mono_mb": float(e.get("mono_mb", 0)),
                    "mono_delta_mb": float(e.get("mono_delta_mb", 0)),
                    "load_ms_sum": 0.0,
                    "load_ms_max": 0.0,
                    "kind_counts": Counter(),
                }
            )
        # 把同秒内的 load/action 细节补进 sample（按 t_ms 对齐）
        by_sec = {s["sec"]: s for s in out}
        for e in events:
            sec = _sec_index(float(e.get("t_ms", 0)))
            s = by_sec.get(sec)
            if s is None:
                continue
            typ = e.get("type")
            if typ == "resource_load":
                dt = float(e.get("dt_ms", 0))
                s["load_ms_sum"] += dt
                s["load_ms_max"] = max(s["load_ms_max"], dt)
            elif typ == "game_action":
                s["kind_counts"][e.get("kind") or "log_other"] += 1
        return out

    # fallback 聚合
    buckets: Dict[int, Dict[str, Any]] = {}

    def bucket(sec: int) -> Dict[str, Any]:
        if sec not in buckets:
            buckets[sec] = {
                "sec": sec,
                "t_ms": sec * 1000.0,
                "frames": 0,
                "fps": 0.0,
                "avg_dt_ms": 0.0,
                "max_dt_ms": 0.0,
                "hitch_count": 0,
                "load_count": 0,
                "action_count": 0,
                "mono_mb": 0.0,
                "mono_delta_mb": 0.0,
                "load_ms_sum": 0.0,
                "load_ms_max": 0.0,
                "kind_counts": Counter(),
                "_sum_dt": 0.0,
            }
        return buckets[sec]

    last_mono: Optional[float] = None
    for e in events:
        sec = _sec_index(float(e.get("t_ms", 0)))
        b = bucket(sec)
        typ = e.get("type")
        if typ in ("hitch", "frame"):
            dt = float(e.get("dt_ms", 0))
            b["frames"] += 1
            b["_sum_dt"] += dt
            b["max_dt_ms"] = max(b["max_dt_ms"], dt)
            if typ == "hitch":
                b["hitch_count"] += 1
        elif typ == "resource_load":
            dt = float(e.get("dt_ms", 0))
            b["load_count"] += 1
            b["load_ms_sum"] += dt
            b["load_ms_max"] = max(b["load_ms_max"], dt)
        elif typ == "game_action":
            b["action_count"] += 1
            b["kind_counts"][e.get("kind") or "log_other"] += 1
        elif typ == "memory":
            mono = float(e.get("mono_mb", 0))
            b["mono_mb"] = mono
            if last_mono is not None:
                b["mono_delta_mb"] = mono - last_mono
            last_mono = mono

    out = []
    for sec in sorted(buckets):
        b = buckets[sec]
        if b["frames"] > 0:
            b["avg_dt_ms"] = b["_sum_dt"] / b["frames"]
            b["fps"] = 1000.0 / b["avg_dt_ms"] if b["avg_dt_ms"] > 0.01 else 0.0
        del b["_sum_dt"]
        out.append(b)
    return out


def second_feature_row(s: Dict[str, Any]) -> Tuple[List[str], List[float]]:
    names = [
        "fps",
        "avg_dt_ms",
        "max_dt_ms",
        "hitch_count",
        "load_count",
        "load_ms_sum",
        "load_ms_max",
        "action_count",
        "mono_delta_mb",
    ]
    vals = [
        float(s.get("fps", 0)),
        float(s.get("avg_dt_ms", 0)),
        float(s.get("max_dt_ms", 0)),
        float(s.get("hitch_count", 0)),
        float(s.get("load_count", 0)),
        float(s.get("load_ms_sum", 0)),
        float(s.get("load_ms_max", 0)),
        float(s.get("action_count", 0)),
        float(s.get("mono_delta_mb", 0)),
    ]
    kinds = s.get("kind_counts") or Counter()
    for k in ACTION_KINDS:
        names.append(f"act_{k}")
        vals.append(float(kinds.get(k, 0)))
    return names, vals


def run_isolation_forest(
    seconds: List[Dict[str, Any]], if_cfg: Dict[str, Any]
) -> Dict[str, Any]:
    if not if_cfg.get("enabled", True):
        return {"enabled": False, "anomalous_seconds": [], "note": "disabled"}
    if np is None or IsolationForest is None:
        return {
            "enabled": False,
            "anomalous_seconds": [],
            "note": "sklearn/numpy unavailable; install scikit-learn",
        }
    min_sec = int(if_cfg.get("min_seconds", 8))
    if len(seconds) < min_sec:
        # 样本太少：用 hitch_count / max_dt 阈值兜底
        flagged = [
            s
            for s in seconds
            if int(s.get("hitch_count", 0)) > 0 or float(s.get("max_dt_ms", 0)) >= 40
        ]
        return {
            "enabled": True,
            "mode": "threshold_fallback",
            "anomalous_seconds": [_sec_public(s, score=None) for s in flagged],
            "note": f"seconds<{min_sec}, fallback to hitch/max_dt threshold",
        }

    feature_names: Optional[List[str]] = None
    rows = []
    for s in seconds:
        names, vals = second_feature_row(s)
        if feature_names is None:
            feature_names = names
        rows.append(vals)
    X = np.asarray(rows, dtype=np.float64)
    mean = X.mean(axis=0)
    std = X.std(axis=0)
    std[std < 1e-8] = 1.0
    Xs = (X - mean) / std

    contamination = float(if_cfg.get("contamination", 0.08))
    model = IsolationForest(
        n_estimators=int(if_cfg.get("n_estimators", 200)),
        contamination=contamination,
        random_state=int(if_cfg.get("random_state", 42)),
        n_jobs=-1,
    )
    model.fit(Xs)
    scores = model.score_samples(Xs)
    pct = max(0.5, min(20.0, contamination * 100.0))
    threshold = float(np.percentile(scores, pct))
    anomalous = []
    for i, s in enumerate(seconds):
        if scores[i] < threshold:
            anomalous.append(_sec_public(s, score=float(scores[i])))
    anomalous.sort(key=lambda x: (x.get("score") if x.get("score") is not None else 0))
    return {
        "enabled": True,
        "mode": "isolation_forest",
        "feature_names": feature_names,
        "score_threshold": threshold,
        "contamination": contamination,
        "anomalous_seconds": anomalous,
        "n_seconds": len(seconds),
        "n_anomalous": len(anomalous),
    }


def _sec_public(s: Dict[str, Any], score: Optional[float]) -> Dict[str, Any]:
    kinds = s.get("kind_counts") or Counter()
    return {
        "sec": s.get("sec"),
        "t_ms": s.get("t_ms"),
        "score": None if score is None else round(score, 5),
        "fps": round(float(s.get("fps", 0)), 2),
        "max_dt_ms": round(float(s.get("max_dt_ms", 0)), 3),
        "hitch_count": int(s.get("hitch_count", 0)),
        "load_count": int(s.get("load_count", 0)),
        "action_count": int(s.get("action_count", 0)),
        "mono_delta_mb": round(float(s.get("mono_delta_mb", 0)), 3),
        "top_actions": dict(Counter(kinds).most_common(5)),
    }


def events_in_window(
    events: List[Dict[str, Any]], t0: float, t1: float
) -> List[Dict[str, Any]]:
    out = []
    for e in events:
        t = float(e.get("t_ms", 0))
        if t0 <= t <= t1:
            out.append(e)
    return out


def cause_key(e: Dict[str, Any]) -> Optional[Tuple[str, str]]:
    typ = e.get("type")
    if typ == "resource_load":
        return ("resource", str(e.get("path") or "?"))
    if typ == "game_action":
        return ("action", str(e.get("kind") or "log_other"))
    if typ == "memory":
        return ("memory", "mono_sample")
    return None


def attribute_causes(
    events: List[Dict[str, Any]],
    anomalous_secs: List[Dict[str, Any]],
    hitches: List[Dict[str, Any]],
    pad_ms: float,
    top_k: int,
) -> Dict[str, Any]:
    """共现归因：异常秒 / hitch 窗口内事件 vs 全局基线。"""
    # 全局基线
    global_counts: Counter = Counter()
    for e in events:
        ck = cause_key(e)
        if ck:
            global_counts[ck] += 1

    window_counts: Counter = Counter()
    window_examples: Dict[Tuple[str, str], List[Dict[str, Any]]] = defaultdict(list)
    windows_used = 0

    def add_window(t_center: float, half: float, extra: Optional[Dict[str, Any]] = None):
        nonlocal windows_used
        windows_used += 1
        for e in events_in_window(events, t_center - half, t_center + half):
            ck = cause_key(e)
            if not ck:
                continue
            window_counts[ck] += 1
            if len(window_examples[ck]) < 3:
                ex = {"t_ms": e.get("t_ms"), "type": e.get("type")}
                if e.get("path"):
                    ex["path"] = e.get("path")
                    ex["dt_ms"] = e.get("dt_ms")
                if e.get("kind"):
                    ex["kind"] = e.get("kind")
                    ex["detail"] = e.get("detail")
                if extra:
                    ex["context"] = extra
                window_examples[ck].append(ex)

    for s in anomalous_secs:
        t = float(s.get("t_ms", (s.get("sec") or 0) * 1000))
        add_window(t + 500.0, 500.0 + pad_ms, {"sec": s.get("sec"), "score": s.get("score")})

    for h in hitches:
        t = float(h.get("t_ms", 0))
        add_window(t, pad_ms + 100.0, {"hitch_dt_ms": h.get("dt_ms")})
        for path in h.get("recent_loads") or []:
            ck = ("resource", str(path))
            window_counts[ck] += 1
        for kind in h.get("recent_actions") or []:
            ck = ("action", str(kind))
            window_counts[ck] += 1

    ranked = []
    for ck, cnt in window_counts.items():
        cat, key = ck
        # 共现次数相对全局出现的抬升
        score = cnt * math.log1p(cnt / max(1.0, global_counts.get(ck, 0) * 0.25 + 0.25))
        hint_key = key if cat == "action" else "resource"
        if cat == "memory":
            hint_key = "memory"
        ranked.append(
            {
                "category": cat,
                "key": key,
                "cooccur_count": cnt,
                "global_count": global_counts.get(ck, 0),
                "score": round(score, 4),
                "suspect_label": _label(cat, key),
                "optimization_hint": HINTS.get(hint_key) or HINTS.get("resource"),
                "examples": window_examples.get(ck, [])[:3],
            }
        )
    ranked.sort(key=lambda x: -x["score"])
    return {
        "windows_used": windows_used,
        "ranked_suspected_causes": ranked[:top_k],
        "disclaimer": "共现≠根因；按卡顿秒/ hitch 窗口内事件抬升排序，供人工复核。",
    }


def _label(cat: str, key: str) -> str:
    if cat == "resource":
        return f"资源加载: {key}"
    if cat == "action":
        return f"玩法行动: {key}"
    if cat == "memory":
        return "内存采样点（可能伴随分配尖峰）"
    return f"{cat}:{key}"


def analyze(session_dir: Path, cfg: Dict[str, Any]) -> Dict[str, Any]:
    events = load_events(session_dir)
    hitch_ms = float(cfg.get("hitch_ms", 33.3))
    window = float(cfg.get("recent_load_window_ms", 100.0))
    analyze_cfg = cfg.get("analyze") or {}
    top_k_loads = int(analyze_cfg.get("top_k_loads", 20))
    top_k_hitches = int(analyze_cfg.get("top_k_hitches", 30))
    top_k_causes = int(analyze_cfg.get("top_k_causes", 15))
    pad_ms = float(analyze_cfg.get("cooccur_pad_ms", 250.0))
    if_cfg = analyze_cfg.get("isolation_forest") or {}

    hitches = [e for e in events if e.get("type") == "hitch"]
    frames = [e for e in events if e.get("type") in ("hitch", "frame")]
    loads = [e for e in events if e.get("type") == "resource_load"]
    actions = [e for e in events if e.get("type") == "game_action"]
    mems = [e for e in events if e.get("type") == "memory"]
    session_end = next((e for e in reversed(events) if e.get("type") == "session_end"), None)

    dts = [float(e.get("dt_ms", 0)) for e in frames]
    dts_sorted = sorted(dts)

    def pct(p: float) -> Optional[float]:
        if not dts_sorted:
            return None
        i = min(len(dts_sorted) - 1, max(0, int(round((p / 100.0) * (len(dts_sorted) - 1)))))
        return round(dts_sorted[i], 3)

    slow_loads = sorted(loads, key=lambda e: -float(e.get("dt_ms", 0)))[:top_k_loads]
    top_hitches = sorted(hitches, key=lambda e: -float(e.get("dt_ms", 0)))[:top_k_hitches]

    # hitch ↔ 近期 load（旧字段保留）
    suspected_resource = []
    for h in hitches:
        t = float(h.get("t_ms", 0))
        related = [
            L for L in loads if 0 <= t - float(L.get("t_ms", 0)) <= window
        ]
        recent = h.get("recent_loads") or []
        paths = list({*(L.get("path") for L in related), *recent})
        if paths:
            suspected_resource.append(
                {
                    "t_ms": t,
                    "dt_ms": h.get("dt_ms"),
                    "frame": h.get("frame"),
                    "related_paths": paths,
                    "related_loads": [
                        {"path": L.get("path"), "dt_ms": L.get("dt_ms")} for L in related
                    ],
                    "recent_actions": h.get("recent_actions") or [],
                }
            )

    path_load_stats: Dict[str, Dict[str, Any]] = {}
    for L in loads:
        path = L.get("path") or "?"
        st = path_load_stats.setdefault(path, {"count": 0, "total_ms": 0.0, "max_ms": 0.0})
        dt = float(L.get("dt_ms", 0))
        st["count"] += 1
        st["total_ms"] += dt
        st["max_ms"] = max(st["max_ms"], dt)
    for st in path_load_stats.values():
        st["total_ms"] = round(st["total_ms"], 3)
        st["max_ms"] = round(st["max_ms"], 3)
        st["avg_ms"] = round(st["total_ms"] / max(1, st["count"]), 3)

    seconds = build_seconds(events)
    if_result = run_isolation_forest(seconds, if_cfg)
    attribution = attribute_causes(
        events,
        if_result.get("anomalous_seconds") or [],
        hitches,
        pad_ms=pad_ms,
        top_k=top_k_causes,
    )

    action_kind_stats = Counter(a.get("kind") or "?" for a in actions)

    report = {
        "session_dir": str(session_dir),
        "summary": {
            "events": len(events),
            "hitches": len(hitches),
            "resource_loads": len(loads),
            "game_actions": len(actions),
            "memory_samples": len(mems),
            "second_samples": len(seconds),
            "anomalous_seconds": len(if_result.get("anomalous_seconds") or []),
            "hitch_threshold_ms": hitch_ms,
            "frame_p95_ms": pct(95),
            "frame_p99_ms": pct(99),
            "session_end": session_end,
            "suspected_resource_hitches": len(suspected_resource),
            "top_suspected_cause": (
                attribution["ranked_suspected_causes"][0]["suspect_label"]
                if attribution.get("ranked_suspected_causes")
                else None
            ),
        },
        "isolation_forest": {
            k: v
            for k, v in if_result.items()
            if k != "feature_names" or if_result.get("mode") == "isolation_forest"
        },
        "suspected_causes": attribution,
        "action_kind_counts": dict(action_kind_stats),
        "top_slow_loads": [
            {
                "path": e.get("path"),
                "asset_type": e.get("asset_type"),
                "dt_ms": e.get("dt_ms"),
                "ok": e.get("ok"),
            }
            for e in slow_loads
        ],
        "top_hitches": [
            {
                "t_ms": e.get("t_ms"),
                "dt_ms": e.get("dt_ms"),
                "frame": e.get("frame"),
                "recent_loads": e.get("recent_loads"),
                "recent_actions": e.get("recent_actions"),
            }
            for e in top_hitches
        ],
        "suspected_resource_caused_hitches": suspected_resource[:50],
        "loads_by_path": dict(
            sorted(path_load_stats.items(), key=lambda kv: -kv[1]["max_ms"])[:40]
        ),
    }
    return report


def main() -> int:
    p = argparse.ArgumentParser(description="Unity 客户端性能 JSONL 分析（IF + 可疑原因）")
    p.add_argument("--input", type=str, required=True, help="session 目录（含 events.jsonl）")
    p.add_argument("--out", type=str, default=None, help="报告输出目录，默认 perf_analysis/reports")
    p.add_argument("--config", type=str, default=None)
    args = p.parse_args()

    session_dir = Path(args.input)
    out_dir = Path(args.out) if args.out else ROOT / "reports"
    if not out_dir.is_absolute():
        out_dir = ROOT / out_dir
    cfg = load_config(Path(args.config) if args.config else None)

    report = analyze(session_dir, cfg)
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / "client_perf_report.json"
    with out_path.open("w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)

    print(json.dumps(report["summary"], ensure_ascii=False, indent=2))
    causes = (report.get("suspected_causes") or {}).get("ranked_suspected_causes") or []
    if causes:
        print("\nTop suspected causes:")
        for c in causes[:5]:
            print(f"  - [{c['score']}] {c['suspect_label']}")
            print(f"    hint: {c['optimization_hint']}")
    print(f"\nWrote {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
