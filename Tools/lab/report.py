"""统一的报告输出：控制台摘要 + 可选 Markdown。

两个模块共用同一套格式，避免"每个工具一种输出风格"。
"""
from __future__ import annotations

from pathlib import Path
from typing import Sequence


def write_markdown(path: Path, title: str, sections: Sequence[tuple[str, str]]) -> Path:
    """把若干 (小节标题, 正文) 写成一份 Markdown 报告。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = [f"# {title}", ""]
    for heading, body in sections:
        lines += [f"## {heading}", "", body.rstrip(), ""]
    path.write_text("\n".join(lines), encoding="utf-8")
    return path


def kv_block(pairs: Sequence[tuple[str, object]]) -> str:
    """把成对的指标排成 Markdown 表格。"""
    if not pairs:
        return "（无数据）"
    lines = ["| 指标 | 值 |", "|---|---|"]
    for k, v in pairs:
        lines.append(f"| {k} | {v} |")
    return "\n".join(lines)
