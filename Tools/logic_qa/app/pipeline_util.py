from __future__ import annotations

import json
import subprocess
import sys
import time
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional

import yaml

ROOT = Path(__file__).resolve().parent.parent
REPO_ROOT = ROOT.parent
SIM_ROOT = REPO_ROOT / "sim"


def count_matches(out_dir: Path) -> int:
    if not out_dir.is_dir():
        return 0
    return sum(
        1
        for p in out_dir.iterdir()
        if p.is_dir() and p.name.startswith("match_") and (p / "events.jsonl").is_file()
    )


def write_anomaly_config(path: Path, contamination: float, base: Optional[Path] = None) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    cfg: Dict[str, Any] = {}
    src = base or (ROOT / "anomaly_analysis" / "config" / "anomaly.yaml")
    if src.is_file():
        with src.open("r", encoding="utf-8") as f:
            cfg = yaml.safe_load(f) or {}
    cfg.setdefault("isolation_forest", {})
    cfg["isolation_forest"]["contamination"] = float(contamination)
    cfg["score_threshold"] = None
    cfg.setdefault("features", {"use_hp_delta": True})
    with path.open("w", encoding="utf-8") as f:
        yaml.safe_dump(cfg, f, allow_unicode=True, sort_keys=False)


def run_batch_subprocess(
    *,
    matches: int,
    out_dir: Path,
    seed: int,
    on_progress: Optional[Callable[[float, int, int], None]] = None,
) -> int:
    out_dir.mkdir(parents=True, exist_ok=True)
    cmd: List[str] = [
        sys.executable,
        str(SIM_ROOT / "scripts" / "run_batch.py"),
        "--matches",
        str(matches),
        "--out",
        str(out_dir),
        "--seed",
        str(seed),
    ]

    proc = subprocess.Popen(cmd, cwd=str(SIM_ROOT))
    target = max(1, matches)
    while proc.poll() is None:
        cur = count_matches(out_dir)
        if on_progress:
            on_progress(min(0.99, cur / target), cur, target)
        time.sleep(0.4)
    if on_progress:
        on_progress(1.0, count_matches(out_dir), target)
    return int(proc.returncode or 0)


def run_baseline_cli(input_dir: Path, reports_dir: Path) -> Dict[str, Any]:
    from anomaly_analysis.baseline.runner import run_baseline

    return run_baseline(input_dir, reports_dir)


def train_and_scan(
    input_dir: Path,
    reports_dir: Path,
    model_dir: Path,
    anomaly_cfg: Path,
    do_train: bool = True,
) -> Dict[str, Any]:
    from anomaly_analysis.detection.runner import run_anomaly, train_anomaly_model

    br = reports_dir / "baseline_report.json"
    if do_train or not (model_dir / "iforest.joblib").is_file():
        train_anomaly_model(
            input_dir,
            model_dir,
            cfg_path=anomaly_cfg,
            baseline_report_path=br if br.is_file() else None,
        )
    return run_anomaly(
        input_dir,
        model_dir,
        reports_dir,
        baseline_report_path=br if br.is_file() else None,
    )


def load_json(path: Path) -> Dict[str, Any]:
    if not path.is_file():
        return {}
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)
