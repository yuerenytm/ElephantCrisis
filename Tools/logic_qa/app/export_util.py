from __future__ import annotations

import json
import time
import uuid
from pathlib import Path
from typing import Any, Dict, List, Optional


def export_allure_results(
    reports_dir: Path,
    baseline: Dict[str, Any],
    anomaly: Dict[str, Any],
) -> Path:
    """写出 Allure 原始结果（可用 allure serve 打开）。"""
    out = reports_dir / "allure-results"
    out.mkdir(parents=True, exist_ok=True)
    # 清理旧结果（仅本工具生成的）
    for p in out.glob("*-result.json"):
        p.unlink()

    now = int(time.time() * 1000)
    results: List[Dict[str, Any]] = []

    for m in baseline.get("matches") or []:
        mid = m.get("match_id", "?")
        viols = m.get("violations") or []
        if not viols:
            results.append(
                _allure_case(
                    name=f"[baseline] {mid} PASS",
                    status="passed",
                    suite="baseline",
                    message="no violations",
                    start=now,
                )
            )
            continue
        for v in viols[:50]:  # 单局上限，避免爆炸
            results.append(
                _allure_case(
                    name=f"[baseline] {mid} {v.get('rule_id')}",
                    status="failed",
                    suite="baseline",
                    message=v.get("message", ""),
                    start=now,
                    details=json.dumps(v, ensure_ascii=False),
                )
            )

    for m in anomaly.get("matches") or []:
        mid = m.get("match_id", "?")
        label = m.get("label", "clean")
        status = "passed" if label == "clean" else "broken" if label == "ai_suspect" else "failed"
        msg = f"label={label} anomaly_count={m.get('anomaly_count')} min_score={m.get('min_score')}"
        results.append(
            _allure_case(
                name=f"[anomaly] {mid} {label}",
                status=status,
                suite="anomaly",
                message=msg,
                start=now,
                details=json.dumps(
                    {"anomalies": (m.get("anomalies") or [])[:5]},
                    ensure_ascii=False,
                ),
            )
        )

    for r in results:
        path = out / f"{r['uuid']}-result.json"
        with path.open("w", encoding="utf-8") as f:
            json.dump(r, f, ensure_ascii=False)

    # 环境信息
    with (out / "environment.properties").open("w", encoding="utf-8") as f:
        f.write("Project=ElephantCrisis-sim\n")
        f.write("Module=QA-Workbench\n")

    return out


def _allure_case(
    *,
    name: str,
    status: str,
    suite: str,
    message: str,
    start: int,
    details: str = "",
) -> Dict[str, Any]:
    uid = str(uuid.uuid4())
    return {
        "uuid": uid,
        "historyId": uid,
        "name": name,
        "status": status,
        "statusDetails": {"message": message, "trace": details},
        "stage": "finished",
        "start": start,
        "stop": start + 1,
        "labels": [
            {"name": "suite", "value": suite},
            {"name": "framework", "value": "elephant-sim"},
            {"name": "language", "value": "python"},
        ],
    }


def export_anomaly_pdf(
    path: Path,
    baseline: Dict[str, Any],
    anomaly: Dict[str, Any],
) -> Path:
    from fpdf import FPDF

    path.parent.mkdir(parents=True, exist_ok=True)
    pdf = FPDF()
    pdf.set_auto_page_break(auto=True, margin=15)
    pdf.add_page()
    pdf.set_font("Helvetica", "B", 16)
    _pdf_line(pdf, "ElephantCrisis AI Anomaly Report", size=16, bold=True)

    bs = baseline.get("summary") or {}
    ans = anomaly.get("summary") or {}
    for line in [
        f"Baseline matches: {bs.get('matches', 0)}  passed={bs.get('passed', 0)}  failed={bs.get('failed', 0)}",
        f"Baseline violations: {bs.get('total_violations', 0)}",
        f"By rule: {json.dumps(bs.get('by_rule') or {}, ensure_ascii=True)}",
        "",
        f"Anomaly matches: {ans.get('matches', 0)}",
        f"ai_suspect={ans.get('ai_suspect', 0)}  baseline_bug={ans.get('baseline_bug', 0)}  clean={ans.get('clean', 0)}",
        f"score_threshold={ans.get('score_threshold')}",
        "",
        "--- Suspect matches ---",
    ]:
        _pdf_line(pdf, line)

    suspects = [m for m in (anomaly.get("matches") or []) if m.get("label") == "ai_suspect"]
    for m in suspects[:30]:
        _pdf_line(
            pdf,
            f"{m.get('match_id')}  anomalies={m.get('anomaly_count')} min={m.get('min_score')}",
            bold=True,
        )
        for a in (m.get("anomalies") or [])[:3]:
            feats = ", ".join(
                f"{x.get('feature')}(z={float(x.get('z', 0)):.2f})"
                for x in (a.get("top_features") or [])[:3]
            )
            score = a.get("score")
            score_s = f"{float(score):.3f}" if score is not None else "?"
            _pdf_line(pdf, f"  t={a.get('t')} round={a.get('round')} score={score_s} | {feats}", size=10)

    if bs.get("failed"):
        pdf.add_page()
        _pdf_line(pdf, "Baseline failures (sample)", size=14, bold=True)
        n = 0
        for m in baseline.get("matches") or []:
            if m.get("pass"):
                continue
            for v in (m.get("violations") or [])[:5]:
                _pdf_line(
                    pdf,
                    f"{m.get('match_id')} [{v.get('rule_id')}] t={v.get('t')}: {v.get('message')}",
                    size=10,
                )
                n += 1
                if n >= 80:
                    break
            if n >= 80:
                break

    pdf.output(str(path))
    return path


def _pdf_line(pdf, text: str, size: int = 11, bold: bool = False) -> None:
    pdf.set_x(pdf.l_margin)
    pdf.set_font("Helvetica", "B" if bold else "", size)
    pdf.multi_cell(pdf.epw, 6, _latin(text))


def _latin(s: str) -> str:
    """FPDF 核心字体仅 Latin-1；中文等替换为近似 ASCII。"""
    return (
        s.replace("→", "->")
        .encode("latin-1", "replace")
        .decode("latin-1")
    )
