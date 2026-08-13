from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, List, Optional

try:
    import yaml
except ImportError:  # pragma: no cover
    yaml = None

TOOLS = Path(__file__).resolve().parent.parent  # tools/balance_analysis → tools
REPO = TOOLS.parent
DEFAULT_RULES = REPO / "Game" / "Assets" / "StreamingAssets" / "Config" / "game_rules.yaml"
DEFAULT_ROLE_DOC = REPO / "Docs" / "规则_角色与基础属性.md"
DEFAULT_OVERVIEW_DOC = REPO / "Docs" / "规则_游戏概述与规则.md"


def load_yaml(path: Path) -> Dict[str, Any]:
    if yaml is None:
        raise RuntimeError("需要 PyYAML：pip install PyYAML")
    with path.open("r", encoding="utf-8") as f:
        return yaml.safe_load(f) or {}


def load_json(path: Path) -> Dict[str, Any]:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def _slim_report(report: Dict[str, Any], keys: Optional[List[str]]) -> Dict[str, Any]:
    if not keys:
        return {
            "summary": report.get("summary"),
            "by_role": report.get("by_role"),
            "strategy_usage": report.get("strategy_usage"),
        }
    return {k: report.get(k) for k in keys if k in report}


def _read_text_truncated(path: Path, max_chars: int) -> Optional[str]:
    if not path.is_file():
        return None
    text = path.read_text(encoding="utf-8")
    if len(text) <= max_chars:
        return text
    return text[: max_chars - 20] + "\n\n…（已截断）\n"


def build_context(
    balance_report: Dict[str, Any],
    *,
    rules_path: Optional[Path] = None,
    role_doc_path: Optional[Path] = None,
    overview_doc_path: Optional[Path] = None,
    cfg: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    """把统计报告 + 规则压成适合塞进 prompt 的上下文（不做全量日志）。"""
    cfg = cfg or {}
    pre = cfg.get("preprocess") or {}
    keys = pre.get("report_keys")
    slim = _slim_report(balance_report, keys)

    rules_obj = None
    if pre.get("include_game_rules", True):
        rp = rules_path
        if rp is None:
            rp = DEFAULT_RULES
        if rp.is_file():
            rules_obj = load_yaml(rp)

    role_doc = None
    if pre.get("include_role_doc", True):
        dp = role_doc_path or DEFAULT_ROLE_DOC
        role_doc = _read_text_truncated(dp, int(pre.get("role_doc_max_chars", 6000)))

    overview_doc = None
    if pre.get("include_overview_doc", True):
        op = overview_doc_path or DEFAULT_OVERVIEW_DOC
        overview_doc = _read_text_truncated(
            op, int(pre.get("overview_doc_max_chars", 8000))
        )

    flags = (cfg.get("advice") or {})
    return {
        "meta": {
            "source": "preprocessed",
            "note": (
                "本上下文已聚合，不含 sim/output 原始 events.jsonl。"
                "结论仅对应当前仿真 AI / 样本，不等于真人平衡。"
            ),
            "thresholds": {
                "win_rate_low": flags.get("win_rate_low", 0.12),
                "win_rate_high": flags.get("win_rate_high", 0.38),
            },
        },
        "balance_stats": slim,
        "game_rules": rules_obj,
        "overview_doc_excerpt": overview_doc,
        "role_design_doc_excerpt": role_doc,
    }


def context_to_prompt(context: Dict[str, Any]) -> str:
    stats = json.dumps(context.get("balance_stats"), ensure_ascii=False, indent=2)
    rules = json.dumps(context.get("game_rules"), ensure_ascii=False, indent=2)
    overview = context.get("overview_doc_excerpt") or "（未提供游戏概述文档摘录）"
    doc = context.get("role_design_doc_excerpt") or "（未提供角色设计文档摘录）"
    th = (context.get("meta") or {}).get("thresholds") or {}
    low = th.get("win_rate_low", 0.12)
    high = th.get("win_rate_high", 0.38)

    return f"""你是《象群危机》的数值策划助手。根据「聚合后的仿真统计」和「规则配置」给出平衡建议。

## 硬性约束
1. 统计来自 AI 对局仿真，不是真人数据；所有建议必须标明「在当前 AI 策略样本下」。
2. 不要假设你看过逐局原始日志；你只有下面的聚合统计。
3. 优先建议修改 `game_rules.yaml` 中已有字段（HP/攻防移/技能冷却相关等），每次少改、可验证。
4. 胜率长期 < {low:.0%} 视为偏弱，> {high:.0%} 视为偏强（经验阈值，非硬规则）。
5. 输出使用中文 Markdown，结构如下：
   - 总览（1 段）
   - 分角色问题（依据具体指标）
   - 建议改动清单（字段路径 / 方向 / 预期影响）
   - 验证计划（建议重跑多少局、看哪些指标）
   - 风险与不确定点

## 聚合统计（balance_report 精简）
```json
{stats}
```

## game_rules.yaml
```json
{rules}
```

## 游戏概述与规则（摘录）
{overview}

## 角色设计文档摘录
{doc}
"""

def write_context_artifacts(context: Dict[str, Any], prompt: str, out_dir: Path) -> Dict[str, Path]:
    out_dir.mkdir(parents=True, exist_ok=True)
    ctx_path = out_dir / "advisor_context.json"
    prompt_path = out_dir / "advisor_prompt.md"
    with ctx_path.open("w", encoding="utf-8") as f:
        json.dump(context, f, ensure_ascii=False, indent=2)
    prompt_path.write_text(prompt, encoding="utf-8")
    return {"context": ctx_path, "prompt": prompt_path}
