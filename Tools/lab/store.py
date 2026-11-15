"""对局数据的唯一存储：数据库（SQLite 或 MySQL）。

设计取舍：
- 跑批器（C#）仍然产出文件，但那些文件只是**传输管道**，
  lab/run.py 导入后立即删除，不留下任何持久目录。
- qa / balance 不再读目录，一律通过本模块查询。

支持两种后端，方言差异只体现在建表语句里：
- sqlite:///path/to.db      （Python 自带，零安装，适合本地/CI）
- mysql://user:pass@host:port/db（生产用，需要 pip install pymysql）
"""
from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional
from urllib.parse import urlparse

# 建表语句按方言替换两处：{pk} 主键自增写法、{json} JSON 列类型
_SCHEMA = [
    """CREATE TABLE IF NOT EXISTS experiments (
        id {pk},
        name {text} NOT NULL,
        created_at {text} NOT NULL,
        matches INT NOT NULL DEFAULT 0,
        seed_start INT,
        seed_end INT,
        collect {text},
        max_rounds INT,
        config_fingerprint {text},
        code_revision {text},
        path {text}
    )""",
    """CREATE TABLE IF NOT EXISTS matches (
        id {pk},
        experiment_id INT NOT NULL,
        seed INT NOT NULL,
        winner {text},
        reason {text},
        full_rounds INT,
        event_count INT NOT NULL DEFAULT 0,
        meta {json},
        UNIQUE (experiment_id, seed)
    )""",
    """CREATE TABLE IF NOT EXISTS match_events (
        id {pk},
        match_id INT NOT NULL,
        t INT NOT NULL,
        type {text} NOT NULL,
        actor {text},
        target {text},
        payload {json} NOT NULL
    )""",
    """CREATE TABLE IF NOT EXISTS match_roles (
        id {pk},
        match_id INT NOT NULL,
        role {text} NOT NULL,
        survived INT NOT NULL,
        death_round INT
    )""",
]

_INDEXES = [
    "CREATE INDEX IF NOT EXISTS idx_matches_experiment ON matches (experiment_id)",
    "CREATE INDEX IF NOT EXISTS idx_events_match_t ON match_events (match_id, t)",
    "CREATE INDEX IF NOT EXISTS idx_events_type ON match_events (type)",
    "CREATE INDEX IF NOT EXISTS idx_roles_match ON match_roles (match_id)",
]


@dataclass
class MatchRecord:
    """一局的数据，字段与 lab.events.MatchLog 对齐，便于两边共用。"""

    match_id: str
    seed: int
    meta: Dict[str, Any]
    events: List[Dict[str, Any]]
    path: str = ""


class Store:
    """数据库访问门面。构造时按 DSN 选方言，ensure_schema() 幂等建表。"""

    def __init__(self, dsn: str):
        self.dsn = dsn
        parsed = urlparse(dsn)
        self.dialect = "sqlite" if parsed.scheme in ("sqlite", "") else "mysql"
        self._conn = None
        self._parsed = parsed

    # ------------------------------------------------------------ 连接

    @property
    def conn(self):
        if self._conn is not None:
            return self._conn
        if self.dialect == "sqlite":
            import sqlite3

            # sqlite:///D:/path → urlparse 给出 '/D:/path'，Windows 上要去掉前导斜杠
            path = self._parsed.path or self._parsed.netloc
            if len(path) > 2 and path[0] == "/" and path[2] == ":":
                path = path[1:]
            if path in ("", ":memory:"):
                path = ":memory:"
            else:
                Path(path).parent.mkdir(parents=True, exist_ok=True)
            self._conn = sqlite3.connect(path)
            self._conn.row_factory = sqlite3.Row
        else:
            import pymysql

            self._conn = pymysql.connect(
                host=self._parsed.hostname or "127.0.0.1",
                port=self._parsed.port or 3306,
                user=self._parsed.username or "root",
                password=self._parsed.password or "",
                database=(self._parsed.path or "/elephant_crisis").lstrip("/"),
                charset="utf8mb4",
                autocommit=False,
            )
        return self._conn

    def _sql(self, template: str) -> str:
        return template.format(
            pk="INTEGER PRIMARY KEY AUTOINCREMENT" if self.dialect == "sqlite"
            else "BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY",
            text="TEXT" if self.dialect == "sqlite" else "VARCHAR(512)",
            json="TEXT" if self.dialect == "sqlite" else "JSON",
        )

    def ensure_schema(self) -> None:
        cur = self.conn.cursor()
        for ddl in _SCHEMA:
            cur.execute(self._sql(ddl))
        for idx in _INDEXES:
            try:
                cur.execute(idx)
            except Exception:
                pass  # MySQL 的 IF NOT EXISTS 索引写法不同，失败不致命
        self.conn.commit()

    # ------------------------------------------------------------ 写入

    def create_experiment(self, name: str, manifest: Dict[str, Any]) -> int:
        cur = self.conn.cursor()
        ph = "?" if self.dialect == "sqlite" else "%s"
        cur.execute(
            f"""INSERT INTO experiments
                (name, created_at, matches, seed_start, seed_end, collect, max_rounds,
                 config_fingerprint, code_revision, path)
                VALUES ({ph},{ph},{ph},{ph},{ph},{ph},{ph},{ph},{ph},{ph})""",
            (
                name,
                manifest.get("created_at") or datetime.now().isoformat(timespec="seconds"),
                int(manifest.get("matches") or 0),
                manifest.get("seed_start"),
                manifest.get("seed_end"),
                manifest.get("collect"),
                manifest.get("max_rounds"),
                manifest.get("config_fingerprint"),
                manifest.get("code_revision"),
                manifest.get("path"),
            ),
        )
        self.conn.commit()
        return int(cur.lastrowid)

    def add_match(self, experiment_id: int, meta: Dict[str, Any],
                  events: Iterable[Dict[str, Any]]) -> int:
        cur = self.conn.cursor()
        ph = "?" if self.dialect == "sqlite" else "%s"
        events = list(events)
        cur.execute(
            f"""INSERT INTO matches
                (experiment_id, seed, winner, reason, full_rounds, event_count, meta)
                VALUES ({ph},{ph},{ph},{ph},{ph},{ph},{ph})""",
            (
                experiment_id,
                int(meta.get("seed") or 0),
                meta.get("winner"),
                meta.get("reason", "unknown"),
                meta.get("full_rounds", 0),
                len(events),
                json.dumps(meta, ensure_ascii=False),
            ),
        )
        match_id = int(cur.lastrowid)

        if events:
            cur.executemany(
                f"INSERT INTO match_events (match_id, t, type, actor, target, payload) "
                f"VALUES ({ph},{ph},{ph},{ph},{ph},{ph})",
                [
                    (match_id, e.get("t"), e.get("type", "unknown"),
                     e.get("actor"), e.get("target"), json.dumps(e, ensure_ascii=False))
                    for e in events
                ],
            )

        role_rows = []
        for role, info in (meta.get("per_role") or {}).items():
            role_rows.append((match_id, role, 1 if info.get("survived") else 0,
                              info.get("death_round")))
        if role_rows:
            cur.executemany(
                f"INSERT INTO match_roles (match_id, role, survived, death_round) "
                f"VALUES ({ph},{ph},{ph},{ph})",
                role_rows,
            )
        return match_id

    def commit(self) -> None:
        self.conn.commit()

    def close(self) -> None:
        if self._conn is not None:
            self._conn.close()
            self._conn = None

    # ------------------------------------------------------------ 查询

    def _rows(self, sql: str, params: tuple = ()) -> List[Dict[str, Any]]:
        cur = self.conn.cursor()
        ph = "?" if self.dialect == "sqlite" else "%s"
        cur.execute(sql.replace("?", ph), params)
        rows = cur.fetchall()
        if self.dialect == "sqlite":
            return [dict(r) for r in rows]
        cols = [d[0] for d in cur.description]
        return [dict(zip(cols, r)) for r in rows]

    def list_experiments(self) -> List[Dict[str, Any]]:
        return self._rows("SELECT * FROM experiments ORDER BY id")

    def resolve_experiment(self, name: str) -> Optional[Dict[str, Any]]:
        rows = self._rows("SELECT * FROM experiments WHERE name = ? ORDER BY id DESC LIMIT 1", (name,))
        return rows[0] if rows else None

    def load_matches(self, experiment_name: str, with_events: bool = True) -> List[MatchRecord]:
        exp = self.resolve_experiment(experiment_name)
        if exp is None:
            raise KeyError(f"数据库里没有实验：{experiment_name}")
        matches = self._rows("SELECT * FROM matches WHERE experiment_id = ? ORDER BY seed",
                             (exp["id"],))
        out: List[MatchRecord] = []
        for m in matches:
            events: List[Dict[str, Any]] = []
            if with_events:
                rows = self._rows(
                    "SELECT payload FROM match_events WHERE match_id = ? ORDER BY t", (m["id"],))
                events = [json.loads(r["payload"]) for r in rows]
            out.append(MatchRecord(
                match_id=f"match_{m['seed']}",
                seed=m["seed"],
                meta=json.loads(m["meta"]) if m["meta"] else {},
                events=events,
            ))
        return out

    def load_metas(self, experiment_name: str) -> List[Dict[str, Any]]:
        return [m.meta for m in self.load_matches(experiment_name, with_events=False)]
