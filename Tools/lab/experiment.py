"""一次可复现的实验 = 种子集 + 配置 + 采集 + 标签。

为什么要有它：判定结论只有"可比"才有意义。
manifest.json 记下配置指纹与代码版本，半年后回看才知道那批数据是哪一版跑出来的。
"""
from __future__ import annotations

import hashlib
import json
import subprocess
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import List

from .runner import REPO_ROOT, run_batch

CONFIG_DIR = REPO_ROOT / "Game" / "Assets" / "StreamingAssets" / "Config"
RULES_JSON = CONFIG_DIR / "game_rules.json"
DEFAULT_OUTPUT = REPO_ROOT / "Tools" / "lab" / "output"


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


@dataclass
class Experiment:
    """一批对局的描述与执行结果。"""

    name: str
    matches: int = 100
    seed: int = 50000
    collect: str = "both"
    max_rounds: int = 100
    out_dir: Path | None = None
    _manifest: dict = field(default_factory=dict, init=False, repr=False)

    @property
    def dir(self) -> Path:
        return self.out_dir or (DEFAULT_OUTPUT / self.name)

    def run(self, *, clean: bool = True) -> int:
        """跑这一批，并写出 manifest.json。"""
        code = run_batch(self.dir, matches=self.matches, seed=self.seed,
                         max_rounds=self.max_rounds, collect=self.collect, clean=clean)
        self._manifest = {
            "experiment": self.name,
            "matches": self.matches,
            "seed_start": self.seed,
            "seed_end": self.seed + self.matches - 1,
            "collect": self.collect,
            "max_rounds": self.max_rounds,
            "config_fingerprint": _config_fingerprint(),
            "code_revision": _code_revision(),
            "created_at": datetime.now().isoformat(timespec="seconds"),
            "exit_code": code,
            "path": str(self.dir).replace("\\", "/"),
        }
        self.write_manifest()
        return code

    def write_manifest(self) -> None:
        self.dir.mkdir(parents=True, exist_ok=True)
        with (self.dir / "manifest.json").open("w", encoding="utf-8") as f:
            json.dump(self._manifest, f, ensure_ascii=False, indent=2)

    def seeds(self) -> List[int]:
        return list(range(self.seed, self.seed + self.matches))
