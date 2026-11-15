#!/usr/bin/env python3
"""跑批入口：跑一批对局，结果**只进数据库**。

    # 本地/CI：SQLite（零安装），默认落在 Tools/lab/ec.db
    python Tools/lab/run.py --name base_v1 --matches 100 --seed 50000

    # 生产：MySQL
    python Tools/lab/run.py --name base_v1 --dsn "mysql://user:pw@127.0.0.1/elephant_crisis"

跑批器（C#）仍然写文件，但那些文件只是传输管道：导入后立即删除，
磁盘上不留 output/ 目录。
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
import tempfile
from collections import Counter
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "Tools"))

from lab.events import iter_match_dirs, load_match  # noqa: E402
from lab.experiment import _code_revision, _config_fingerprint  # noqa: E402
from lab.runner import run_batch  # noqa: E402
from lab.store import Store  # noqa: E402

DEFAULT_DSN = f"sqlite:///{(REPO_ROOT / 'Tools' / 'lab' / 'ec.db').as_posix()}"


def _import_matches(store: Store, experiment_id: int, data_dir: Path):
    count = 0
    winners: Counter = Counter()
    for d in iter_match_dirs(data_dir):
        try:
            log = load_match(d)
        except Exception:
            continue
        store.add_match(experiment_id, log.meta, log.events)
        winners[log.meta.get("winner") or "none"] += 1
        count += 1
    store.commit()
    return count, winners


def main() -> int:
    p = argparse.ArgumentParser(description="跑一批对局，结果写入数据库")
    p.add_argument("--name", required=True, help="实验名（数据库内唯一）")
    p.add_argument("--matches", type=int, default=100)
    p.add_argument("--seed", type=int, default=50000)
    p.add_argument("--max-rounds", type=int, default=100)
    p.add_argument("--collect", default="both", choices=["balance", "logic", "both"])
    p.add_argument("--dsn", default=None, help="数据库连接串（默认本地 SQLite）")
    p.add_argument("--keep-files", action="store_true",
                   help="保留中间文件用于排查（默认导入后删除）")
    args = p.parse_args()

    dsn = args.dsn or DEFAULT_DSN
    store = Store(dsn)
    store.ensure_schema()

    if store.resolve_experiment(args.name):
        print(f"[lab] 实验名已存在：{args.name}，请换个名字")
        return 2

    tmp = Path(tempfile.mkdtemp(prefix="ec_batch_")) if not args.keep_files \
        else (REPO_ROOT / "Tools" / "lab" / "_tmp" / args.name)

    try:
        code = run_batch(tmp, matches=args.matches, seed=args.seed,
                         max_rounds=args.max_rounds, collect=args.collect, clean=True)
        if code != 0:
            print(f"[lab] 跑批失败，退出码 {code}")
            return code

        manifest = {
            "matches": args.matches,
            "seed_start": args.seed,
            "seed_end": args.seed + args.matches - 1,
            "collect": args.collect,
            "max_rounds": args.max_rounds,
            "config_fingerprint": _config_fingerprint(),
            "code_revision": _code_revision(),
            "path": f"db:{args.name}",
        }
        experiment_id = store.create_experiment(args.name, manifest)
        count, winners = _import_matches(store, experiment_id, tmp)

        print(json.dumps({
            "experiment": args.name,
            "matches": count,
            "winners": dict(winners),
            "config_fingerprint": manifest["config_fingerprint"],
            "code_revision": manifest["code_revision"],
            "backend": "sqlite" if dsn.startswith("sqlite") else "mysql",
        }, ensure_ascii=False, indent=2))
        return 0
    finally:
        if not args.keep_files:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
