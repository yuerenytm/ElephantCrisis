from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, Optional

import pandas as pd
import streamlit as st

ROOT = Path(__file__).resolve().parent.parent
TOOLS = ROOT.parent
REPO = TOOLS.parent
SIM = TOOLS / "sim"
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
if str(SIM) not in sys.path:
    sys.path.insert(0, str(SIM))

from llm_client import chat_completion, load_advisor_config, resolve_api_settings  # noqa: E402
from preprocess import (  # noqa: E402
    build_context,
    context_to_prompt,
    load_json,
    write_context_artifacts,
)

st.set_page_config(page_title="象群危机 · 数值平衡顾问", layout="wide", page_icon="⚖️")

DEFAULT_REPORT = SIM / "reports" / "balance_report.json"
DEFAULT_SIM_OUT = SIM / "output"
DEFAULT_ADV_OUT = ROOT / "reports"


def main() -> None:
    st.title("数值平衡顾问")
    st.caption(
        "仿真统计 → 预处理（规则+指标）→ LLM 调参建议。"
        "默认不喂原始 events.jsonl。"
    )

    cfg = load_advisor_config(ROOT / "config" / "advisor.yaml")
    api = resolve_api_settings(cfg)

    with st.sidebar:
        st.header("路径与 API")
        report_path = Path(
            st.text_input("平衡报告", value=str(DEFAULT_REPORT))
        )
        sim_out = Path(st.text_input("仿真输出目录", value=str(DEFAULT_SIM_OUT)))
        adv_out = Path(st.text_input("顾问输出目录", value=str(DEFAULT_ADV_OUT)))
        st.divider()
        key_ok = bool(api.get("api_key"))
        st.write(f"模型：`{api.get('model')}`")
        st.write(f"端点：`{api.get('base_url')}`")
        st.write("API Key：" + ("已配置" if key_ok else "未配置（见 config/advisor.yaml）"))
        if key_ok:
            masked = str(api["api_key"])
            st.code((masked[:6] + "…" + masked[-4:]) if len(masked) > 12 else "***")
        st.caption("环境变量优先于 yaml。")

    tab_stats, tab_advice, tab_prompt = st.tabs(
        ["① 统计", "② LLM 建议", "③ Prompt 预览"]
    )
    with tab_stats:
        render_stats_tab(report_path, sim_out)
    with tab_advice:
        render_advice_tab(report_path, adv_out, cfg, api)
    with tab_prompt:
        render_prompt_tab(adv_out)


def render_stats_tab(report_path: Path, sim_out: Path) -> None:
    st.subheader("生成 / 加载平衡统计")
    st.caption("仿真走 Unity LogicSim；请先关闭已打开该 Game 工程的 Editor。")
    c1, c2 = st.columns(2)
    with c1:
        matches = st.number_input("仿真场数", min_value=0, max_value=2000, value=100, step=10)
        st.caption("0 = 不重新仿真，只统计已有日志或直接读报告")
    with c2:
        seed = st.number_input("起始 seed", min_value=1, value=1, step=1)

    b1, b2 = st.columns(2)
    run_clicked = b1.button("运行仿真 + 统计", type="primary", use_container_width=True)
    load_clicked = b2.button("仅加载已有报告", use_container_width=True)

    if run_clicked:
        with st.spinner("正在运行 sim/scripts/run_balance.py …"):
            cmd = [
                sys.executable,
                str(SIM / "scripts" / "run_balance.py"),
                "--input",
                str(sim_out),
                "--out",
                str(report_path.parent),
                "--seed",
                str(int(seed)),
            ]
            if matches and matches > 0:
                cmd.extend(["--matches", str(int(matches))])
            proc = subprocess.run(cmd, cwd=str(SIM), capture_output=True, text=True)
            if proc.returncode != 0:
                st.error("统计失败")
                st.code(proc.stderr or proc.stdout or "(no output)")
            else:
                st.success("完成")
                if proc.stdout:
                    st.code(proc.stdout[-2000:])
                st.session_state["balance_report"] = _safe_load(report_path.parent / "balance_report.json")

    if load_clicked or "balance_report" not in st.session_state:
        loaded = _safe_load(report_path)
        if loaded:
            st.session_state["balance_report"] = loaded
        elif load_clicked:
            st.warning(f"找不到报告：{report_path}")

    report = st.session_state.get("balance_report")
    if not report:
        st.info("尚无报告。请先运行统计或确认路径。")
        return

    summary = report.get("summary") or {}
    m1, m2, m3 = st.columns(3)
    m1.metric("对局数", summary.get("matches"))
    m2.metric("平均回合", summary.get("avg_full_rounds"))
    m3.metric("终局原因", len(summary.get("end_reasons") or {}))
    st.write("终局原因分布", summary.get("end_reasons") or {})
    st.caption(summary.get("disclaimer") or "")

    df = _role_frame(report)
    st.dataframe(df, use_container_width=True)
    if not df.empty:
        chart_df = df.set_index("role")[["win_rate", "death_rate"]]
        st.bar_chart(chart_df)


def render_advice_tab(
    report_path: Path,
    adv_out: Path,
    cfg: Dict[str, Any],
    api: Dict[str, Any],
) -> None:
    st.subheader("预处理并请求 LLM 建议")
    report = st.session_state.get("balance_report") or _safe_load(report_path)
    if not report:
        st.warning("请先在「统计」页加载或生成 balance_report。")
        return

    dry = st.checkbox("仅预处理（不调 API）", value=False)
    if st.button("生成建议", type="primary"):
        with st.spinner("预处理中…" if dry else "调用 API 中…"):
            try:
                context = build_context(report, cfg=cfg)
                prompt = context_to_prompt(context)
                paths = write_context_artifacts(context, prompt, adv_out)
                st.session_state["advisor_prompt"] = prompt
                st.session_state["advisor_context"] = context
                st.write(f"已写入 `{paths['prompt']}`（约 {paths['prompt'].stat().st_size // 1024} KB）")
                if dry:
                    st.success("dry-run 完成，请到「Prompt 预览」查看。")
                else:
                    if not api.get("api_key"):
                        st.error("未配置 API Key。")
                        return
                    text = chat_completion(prompt, cfg=cfg)
                    md_path = adv_out / "advisor_suggestions.md"
                    md_path.write_text(text, encoding="utf-8")
                    meta = {
                        "balance_report": str(report_path),
                        "suggestions_path": str(md_path),
                        "model": api.get("model"),
                    }
                    (adv_out / "advisor_suggestions.json").write_text(
                        json.dumps(meta, ensure_ascii=False, indent=2),
                        encoding="utf-8",
                    )
                    st.session_state["advisor_suggestions"] = text
                    st.success(f"已写入 {md_path}")
            except Exception as e:
                st.exception(e)
                return

    text = st.session_state.get("advisor_suggestions")
    if not text:
        sug = adv_out / "advisor_suggestions.md"
        if sug.is_file():
            text = sug.read_text(encoding="utf-8")
            st.session_state["advisor_suggestions"] = text
    if text:
        st.markdown(text)
        st.download_button(
            "下载建议 Markdown",
            data=text.encode("utf-8"),
            file_name="advisor_suggestions.md",
            mime="text/markdown",
        )
    else:
        st.info("还没有建议。点击上方按钮生成。")


def render_prompt_tab(adv_out: Path) -> None:
    st.subheader("实际送入模型的 Prompt")
    prompt = st.session_state.get("advisor_prompt")
    if not prompt:
        p = adv_out / "advisor_prompt.md"
        if p.is_file():
            prompt = p.read_text(encoding="utf-8")
    if not prompt:
        st.info("先在「LLM 建议」里跑一次预处理或生成。")
        return
    st.write(f"长度约 {len(prompt)} 字符 / {len(prompt.encode('utf-8')) // 1024} KB")
    st.text_area("prompt", prompt, height=480)
    ctx = st.session_state.get("advisor_context")
    if not ctx:
        cpath = adv_out / "advisor_context.json"
        if cpath.is_file():
            ctx = load_json(cpath)
    if ctx:
        with st.expander("advisor_context.json"):
            st.json(ctx)


def _safe_load(path: Path) -> Optional[Dict[str, Any]]:
    if not path.is_file():
        return None
    try:
        return load_json(path)
    except Exception:
        return None


def _role_frame(report: Dict[str, Any]) -> pd.DataFrame:
    rows = []
    for role, row in (report.get("by_role") or {}).items():
        surv = row.get("survival_rounds") or {}
        rows.append(
            {
                "role": role,
                "win_rate": row.get("win_rate"),
                "death_rate": row.get("death_rate"),
                "surv_mean": surv.get("mean"),
                "surv_p25": surv.get("p25"),
                "surv_median": surv.get("median"),
                "surv_p75": surv.get("p75"),
                "wins": row.get("wins"),
                "deaths": row.get("deaths"),
            }
        )
    return pd.DataFrame(rows)


if __name__ == "__main__":
    main()
