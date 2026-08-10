#!/usr/bin/env python3
"""说明如何从 Unity persistentDataPath 取得最新 session（Windows 常见路径提示）。"""
from __future__ import annotations

import argparse
import os
import shutil
from pathlib import Path


def main() -> int:
    p = argparse.ArgumentParser(description="拷贝最新 ElephantPerf session 到 perf_analysis/samples")
    p.add_argument(
        "--from",
        dest="src_root",
        type=str,
        default=None,
        help="ElephantPerf 根目录；默认尝试 %%USERPROFILE%%/AppData/LocalLow/<Company>/<Product>/ElephantPerf",
    )
    p.add_argument("--out", type=str, default=None)
    args = p.parse_args()

    root = Path(__file__).resolve().parent.parent
    out = Path(args.out) if args.out else root / "samples"
    out.mkdir(parents=True, exist_ok=True)

    src_root = Path(args.src_root) if args.src_root else _guess_elephant_perf()
    if src_root is None or not src_root.is_dir():
        print("未找到 ElephantPerf 目录。请在 Unity Console 查看 [ClientPerf] session started → 路径，")
        print("然后: python tools/pull_latest.py --from \"<该目录的父级 ElephantPerf>\"")
        return 1

    sessions = sorted(
        [d for d in src_root.iterdir() if d.is_dir() and d.name.startswith("session_")],
        key=lambda d: d.stat().st_mtime,
        reverse=True,
    )
    if not sessions:
        print(f"无 session_*：{src_root}")
        return 1

    latest = sessions[0]
    dest = out / latest.name
    if dest.exists():
        shutil.rmtree(dest)
    shutil.copytree(latest, dest)
    print(f"Copied {latest} → {dest}")
    print(f"Analyze: python tools/analyze_client_perf.py --input {dest}")
    return 0


def _guess_elephant_perf():
    home = Path.home()
    # Unity 默认 LocalLow：CompanyName/ProductName — 本项目常为 DefaultCompany / 产品名
    base = home / "AppData" / "LocalLow"
    if not base.is_dir():
        return None
    candidates = list(base.glob("*/**/ElephantPerf"))
    # 也扫一层
    for company in base.iterdir():
        if not company.is_dir():
            continue
        for prod in company.iterdir():
            ep = prod / "ElephantPerf"
            if ep.is_dir():
                candidates.append(ep)
    return max(candidates, key=lambda p: p.stat().st_mtime) if candidates else None


if __name__ == "__main__":
    raise SystemExit(main())
