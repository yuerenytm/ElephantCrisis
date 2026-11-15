#!/usr/bin/env python3
"""把跑批产出的 JSON 文件导入 MySQL。

设计原则（与跑批解耦）：
- 跑批只写文件，不连数据库；本脚本负责"文件 → 库"
- 幂等：按目录路径 + 内容哈希去重，重复跑不会产生重复行
- 库随时可以删掉重建：数据真源是 Tools/sim/output 下的文件

用法：
    # 只解析不写库（不需要 MySQL，用于检查数据结构）
    python Tools/db/ingest.py --dry-run

    # 真正入库（需要 pymysql 与可连的 MySQL）
    python Tools/db/ingest.py --dsn "mysql://user:pass@127.0.0.1:3306/elephant_crisis"

依赖：pip install pymysql（仅入库模式需要）
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from urllib.parse import urlparse

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_OUT = REPO_ROOT / "Tools" / "lab" / "output"


# --------------------------------------------------------------- 读取

def _hash_files(meta_path: Path, events_path: Path | None) -> str:
    h = hashlib.sha256()
    h.update(meta_path.read_bytes())
    if events_path and events_path.exists():
        h.update(events_path.read_bytes())
    return h.hexdigest()


def load_match(match_dir: Path) -> dict | None:
    """读一局的数据；缺 meta.json 视为无效。"""
    meta_path = match_dir / "meta.json"
    if not meta_path.exists():
        return None

    meta = json.loads(meta_path.read_text(encoding="utf-8"))
    events_path = match_dir / "events.jsonl"
    events = []
    if events_path.exists():
        for line in events_path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line:
                try:
                    events.append(json.loads(line))
                except json.JSONDecodeError:
                    continue

    return {
        "path": str(match_dir).replace("\\", "/"),
        "file_hash": _hash_files(meta_path, events_path),
        "meta": meta,
        "events": events,
    }


def iter_matches(out_dir: Path):
    for d in sorted(out_dir.glob("*/match_*")):
        if d.is_dir():
            data = load_match(d)
            if data:
                yield data


# --------------------------------------------------------------- 行映射

def to_match_row(m: dict) -> tuple:
    meta = m["meta"]
    return (
        meta.get("seed"),
        meta.get("winner"),
        meta.get("reason", "unknown"),
        meta.get("full_rounds", 0),
        meta.get("collect", "both"),
        meta.get("source", "unknown"),
        len(m["events"]) or meta.get("events", 0),
        json.dumps(meta.get("strategies"), ensure_ascii=False),
        json.dumps(meta.get("per_role"), ensure_ascii=False),
        m["path"],
        m["file_hash"],
    )


def to_role_rows(meta: dict) -> list[tuple]:
    rows = []
    for role, info in (meta.get("per_role") or {}).items():
        rows.append((role, 1 if info.get("survived") else 0, info.get("death_round")))
    return rows


def to_event_rows(events: list[dict]) -> list[tuple]:
    rows = []
    for e in events:
        rows.append((
            e.get("t"),
            e.get("type", "unknown"),
            e.get("actor"),
            e.get("target"),
            e.get("round"),
            json.dumps(e, ensure_ascii=False),
        ))
    return rows


# --------------------------------------------------------------- 入库

def connect(dsn: str):
    import pymysql  # 延迟导入：dry-run 不需要
    u = urlparse(dsn)
    return pymysql.connect(
        host=u.hostname or "127.0.0.1",
        port=u.port or 3306,
        user=u.username or "root",
        password=u.password or "",
        database=(u.path or "/elephant_crisis").lstrip("/"),
        charset="utf8mb4",
        autocommit=False,
    )


def ingest(out_dir: Path, dsn: str, batch_note: str | None, force: bool) -> dict:
    conn = connect(dsn)
    new = skip = 0
    try:
        with conn.cursor() as cur:
            cur.execute(
                "INSERT INTO import_batches (out_dir, note) VALUES (%s, %s)",
                (str(out_dir).replace("\\", "/"), batch_note),
            )
            batch_id = cur.lastrowid

            for m in iter_matches(out_dir):
                cur.execute("SELECT id, file_hash FROM matches WHERE path = %s", (m["path"],))
                existing = cur.fetchone()
                if existing and not force and existing[1] == m["file_hash"]:
                    skip += 1
                    continue

                if existing:
                    cur.execute("DELETE FROM matches WHERE id = %s", (existing[0],))

                cur.execute(
                    """INSERT INTO matches
                       (seed, winner, reason, full_rounds, collect, source, event_count,
                        strategies, per_role, path, file_hash)
                       VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)""",
                    to_match_row(m),
                )
                match_id = cur.lastrowid

                role_rows = [(match_id, *r) for r in to_role_rows(m["meta"])]
                if role_rows:
                    cur.executemany(
                        "INSERT INTO match_roles (match_id, role, survived, death_round) "
                        "VALUES (%s,%s,%s,%s)",
                        role_rows,
                    )

                event_rows = [(match_id, *r) for r in to_event_rows(m["events"])]
                if event_rows:
                    cur.executemany(
                        "INSERT INTO match_events (match_id, t, type, actor, target, round, payload) "
                        "VALUES (%s,%s,%s,%s,%s,%s,%s)",
                        event_rows,
                    )
                new += 1

            cur.execute(
                "UPDATE import_batches SET matches_new = %s, matches_skip = %s WHERE id = %s",
                (new, skip, batch_id),
            )
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()

    return {"new": new, "skip": skip}


def main() -> int:
    p = argparse.ArgumentParser(description="把跑批 JSON 导入 MySQL（可重复执行，幂等）")
    p.add_argument("--out-dir", default=str(DEFAULT_OUT), help="跑批输出目录（其下有 */match_*）")
    p.add_argument("--dsn", default=None, help="mysql://user:pass@host:port/db")
    p.add_argument("--dry-run", action="store_true", help="只解析并打印统计，不写库")
    p.add_argument("--force", action="store_true", help="即使内容未变也重新写入")
    p.add_argument("--note", default=None, help="本次导入的备注（写进 import_batches）")
    args = p.parse_args()

    out_dir = Path(args.out_dir)
    if not out_dir.exists():
        print(f"目录不存在：{out_dir}", file=sys.stderr)
        return 2

    if args.dry_run:
        total = events = roles = 0
        kinds: dict[str, int] = {}
        winners: dict[str, int] = {}
        for m in iter_matches(out_dir):
            total += 1
            events += len(m["events"])
            roles += len(m["meta"].get("per_role") or {})
            w = m["meta"].get("winner") or "none"
            winners[w] = winners.get(w, 0) + 1
            for e in m["events"]:
                kinds[e.get("type", "?")] = kinds.get(e.get("type", "?"), 0) + 1
        print(f"目录：{out_dir}")
        print(f"对局 {total} 局 / 事件 {events} 条 / 角色记录 {roles} 条")
        print(f"胜者分布：{winners}")
        top = sorted(kinds.items(), key=lambda kv: -kv[1])[:8]
        print(f"事件类型（前 8）：{top}")
        print("\n（dry-run：未连接数据库）")
        return 0

    if not args.dsn:
        print("入库需要 --dsn，例如：\n"
              "  --dsn \"mysql://root:pass@127.0.0.1:3306/elephant_crisis\"", file=sys.stderr)
        return 2

    result = ingest(out_dir, args.dsn, args.note, args.force)
    print(f"导入完成：新增 {result['new']} 局，跳过 {result['skip']} 局（已存在且未变）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
