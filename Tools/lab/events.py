"""对局日志的读写：match_*/meta.json 与 events.jsonl。

产物由 Game 的 LogicSim 写出（见 Game/Assets/Scripts/LogicSim/LogicMatchLogger.cs），
本模块只负责读取与汇总，不生成任何游戏数据。
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Iterable, Iterator, List


@dataclass
class MatchLog:
    """一局对局的内存表示。"""

    # 目录名，形如 match_900
    match_id: str
    # 对局目录
    path: Path
    # 逐事件列表；collect=balance 时为空
    events: List[Dict[str, Any]]
    # meta.json 内容；文件缺失时为空字典
    meta: Dict[str, Any]

    @property
    def events_path(self) -> Path:
        """本局 events.jsonl 的路径。"""
        return self.path / "events.jsonl"


def iter_jsonl(path: Path) -> Iterator[Dict[str, Any]]:
    """逐行解析 JSONL，每行产出一个字典。

    生成器写法：读取时内存里只有当前行；空行会被跳过。
    """
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            yield json.loads(line)


def load_events(path: Path) -> List[Dict[str, Any]]:
    """把 events.jsonl 全量读进内存。

    注意：事件里包含 snapshot，单局可能有数百条；批量分析时优先用 iter_jsonl。
    """
    return list(iter_jsonl(path))


def load_meta(match_dir: Path) -> Dict[str, Any]:
    """读取一局的 meta.json；文件不存在时返回空字典而不是抛异常。"""
    meta_path = match_dir / "meta.json"
    if not meta_path.is_file():
        return {}
    with meta_path.open("r", encoding="utf-8") as f:
        return json.load(f)


def load_match(match_dir: Path) -> MatchLog:
    """读取一整局：meta + events（没有 events.jsonl 时为空列表）。"""
    events_path = match_dir / "events.jsonl"
    events = load_events(events_path) if events_path.is_file() else []
    return MatchLog(
        match_id=match_dir.name,
        path=match_dir,
        events=events,
        meta=load_meta(match_dir),
    )


def iter_match_dirs(input_dir: Path) -> Iterator[Path]:
    """按名称排序遍历输入目录下的 match_* 子目录。

    判定规则：目录名以 match_ 开头，且 meta.json 或 events.jsonl 至少存在一个
    （兼容只写 meta 的 balance 模式与只写 events 的旧数据）。
    目录不存在时静默返回空，不抛异常。
    """
    if not input_dir.is_dir():
        return
    for p in sorted(input_dir.iterdir()):
        if not (p.is_dir() and p.name.startswith("match_")):
            continue
        # meta.json 每局必有（sim 无条件写）；events.jsonl 仅 logic/both 写。
        # 兼容旧数据：只要二者其一存在即算一局。
        if (p / "meta.json").is_file() or (p / "events.jsonl").is_file():
            yield p


def iter_matches(input_dir: Path) -> Iterator[MatchLog]:
    """遍历并加载所有对局（生成器版 load_match）。

    目前仓库内没有调用方，保留给重放 / 造数脚本使用。
    """
    for d in iter_match_dirs(input_dir):
        yield load_match(d)


def write_jsonl(path: Path, events: Iterable[Dict[str, Any]]) -> None:
    """把事件序列写成 JSONL（git 友好、可逐行 diff）。

    目前仓库内没有调用方，保留给重放 / 造数脚本使用。
    """
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        for e in events:
            f.write(json.dumps(e, ensure_ascii=False) + "\n")
