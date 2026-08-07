from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, List, Optional, Set

from anomaly_analysis.baseline.runner import load_baseline_report
from elephant_sim.log_io import iter_match_dirs, load_match
from elephant_sim.models import load_yaml
from .detector import AnomalyDetector, score_match, train_from_matches


def _default_anomaly_cfg() -> Path:
    return Path(__file__).resolve().parent.parent / "config" / "anomaly.yaml"


def train_anomaly_model(
    input_dir: Path,
    model_dir: Path,
    cfg_path: Optional[Path] = None,
    baseline_report_path: Optional[Path] = None,
    only_baseline_pass: bool = True,
) -> Dict[str, Any]:
    cfg = load_yaml(str(cfg_path or _default_anomaly_cfg()))
    failed_ids: Set[str] = set()
    if only_baseline_pass:
        br = load_baseline_report(baseline_report_path) if baseline_report_path else None
        if br is None:
            # 现场跑一遍基线过滤
            from anomaly_analysis.baseline.runner import run_baseline

            tmp = model_dir / "_baseline_for_train"
            br = run_baseline(input_dir, tmp)
        for m in br.get("matches") or []:
            if not m.get("pass", True):
                failed_ids.add(m["match_id"])

    pairs = []
    used = []
    skipped = []
    for d in iter_match_dirs(input_dir):
        log = load_match(d)
        if log.match_id in failed_ids:
            skipped.append(log.match_id)
            continue
        pairs.append((log.match_id, log.events))
        used.append(log.match_id)

    if not pairs:
        raise RuntimeError("没有可用于训练的干净对局（基线通过）")

    det, n_rows = train_from_matches(pairs, cfg)
    det.save(model_dir)
    summary = {
        "matches_used": len(used),
        "matches_skipped_baseline_fail": len(skipped),
        "snapshot_rows": n_rows,
        "model_dir": str(model_dir),
        "score_threshold": det.score_threshold,
        "n_features": len(det.feature_names),
    }
    with (model_dir / "train_summary.json").open("w", encoding="utf-8") as f:
        json.dump(summary, f, ensure_ascii=False, indent=2)
    return summary


def run_anomaly(
    input_dir: Path,
    model_dir: Path,
    out_dir: Path,
    baseline_report_path: Optional[Path] = None,
) -> Dict[str, Any]:
    det = AnomalyDetector.load(model_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    baseline_failed: Set[str] = set()
    br = load_baseline_report(baseline_report_path) if baseline_report_path else None
    if br is None:
        cand = out_dir / "baseline_report.json"
        if not cand.is_file():
            cand = input_dir.parent / "reports" / "baseline_report.json"
        br = load_baseline_report(cand)
    if br:
        for m in br.get("matches") or []:
            if not m.get("pass", True):
                baseline_failed.add(m["match_id"])

    match_reports: List[Dict[str, Any]] = []
    suspect = 0
    baseline_bug = 0
    for d in iter_match_dirs(input_dir):
        log = load_match(d)
        scored = score_match(det, log.events, log.match_id)
        if log.match_id in baseline_failed:
            label = "baseline_bug"
            baseline_bug += 1
        elif scored["anomaly_count"] > 0:
            label = "ai_suspect"
            suspect += 1
        else:
            label = "clean"
        scored["label"] = label
        scored["path"] = str(d)
        match_reports.append(scored)

    summary = {
        "matches": len(match_reports),
        "ai_suspect": suspect,
        "baseline_bug": baseline_bug,
        "clean": sum(1 for m in match_reports if m["label"] == "clean"),
        "score_threshold": det.score_threshold,
        "model_dir": str(model_dir),
    }
    report = {"summary": summary, "matches": match_reports}
    with (out_dir / "anomaly_report.json").open("w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    return report
