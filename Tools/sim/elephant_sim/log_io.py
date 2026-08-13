from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Iterable, Iterator, List, Optional


@dataclass
class MatchLog:
    match_id: str
    path: Path
    events: List[Dict[str, Any]]
    meta: Dict[str, Any]

    @property
    def events_path(self) -> Path:
        return self.path / "events.jsonl"


def iter_jsonl(path: Path) -> Iterator[Dict[str, Any]]:
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            yield json.loads(line)


def load_events(path: Path) -> List[Dict[str, Any]]:
    return list(iter_jsonl(path))


def load_meta(match_dir: Path) -> Dict[str, Any]:
    meta_path = match_dir / "meta.json"
    if not meta_path.is_file():
        return {}
    with meta_path.open("r", encoding="utf-8") as f:
        return json.load(f)


def load_match(match_dir: Path) -> MatchLog:
    events_path = match_dir / "events.jsonl"
    events = load_events(events_path) if events_path.is_file() else []
    return MatchLog(
        match_id=match_dir.name,
        path=match_dir,
        events=events,
        meta=load_meta(match_dir),
    )


def iter_match_dirs(input_dir: Path) -> Iterator[Path]:
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
    for d in iter_match_dirs(input_dir):
        yield load_match(d)


def write_jsonl(path: Path, events: Iterable[Dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        for e in events:
            f.write(json.dumps(e, ensure_ascii=False) + "\n")
