from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional

import streamlit as st
import yaml

ROOT = Path(__file__).resolve().parent.parent
TOOLS = ROOT.parent
SIM = TOOLS / "sim"
if str(SIM) not in sys.path:
    sys.path.insert(0, str(SIM))

from elephant_sim.unity_batch import find_unity_editor  # noqa: E402

st.set_page_config(page_title="象群危机 · RL 训练台", layout="wide", page_icon="🧠")

RESULTS = ROOT / "results"
REPORTS = ROOT / "reports"
CONFIG = ROOT / "config"
VENV_PY = ROOT / ".venv" / "Scripts" / "python.exe"
BEHAVIOR = "ElephantCrisis"
_STEP_IN_NAME = re.compile(r"-(\d{3,})(?:\.(?:pt|onnx|nn))?$")


def trainer_python() -> Path:
    """Prefer Tools/rl/.venv (Python 3.10 + mlagents); fall back to current interpreter."""
    if VENV_PY.is_file():
        return VENV_PY
    return Path(sys.executable)


def _read_json(path: Path) -> Optional[Dict[str, Any]]:
    if not path.is_file():
        return None
    try:
        # Unity File.WriteAllText(UTF8) often writes a BOM; utf-8-sig strips it.
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return None


def max_steps_from_yaml(path: Path) -> Optional[int]:
    if not path.is_file():
        return None
    try:
        data = yaml.safe_load(path.read_text(encoding="utf-8")) or {}
    except (OSError, yaml.YAMLError):
        return None
    beh = (data.get("behaviors") or {}).get(BEHAVIOR) or {}
    v = beh.get("max_steps")
    return int(v) if v is not None else None


def list_run_dirs() -> List[Path]:
    RESULTS.mkdir(parents=True, exist_ok=True)
    return sorted(
        [p for p in RESULTS.iterdir() if p.is_dir()],
        key=lambda p: p.stat().st_mtime,
        reverse=True,
    )


def steps_from_checkpoints(run_dir: Path) -> Optional[int]:
    best: Optional[int] = None
    for p in run_dir.rglob("*"):
        if not p.is_file():
            continue
        if p.suffix.lower() not in {".pt", ".onnx", ".nn"}:
            continue
        m = _STEP_IN_NAME.search(p.stem) or _STEP_IN_NAME.search(p.name)
        if not m:
            # e.g. ElephantCrisis-200000.onnx
            m = re.search(r"(\d{3,})", p.stem)
        if not m:
            continue
        step = int(m.group(1))
        if best is None or step > best:
            best = step
    return best


def steps_from_tensorboard(run_dir: Path) -> Optional[int]:
    event_dirs = []
    beh = run_dir / BEHAVIOR
    if beh.is_dir():
        event_dirs.append(beh)
    event_dirs.append(run_dir)
    try:
        from tensorboard.backend.event_processing.event_accumulator import (  # type: ignore
            EventAccumulator,
        )
    except ImportError:
        return None

    best: Optional[int] = None
    for d in event_dirs:
        events = list(d.glob("events.out.tfevents.*"))
        if not events:
            continue
        try:
            acc = EventAccumulator(str(d), size_guidance={"scalars": 0})
            acc.Reload()
            for tag in acc.Tags().get("scalars", []):
                scalars = acc.Scalars(tag)
                if not scalars:
                    continue
                step = int(scalars[-1].step)
                if best is None or step > best:
                    best = step
        except Exception:
            continue
    return best


def read_trainer_progress(run_dir: Path) -> Dict[str, Any]:
    """Parse mlagents results/<run-id> for current step vs max_steps."""
    out: Dict[str, Any] = {
        "run_id": run_dir.name,
        "steps": None,
        "max_steps": None,
        "source": None,
    }
    cfg = run_dir / "configuration.yaml"
    out["max_steps"] = max_steps_from_yaml(cfg)
    if out["max_steps"] is None:
        name = run_dir.name.lower()
        if "bootstrap" in name:
            out["max_steps"] = max_steps_from_yaml(CONFIG / "ppo_bootstrap.yaml")
        else:
            out["max_steps"] = max_steps_from_yaml(CONFIG / "ppo_selfplay.yaml")
        if out["max_steps"] is None:
            out["max_steps"] = max_steps_from_yaml(CONFIG / "ppo_bootstrap.yaml")

    tb = steps_from_tensorboard(run_dir)
    ckpt = steps_from_checkpoints(run_dir)
    if tb is not None and ckpt is not None:
        out["steps"] = max(tb, ckpt)
        out["source"] = "tensorboard+checkpoint"
    elif tb is not None:
        out["steps"] = tb
        out["source"] = "tensorboard"
    elif ckpt is not None:
        out["steps"] = ckpt
        out["source"] = "checkpoint"
    return out


def render_step_banner(key_prefix: str = "steps") -> None:
    """Show trainer steps + Unity decision steps at top of live tab / train tab."""
    st.markdown("#### 步数进度")
    runs = list_run_dirs()
    progress = _read_json(REPORTS / "training_progress.json") or {}
    stats = _read_json(REPORTS / "training_stats.json") or {}

    unity_steps = progress.get("rl_decisions_total")
    if unity_steps is None:
        unity_steps = stats.get("rl_decisions_total")
    matches = progress.get("matches_total")
    if matches is None:
        matches = stats.get("matches_total")

    c1, c2, c3 = st.columns(3)
    with c1:
        if runs:
            pick = st.selectbox(
                "Trainer run-id",
                [p.name for p in runs],
                key=f"{key_prefix}_run_id",
                help="来自 mlagents-learn 的 results/；这才是 max_steps 预算进度。",
            )
            info = read_trainer_progress(RESULTS / pick)
            steps = info.get("steps")
            max_s = info.get("max_steps")
            if steps is not None:
                st.metric("Trainer 已学步数", f"{int(steps):,}")
                if max_s:
                    frac = min(1.0, float(steps) / float(max_s))
                    st.progress(frac, text=f"{int(steps):,} / {int(max_s):,}（{100 * frac:.1f}%）")
                st.caption(f"来源：{info.get('source')} · 目标 max_steps={max_s}")
            else:
                st.warning("该 run 尚无 step 记录（Trainer 未跑或刚启动）。")
                if max_s:
                    st.caption(f"配置 max_steps = {int(max_s):,}")
        else:
            st.warning("results/ 为空：还没有 mlagents-learn 写出的训练步。")
            st.caption("请先开终端 A 的 `mlagents-learn`，再开 Unity。")

    with c2:
        if unity_steps is not None:
            st.metric("Unity 决策步", f"{int(unity_steps):,}")
            st.caption("每次微操作 +1；有 Trainer 时大致跟随学习步。")
        else:
            st.metric("Unity 决策步", "—")
            st.caption("开始 -rlTrain 后写入 `training_progress.json`。")

    with c3:
        if matches is not None:
            st.metric("累计局数", f"{int(matches):,}")
        else:
            st.metric("累计局数", "—")
        st.caption("局数 ≠ 步数；预算按 Trainer 步消耗。")


def main() -> None:
    st.title("深度强化学习 · 训练台")
    st.caption(
        "Unity ML-Agents · Game lean 真源 · bootstrap → self-play。"
        "RL 胜率 ≠ 真人平衡；请先关闭已打开的 Game 工程 Editor。"
    )

    unity = find_unity_editor()
    with st.sidebar:
        st.header("环境")
        st.write("Unity：" + (f"`{unity}`" if unity else "**未找到**（设 UNITY_EDITOR）"))
        st.write(f"配置目录：`{CONFIG}`")
        st.write(f"结果目录：`{RESULTS}`")
        st.divider()
        st.markdown(
            "- Behavior：`ElephantCrisis`\n"
            "- 观测：96 维\n"
            "- 动作：Op×dx×dy×target + mask\n"
            "- 文档：`Docs/RL_自博弈训练.md`"
        )

    tab_guide, tab_train, tab_eval, tab_results, tab_live = st.tabs(
        ["① 说明", "② 训练启动", "③ 评估", "④ 结果", "⑤ 训练埋点"]
    )
    with tab_guide:
        render_guide()
    with tab_train:
        render_train(unity)
    with tab_eval:
        render_eval(unity)
    with tab_results:
        render_results()
    with tab_live:
        render_live_stats()


def render_guide() -> None:
    st.subheader("课程怎么走")
    st.markdown(
        """
1. **Bootstrap**：1 席 PPO 学，另外 3 席启发式 —— 验证观测/动作/mask 能学。
2. **Self-play**：四席共享策略；配合 `ppo_selfplay.yaml` 的 self_play + league 目录。
3. **评估**：固定 seed / 局数，看 vs 启发式或自博弈胜率分布。
4. **接入**：训练好的策略再进 LogicSim / 对战（`-ai rl` 桥已预留）。

**两个终端：** 先起 `mlagents-learn`，再起 Unity 环境（本页可帮你拼命令）。

**奖励（当前）：** 胜 +2 / 死亡 -1 / 败但存活 -0.5 / 平局(无人存活) -0.2；人偶 ±0.1；击杀 +0.25；伤害 +0.003×dealt（步上限 0.1）、受伤 -0.001×taken。无回合超时。无「用卡」奖。
"""
    )
    c1, c2 = st.columns(2)
    with c1:
        st.markdown("#### Bootstrap 配置")
        st.code((CONFIG / "ppo_bootstrap.yaml").read_text(encoding="utf-8")[:1200], language="yaml")
    with c2:
        st.markdown("#### Self-play 配置")
        st.code((CONFIG / "ppo_selfplay.yaml").read_text(encoding="utf-8")[:1200], language="yaml")


def render_train(unity: Optional[Path]) -> None:
    st.subheader("训练参数")
    render_step_banner("train")
    st.divider()
    curriculum = st.selectbox("课程", ["bootstrap", "selfplay"], index=0)
    role = st.selectbox("Bootstrap 学习角色", ["human", "elephant", "monkey", "cat"], index=0)
    run_id = st.text_input("run-id", value=f"ec_{curriculum}_v1")
    force = st.checkbox("mlagents --force（覆盖同名 run）", value=False)
    league = st.text_input(
        "League 目录（selfplay 可选）",
        value=str(RESULTS / run_id) if curriculum == "selfplay" else "",
    )
    ghost = st.slider("幽灵对手比例", 0.0, 1.0, 0.3, 0.05)

    cfg = CONFIG / ("ppo_selfplay.yaml" if curriculum == "selfplay" else "ppo_bootstrap.yaml")

    tpy = trainer_python()
    learn_cmd = [
        str(tpy),
        "-m",
        "mlagents.trainers.learn",
        str(cfg),
        "--run-id",
        run_id,
        "--results-dir",
        str(RESULTS),
    ]
    if force:
        learn_cmd.append("--force")

    env_cmd = [
        sys.executable,
        str(ROOT / "scripts" / "run_train_env.py"),
        "--curriculum",
        curriculum,
        "--role",
        role,
    ]
    if league.strip():
        env_cmd.extend(["--league-dir", league.strip(), "--ghost-ratio", str(ghost)])

    st.markdown("#### 终端 A · Python Trainer（请先开）")
    if not VENV_PY.is_file():
        st.error(
            "未找到 `Tools/rl/.venv`。请用 **Python 3.10** 创建："
            "`py -3.10 -m venv Tools/rl/.venv` 然后 "
            "`Tools\\rl\\.venv\\Scripts\\python -m pip install -r Tools/rl/requirements.txt`"
        )
    else:
        st.caption(f"Trainer 解释器：`{tpy}`（勿用系统 3.14，mlagents 装不上）")
    def _ps(cmd: List[str]) -> str:
        parts = []
        for i, c in enumerate(cmd):
            if i == 0:
                parts.append(f'& "{c}"')
            elif any(ch in c for ch in (" ", "\\", ":")) and not c.startswith("-"):
                parts.append(f'"{c}"')
            else:
                parts.append(c)
        return " ".join(parts)

    st.code(_ps(learn_cmd), language="powershell")
    st.markdown("#### 终端 B · Unity 环境")
    st.code(_ps(env_cmd), language="powershell")

    b1, b2 = st.columns(2)
    if b1.button("复制 Trainer 命令提示", use_container_width=True):
        st.info("请手动复制上方 Trainer 命令到独立终端（训练进程需长期占用）。")
    if b2.button("启动 Unity 训练环境", type="primary", use_container_width=True):
        if unity is None:
            st.error("未找到 Unity.exe，请设置环境变量 UNITY_EDITOR。")
            return
        st.warning("将拉起 Unity；请确认已关闭同工程 Editor，且终端 A 的 Trainer 已在跑。")
        with st.spinner("Launching Unity …"):
            proc = subprocess.run(env_cmd, cwd=str(ROOT), capture_output=True, text=True)
        if proc.returncode != 0:
            st.error(f"退出码 {proc.returncode}")
            st.code(proc.stderr or proc.stdout or "(no output)")
        else:
            st.success("Unity 进程已结束（正常训练中应一直开着；若秒退请看 unity_rl_train.log）")
            log = ROOT / "unity_rl_train.log"
            if log.is_file():
                st.code(log.read_text(encoding="utf-8", errors="ignore")[-3000:])


def render_eval(unity: Optional[Path]) -> None:
    st.subheader("固定评估")
    st.caption("调用 `scripts/run_eval.py`（Unity -rlEval）。结果写入 reports/rl_eval.json。")
    c1, c2, c3 = st.columns(3)
    with c1:
        episodes = st.number_input("局数", min_value=1, max_value=500, value=20, step=1)
        seed = st.number_input("seed", min_value=1, value=100, step=1)
    with c2:
        curriculum = st.selectbox("评估课程", ["bootstrap", "selfplay"], key="eval_cur")
        role = st.selectbox("角色", ["human", "elephant", "monkey", "cat"], key="eval_role")
    with c3:
        st.caption("无 maxRounds：熔岩缩圈至决出胜负/平局")

    out = REPORTS / "rl_eval.json"
    if st.button("运行评估", type="primary", use_container_width=True):
        if unity is None:
            st.error("未找到 Unity.exe")
            return
        cmd = [
            sys.executable,
            str(ROOT / "scripts" / "run_eval.py"),
            "--episodes",
            str(int(episodes)),
            "--seed",
            str(int(seed)),
            "--curriculum",
            curriculum,
            "--role",
            role,
            "--out",
            str(out),
        ]
        with st.spinner("Unity -rlEval 运行中…"):
            proc = subprocess.run(cmd, cwd=str(ROOT), capture_output=True, text=True)
        if proc.returncode != 0:
            st.error(f"失败 exit={proc.returncode}")
            st.code(proc.stderr or proc.stdout or "")
        else:
            st.success("完成")
            if proc.stdout:
                st.code(proc.stdout[-2500:])

    if out.is_file():
        st.subheader("最近评估报告")
        data = json.loads(out.read_text(encoding="utf-8-sig"))
        st.json(data)
        winners = data.get("winners") or {}
        if winners:
            st.bar_chart({"wins": winners})


def render_results() -> None:
    st.subheader("results/ 下的 run")
    RESULTS.mkdir(parents=True, exist_ok=True)
    runs = sorted([p for p in RESULTS.iterdir() if p.is_dir()], key=lambda p: p.stat().st_mtime, reverse=True)
    if not runs:
        st.info("暂无 results/。跑过 mlagents-learn 后会出现 run-id 目录。")
        return
    names = [p.name for p in runs]
    pick = st.selectbox("run-id", names)
    run_dir = RESULTS / pick
    files = list(run_dir.rglob("*"))
    st.write(f"文件数约 {len(files)}")
    # Show shallow listing
    rows: List[Dict[str, Any]] = []
    for p in sorted(run_dir.glob("*"))[:50]:
        rows.append({"name": p.name, "type": "dir" if p.is_dir() else "file", "bytes": p.stat().st_size if p.is_file() else 0})
    st.dataframe(rows, use_container_width=True)
    st.caption("完整 TensorBoard：`tensorboard --logdir results`")


def render_live_stats() -> None:
    st.subheader("训练埋点（滚动最近 100 局）")
    st.caption(
        "Unity 训练时写入 `Tools/rl/reports/training_stats.json` 与 `training_matches.jsonl`。"
        "含胜率、死亡率、领袖宣言次数等。"
    )
    stats_path = REPORTS / "training_stats.json"
    jsonl_path = REPORTS / "training_matches.jsonl"
    if st.button("刷新", key="refresh_rl_stats"):
        st.rerun()

    render_step_banner("live")
    st.divider()

    if not stats_path.is_file():
        st.info("尚无 training_stats.json。开始 -rlTrain 并对局结束后会出现。")
        return

    data = json.loads(stats_path.read_text(encoding="utf-8-sig"))
    c1, c2, c3, c4 = st.columns(4)
    c1.metric("窗口局数", data.get("window"))
    c2.metric("累计局数", data.get("matches_total"))
    c3.metric(
        "Unity 决策步",
        f"{int(data['rl_decisions_total']):,}" if data.get("rl_decisions_total") is not None else "—",
    )
    c4.metric("每局平均宣言", f"{float(data.get('leader_declarations_per_match') or 0):.2f}")

    if data.get("learning_role"):
        st.write(
            f"Bootstrap 学习角色 `{data.get('learning_role')}` 窗内胜率："
            f"**{100 * float(data.get('learning_role_win_rate') or 0):.1f}%**"
        )

    wr = data.get("win_rate") or {}
    dr = data.get("death_rate") or {}
    if wr:
        st.markdown("#### 胜率（窗内）")
        st.bar_chart(wr)
    if dr:
        st.markdown("#### 死亡率（窗内）")
        st.bar_chart(dr)

    st.markdown("#### 终局原因")
    st.json(data.get("end_reasons") or {})
    st.markdown("#### 原始汇总")
    st.json(data)

    if jsonl_path.is_file():
        lines = jsonl_path.read_text(encoding="utf-8-sig", errors="ignore").strip().splitlines()
        st.caption(f"jsonl 行数：{len(lines)}（展示最近 15 行）")
        tail = lines[-15:]
        st.code("\n".join(tail), language="json")


if __name__ == "__main__":
    main()
