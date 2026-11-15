"""一次可复现的实验 = 种子集 + 配置 + 采集 + 标签。

为什么要有它：判定结论只有"可比"才有意义。
manifest.json 记下配置指纹与代码版本，半年后回看才知道那批数据是哪一版跑出来的。
"""
from __future__ import annotations

import hashlib
import json
import subprocess
from datetime import datetime
from pathlib import Path

from .runner import REPO_ROOT

CONFIG_DIR = REPO_ROOT / "Game" / "Assets" / "StreamingAssets" / "Config"
RULES_JSON = CONFIG_DIR / "game_rules.json"



def _config_fingerprint() -> str:
    """配置文件的 sha256 前 12 位；缺失时返回 'none'。"""
    if not RULES_JSON.exists():
        return "none"
    return hashlib.sha256(RULES_JSON.read_bytes()).hexdigest()[:12]


def _code_revision() -> str:
    """当前 git 提交短哈希；不是仓库时返回 'unknown'。"""
    try:
        out = subprocess.run(["git", "rev-parse", "--short", "HEAD"],
                             cwd=str(REPO_ROOT), capture_output=True, text=True, timeout=5)
        return out.stdout.strip() or "unknown"
    except Exception:
        return "unknown"
