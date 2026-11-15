"""等价性检查：同一批种子下，两次实验逐局对比。

用途：重构/优化之后，证明"行为没有变"。
只比 winner / reason / full_rounds；路径、耗时这些不参与。
"""
from __future__ import annotations

from typing import Any, Dict, Iterable, List

COMPARE_KEYS = ("winner", "reason", "full_rounds")


def _index_by_seed(metas: Iterable[Dict[str, Any]]) -> Dict[int, Dict[str, Any]]:
    out: Dict[int, Dict[str, Any]] = {}
    for meta in metas:
        seed = meta.get("seed")
        if seed is not None:
            out[int(seed)] = meta
    return out


def compare(candidate: Iterable[Dict[str, Any]], baseline: Iterable[Dict[str, Any]]) -> Dict[str, Any]:
    """逐局对比两批 meta；返回差异明细与汇总。"""
    cand = _index_by_seed(candidate)
    base = _index_by_seed(baseline)

    only_cand = sorted(set(cand) - set(base))
    only_base = sorted(set(base) - set(cand))
    common = sorted(set(cand) & set(base))

    diffs: List[Dict[str, Any]] = []
    for seed in common:
        a, b = cand[seed], base[seed]
        fields = {k: (a.get(k), b.get(k)) for k in COMPARE_KEYS if a.get(k) != b.get(k)}
        if fields:
            diffs.append({
                "seed": seed,
                "candidate": a.get("winner"),
                "baseline": b.get("winner"),
                "fields": {k: list(v) for k, v in fields.items()},
            })

    return {
        "summary": {
            "compared": len(common),
            "identical": len(common) - len(diffs),
            "differing": len(diffs),
            "only_in_candidate": only_cand,
            "only_in_baseline": only_base,
        },
        "differences": diffs,
    }


def format_console(result: Dict[str, Any]) -> str:
    s = result["summary"]
    lines = [f"对比 {s['compared']} 局：一致 {s['identical']}，不同 {s['differing']}"]
    if s["only_in_candidate"]:
        lines.append(f"  仅候选有：{len(s['only_in_candidate'])} 局 {s['only_in_candidate'][:5]}")
    if s["only_in_baseline"]:
        lines.append(f"  仅基线有：{len(s['only_in_baseline'])} 局 {s['only_in_baseline'][:5]}")
    for d in result["differences"][:10]:
        lines.append(f"  seed {d['seed']}: {d['candidate']} vs {d['baseline']}  {d['fields']}")
    return "\n".join(lines)
