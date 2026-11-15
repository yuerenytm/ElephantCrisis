"""等价性检查：新一批结果与基线逐局对比。

用途：重构/优化之后，证明"行为没有变"。
比较字段固定为 winner / reason / full_rounds，其余（路径、耗时）不参与。

注意：这里的"基线"指的是**同一种子集**跑出来的一批数据，
所以两边种子必须完全一致，否则会比出大量假差异。
"""
from __future__ import annotations

from pathlib import Path
from typing import Any, Dict, List

from lab.events import iter_match_dirs, load_meta

COMPARE_KEYS = ("winner", "reason", "full_rounds")


def _index_by_seed(data_dir: Path) -> Dict[int, Dict[str, Any]]:
    out: Dict[int, Dict[str, Any]] = {}
    for d in iter_match_dirs(data_dir):
        meta = load_meta(d)
        seed = meta.get("seed")
        if seed is None:
            continue
        out[int(seed)] = meta
    return out


def compare(candidate_dir: Path, baseline_dir: Path) -> Dict[str, Any]:
    """逐局对比两批数据；返回差异明细与汇总。"""
    cand = _index_by_seed(candidate_dir)
    base = _index_by_seed(baseline_dir)

    only_cand = sorted(set(cand) - set(base))
    only_base = sorted(set(base) - set(cand))
    common = sorted(set(cand) & set(base))

    diffs: List[Dict[str, Any]] = []
    for seed in common:
        a, b = cand[seed], base[seed]
        fields = {k: (a.get(k), b.get(k)) for k in COMPARE_KEYS if a.get(k) != b.get(k)}
        if fields:
            diffs.append({"seed": seed, "candidate": a.get("winner"), "baseline": b.get("winner"),
                          "fields": {k: list(v) for k, v in fields.items()}})

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
    lines = [
        f"对比 {s['compared']} 局：一致 {s['identical']}，不同 {s['differing']}",
    ]
    if s["only_in_candidate"]:
        lines.append(f"  仅候选有：{len(s['only_in_candidate'])} 局 {s['only_in_candidate'][:5]}")
    if s["only_in_baseline"]:
        lines.append(f"  仅基线有：{len(s['only_in_baseline'])} 局 {s['only_in_baseline'][:5]}")
    for d in result["differences"][:10]:
        lines.append(f"  seed {d['seed']}: {d['candidate']} vs {d['baseline']}  {d['fields']}")
    return "\n".join(lines)
