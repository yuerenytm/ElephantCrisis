from __future__ import annotations

import sys
from pathlib import Path

import pandas as pd
import plotly.express as px
import streamlit as st

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
from pathsetup import SIM_ROOT, ensure_path  # noqa: E402

ensure_path()

from app.export_util import export_allure_results, export_anomaly_pdf  # noqa: E402
from app.pipeline_util import (  # noqa: E402
    load_json,
    run_baseline_cli,
    run_batch_subprocess,
    train_and_scan,
    write_anomaly_config,
)
from app.replay_util import (  # noqa: E402
    events_between,
    extract_snapshots,
    grid_figure,
    summarize_event,
    unit_table_rows,
)
from elephant_sim.log_io import iter_match_dirs, load_match  # noqa: E402

st.set_page_config(page_title="象群危机 · 逻辑 QA 工作台", layout="wide", page_icon="🐘")

RUNTIME = ROOT / "config" / "runtime"
OUTPUT = SIM_ROOT / "output"
REPORTS = ROOT / "reports"
MODELS = ROOT / "models"


def main() -> None:
    st.title("象群危机 · 可视化测试工作台")
    st.caption("Unity LogicSim 造数 → 基线校验 → 孤立森林扫描 → 回放与导出")

    tab_run, tab_dash, tab_replay, tab_export = st.tabs(
        ["① 配置与运行", "② 数据大盘", "③ 对局回放", "④ 导出报告"]
    )

    with tab_run:
        render_run_tab()
    with tab_dash:
        render_dashboard()
    with tab_replay:
        render_replay()
    with tab_export:
        render_export()


def render_run_tab() -> None:
    st.subheader("运行参数")
    st.caption(
        "造数走 Unity LogicSim（Game 启发式 AI + StreamingAssets/game_rules.yaml）。"
        "请先关闭已打开该 Game 工程的 Editor。"
    )
    c1, c2 = st.columns(2)
    with c1:
        matches = st.number_input("对局场数", min_value=1, max_value=2000, value=100, step=1)
        seed = st.number_input("起始 seed", min_value=1, value=1, step=1)
    with c2:
        contamination = st.slider(
            "异常 contamination（训练用）",
            0.005,
            0.15,
            0.02,
            0.005,
            help="越小越严；训练时会按该比例校准分数阈值",
        )
        do_train = st.checkbox("运行后重新训练 IF 模型", value=True)

    skip_batch = st.checkbox("跳过批量对局（仅用现有 output/ 做基线+异常）", value=False)

    if st.button("一键启动流水线", type="primary", use_container_width=True):
        RUNTIME.mkdir(parents=True, exist_ok=True)
        REPORTS.mkdir(parents=True, exist_ok=True)
        MODELS.mkdir(parents=True, exist_ok=True)

        anom_cfg = RUNTIME / "anomaly.yaml"
        write_anomaly_config(anom_cfg, contamination=float(contamination))

        progress = st.progress(0.0, text="准备中…")
        status = st.empty()

        try:
            if not skip_batch:
                status.write("正在批量对局（Unity LogicSim）…")

                def on_prog(p: float, cur: int, total: int) -> None:
                    progress.progress(min(0.55, p * 0.55), text=f"对局进度约 {cur}/{total}")

                rc = run_batch_subprocess(
                    matches=int(matches),
                    out_dir=OUTPUT,
                    seed=int(seed),
                    on_progress=on_prog,
                )
                if rc != 0:
                    st.error(f"批量对局进程退出码 {rc}")
                    return
            else:
                progress.progress(0.55, text="已跳过批量对局")

            status.write("基线校验…")
            progress.progress(0.7, text="基线校验中")
            base = run_baseline_cli(OUTPUT, REPORTS)
            st.session_state["baseline_report"] = base

            status.write("异常检测…")
            progress.progress(0.85, text="训练/扫描 Isolation Forest")
            anom = train_and_scan(OUTPUT, REPORTS, MODELS, anom_cfg, do_train=do_train)
            st.session_state["anomaly_report"] = anom

            progress.progress(1.0, text="完成")
            status.write("流水线完成")
            st.success(
                f"基线 failed={base.get('summary', {}).get('failed')} · "
                f"AI suspect={anom.get('summary', {}).get('ai_suspect')}"
            )
        except Exception as e:
            st.exception(e)


def _reports() -> tuple:
    base = st.session_state.get("baseline_report") or load_json(REPORTS / "baseline_report.json")
    anom = st.session_state.get("anomaly_report") or load_json(REPORTS / "anomaly_report.json")
    return base, anom


def _match_sort_key(match_id: str) -> tuple:
    """match_10 排在 match_2 之后（按数字，而非字符串）。"""
    name = str(match_id or "")
    if name.startswith("match_"):
        suffix = name[6:]
        if suffix.isdigit():
            return (0, int(suffix))
    return (1, name)


# 回放列表展示名与排序：baseline bug → AI suspected → passed
_STATUS_ORDER = ("baseline_bug", "ai_suspect", "passed")
_STATUS_LABEL = {
    "baseline_bug": "baseline bug",
    "ai_suspect": "AI suspected",
    "passed": "passed",
}


def _resolve_match_status(match_id: str, base: dict, anom: dict) -> str:
    """优先用 anomaly 报告标签；无异常报告时回退基线 pass。"""
    for m in anom.get("matches") or []:
        if m.get("match_id") != match_id:
            continue
        lab = m.get("label")
        if lab == "baseline_bug":
            return "baseline_bug"
        if lab == "ai_suspect":
            return "ai_suspect"
        if lab == "clean":
            return "passed"
        break
    for m in base.get("matches") or []:
        if m.get("match_id") != match_id:
            continue
        return "passed" if m.get("pass") else "baseline_bug"
    return "passed"


def _status_sort_key(match_id: str, status: str) -> tuple:
    try:
        rank = _STATUS_ORDER.index(status)
    except ValueError:
        rank = 99
    return (rank, _match_sort_key(match_id))


def render_dashboard() -> None:
    st.subheader("缺陷与对局概览")
    base, anom = _reports()
    if not base and not anom:
        st.warning("暂无报告。请先在「配置与运行」执行流水线，或确保 reports/ 下已有 JSON。")
        return

    bs = base.get("summary") or {}
    ans = anom.get("summary") or {}
    m1, m2, m3, m4 = st.columns(4)
    m1.metric("对局总数", bs.get("matches") or ans.get("matches") or 0)
    m2.metric("基线失败局", bs.get("failed", 0))
    m3.metric("基线违规条数", bs.get("total_violations", 0))
    m4.metric("AI 可疑局", ans.get("ai_suspect", 0))

    c1, c2 = st.columns(2)
    with c1:
        st.markdown("**基线规则分布**")
        by_rule = bs.get("by_rule") or {}
        if by_rule:
            df = pd.DataFrame(
                {"rule": list(by_rule.keys()), "count": list(by_rule.values())}
            ).sort_values("count", ascending=True)
            # 横向柱图：类别名在纵轴，始终水平可读，避免竖排截断
            fig = px.bar(df, x="count", y="rule", orientation="h", text="count")
            fig.update_layout(
                margin=dict(l=10, r=10, t=10, b=10),
                height=max(220, 48 * len(df)),
                xaxis_title="次数",
                yaxis_title="",
                showlegend=False,
            )
            fig.update_traces(textposition="outside", cliponaxis=False)
            st.plotly_chart(fig, use_container_width=True)
        else:
            st.info("无基线违规")
    with c2:
        st.markdown("**异常标签分布**")
        labels = {
            "clean": ans.get("clean", 0),
            "ai_suspect": ans.get("ai_suspect", 0),
            "baseline_bug": ans.get("baseline_bug", 0),
        }
        df2 = pd.DataFrame(
            {"label": list(labels.keys()), "count": list(labels.values())}
        ).sort_values("count", ascending=True)
        fig2 = px.bar(df2, x="count", y="label", orientation="h", text="count")
        fig2.update_layout(
            margin=dict(l=10, r=10, t=10, b=10),
            height=220,
            xaxis_title="局数",
            yaxis_title="",
            showlegend=False,
        )
        fig2.update_traces(textposition="outside", cliponaxis=False)
        st.plotly_chart(fig2, use_container_width=True)

    st.markdown("**对局明细**（按 baseline bug → AI suspected → passed）")
    rows = []
    base_map = {m["match_id"]: m for m in base.get("matches") or []}
    anom_map = {m["match_id"]: m for m in anom.get("matches") or []}
    ids = sorted(set(base_map) | set(anom_map), key=_match_sort_key)
    for mid in ids:
        b = base_map.get(mid, {})
        a = anom_map.get(mid, {})
        status = _resolve_match_status(mid, base or {}, anom or {})
        rows.append(
            {
                "status": _STATUS_LABEL[status],
                "match_id": mid,
                "baseline_pass": b.get("pass"),
                "violations": b.get("violation_count", 0),
                "anomaly_label": a.get("label"),
                "anomaly_count": a.get("anomaly_count"),
                "min_score": a.get("min_score"),
                "_status_key": status,
            }
        )
    rows.sort(key=lambda r: _status_sort_key(r["match_id"], r["_status_key"]))
    view = [{k: v for k, v in r.items() if k != "_status_key"} for r in rows]
    st.dataframe(pd.DataFrame(view), use_container_width=True, hide_index=True)


def render_replay() -> None:
    st.subheader("对局回放器")
    base, anom = _reports()

    match_dirs = list(iter_match_dirs(OUTPUT))
    if not match_dirs:
        st.warning("output/ 下没有对局，请先跑批量仿真。")
        return

    all_ids = [p.name for p in match_dirs]
    status_by_id = {mid: _resolve_match_status(mid, base or {}, anom or {}) for mid in all_ids}
    ordered = sorted(all_ids, key=lambda mid: _status_sort_key(mid, status_by_id[mid]))

    # selectbox 展示带标签；用 format_func 保持值为 match_id
    def _fmt(mid: str) -> str:
        return f"[{_STATUS_LABEL.get(status_by_id.get(mid, 'passed'), 'passed')}] {mid}"

    counts = {s: 0 for s in _STATUS_ORDER}
    for s in status_by_id.values():
        counts[s] = counts.get(s, 0) + 1
    st.caption(
        "排序：baseline bug → AI suspected → passed　|　"
        f"bug {counts.get('baseline_bug', 0)} · "
        f"suspect {counts.get('ai_suspect', 0)} · "
        f"passed {counts.get('passed', 0)}"
    )

    mid = st.selectbox(
        "选择对局",
        ordered,
        format_func=_fmt,
    )
    status = status_by_id.get(mid, "passed")
    st.markdown(f"**状态：** `{_STATUS_LABEL[status]}`")

    log = load_match(OUTPUT / mid)
    snaps = extract_snapshots(log.events)
    if not snaps:
        st.error("该局无 snapshot")
        return

    meta = log.meta or {}
    st.write(
        f"seed={meta.get('seed')} winner={meta.get('winner')} reason={meta.get('reason')} "
        f"strategies={meta.get('strategies')}"
    )

    # 异常帧快捷跳转
    anom_ts = []
    for m in anom.get("matches") or []:
        if m.get("match_id") == mid:
            anom_ts = [a.get("t") for a in m.get("anomalies") or []]
            break
    if anom_ts:
        st.caption(f"AI 异常 snapshot t 列表: {anom_ts[:20]}")

    idx = st.slider("Snapshot 序号", 0, len(snaps) - 1, 0)
    snap = snaps[idx]
    prev_t = snaps[idx - 1]["t"] if idx > 0 else None
    between = events_between(log.events, prev_t, snap["t"])

    col_a, col_b = st.columns([1.2, 1])
    with col_a:
        st.plotly_chart(grid_figure(snap), use_container_width=True)
    with col_b:
        st.markdown("**单位属性**")
        st.dataframe(pd.DataFrame(unit_table_rows(snap)), use_container_width=True, hide_index=True)
        st.markdown("**本段事件**")
        if between:
            st.code("\n".join(summarize_event(e) for e in between[-40:]), language="text")
        else:
            st.caption("无中间事件")

    # 基线违规定位
    for m in base.get("matches") or []:
        if m.get("match_id") != mid:
            continue
        viols = m.get("violations") or []
        if viols:
            with st.expander(f"基线违规（{len(viols)}）"):
                st.json(viols[:30])
        break

    st.download_button(
        "下载本局 events.jsonl",
        data=(OUTPUT / mid / "events.jsonl").read_bytes(),
        file_name=f"{mid}_events.jsonl",
    )


def render_export() -> None:
    st.subheader("导出产物")
    base, anom = _reports()
    if not base and not anom:
        st.warning("无报告可导出。")
        return

    if st.button("生成 Allure 结果 + PDF", type="primary"):
        allure_dir = export_allure_results(REPORTS, base or {}, anom or {})
        pdf_path = REPORTS / "anomaly_report.pdf"
        export_anomaly_pdf(pdf_path, base or {}, anom or {})
        st.session_state["export_allure"] = str(allure_dir)
        st.session_state["export_pdf"] = str(pdf_path)
        st.success("已生成")

    pdf_path = Path(st.session_state.get("export_pdf") or (REPORTS / "anomaly_report.pdf"))
    allure_dir = Path(st.session_state.get("export_allure") or (REPORTS / "allure-results"))

    if pdf_path.is_file():
        st.download_button(
            "下载 anomaly_report.pdf",
            data=pdf_path.read_bytes(),
            file_name="anomaly_report.pdf",
            mime="application/pdf",
        )
        st.caption(f"PDF: {pdf_path}")

    if allure_dir.is_dir():
        st.caption(f"Allure 原始结果目录: {allure_dir}")
        st.code(f"allure serve {allure_dir}", language="bash")
        st.caption("需本机安装 Allure CLI；未安装也可直接把结果目录交给 CI 归档。")

    st.markdown("**原始 JSON**")
    c1, c2 = st.columns(2)
    with c1:
        bp = REPORTS / "baseline_report.json"
        if bp.is_file():
            st.download_button("baseline_report.json", bp.read_bytes(), file_name="baseline_report.json")
    with c2:
        ap = REPORTS / "anomaly_report.json"
        if ap.is_file():
            st.download_button("anomaly_report.json", ap.read_bytes(), file_name="anomaly_report.json")


if __name__ == "__main__":
    main()
