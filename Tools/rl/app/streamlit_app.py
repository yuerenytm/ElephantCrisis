from __future__ import annotations

import copy
import datetime
import json
import re
import subprocess
import sys
import time
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

# 可调 PPO 超参：(yaml_key, 显示名, 控件类型, (min, max), 步长)
# "int" 整数输入；"float" 普通浮点；"log" 科学计数法输入（对数尺度参数）
PPO_PARAM_SPECS = [
    ("learning_rate", "学习率 learning_rate", "log", (1e-5, 1e-2), None),
    ("batch_size", "batch_size（小批大小）", "int", (16, 4096), 16),
    ("buffer_size", "buffer_size（经验蓄水池）", "int", (512, 65536), 256),
    ("num_epoch", "num_epoch（每批反复学几轮）", "int", (1, 20), 1),
    ("gamma", "γ gamma（折扣因子）", "float", (0.9, 0.999), 0.001),
    ("lambd", "λ lambd（GAE 平滑）", "float", (0.9, 0.999), 0.01),
    ("epsilon", "ε epsilon（PPO 裁剪）", "float", (0.05, 0.5), 0.01),
    ("beta", "β beta（熵探索强度）", "float", (0.0, 0.05), 0.0005),
]
PPO_NET_SPECS = [
    ("hidden_units", "hidden_units（每层神经元）", "int", (32, 2048), 32),
    ("num_layers", "num_layers（层数）", "int", (1, 8), 1),
]
PPO_TRAIN_SPECS = [
    ("max_steps", "max_steps（总预算）", "int", (1000, 5000000), 1000),
    ("time_horizon", "time_horizon（时序长度）", "int", (32, 1024), 32),
    ("summary_freq", "summary_freq（日志频率）", "int", (500, 50000), 500),
    ("checkpoint_interval", "checkpoint_interval（存档间隔）", "int", (1000, 500000), 1000),
]
PPO_SP_SPECS = [
    ("window", "self_play.window（幽灵窗口）", "int", (2, 32), 1),
    ("play_against_latest_model_ratio", "self_play 打最新模型比例", "float", (0.1, 0.9), 0.05),
    ("save_steps", "self_play.save_steps（幽灵存档间隔）", "int", (1000, 100000), 1000),
]


def render_param_widgets(specs: List[tuple], prefix: str, config: Dict[str, Any]) -> Dict[str, Any]:
    """按规格渲染一组参数控件；读 config 作默认值，返回用户当前值。"""
    out: Dict[str, Any] = {}
    for key, label, ctype, rng, step in specs:
        skey = f"{prefix}_{key}"
        default = config.get(key)
        if ctype == "int":
            out[key] = st.number_input(
                label,
                min_value=int(rng[0]),
                max_value=int(rng[1]),
                value=int(default) if default is not None else int(rng[0]),
                step=int(step),
                key=skey,
            )
        elif ctype == "log":
            out[key] = st.number_input(
                label,
                min_value=float(rng[0]),
                max_value=float(rng[1]),
                value=float(default) if default is not None else float(rng[0]),
                format="%.2e",
                key=skey,
            )
        else:
            out[key] = st.number_input(
                label,
                min_value=float(rng[0]),
                max_value=float(rng[1]),
                value=float(default) if default is not None else float(rng[0]),
                step=float(step) if step is not None else None,
                format="%.4f",
                key=skey,
            )
    return out


def trainer_python() -> Path:
    """Prefer Tools/rl/.venv (Python 3.10 + mlagents); fall back to current interpreter."""
    if VENV_PY.is_file():
        return VENV_PY
    return Path(sys.executable)


# ---- 后台进程管理（Trainer / Unity）----
# Streamlit 每次交互都会重跑脚本，不能依赖内存状态；用 reports/trainer_pids.json 持久化 PID。
TRAINER_PIDS = REPORTS / "trainer_pids.json"


def load_trainer_pids() -> Dict[str, Any]:
    return _read_json(TRAINER_PIDS) or {}


def save_trainer_pids(data: Dict[str, Any]) -> None:
    REPORTS.mkdir(parents=True, exist_ok=True)
    TRAINER_PIDS.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def is_pid_alive(pid: Optional[int]) -> bool:
    """Windows 下用 tasklist 判断进程是否存活（os.kill(pid,0) 在 Win 上不可靠）。"""
    if not pid:
        return False
    try:
        r = subprocess.run(
            ["tasklist", "/FI", f"PID eq {int(pid)}", "/NH"],
            capture_output=True,
            text=True,
            timeout=15,
        )
        return str(pid) in (r.stdout or "")
    except (OSError, subprocess.TimeoutExpired, ValueError):
        return False


def kill_process(pid: Optional[int]) -> bool:
    """Windows 下强制终止进程（os.kill(SIGTERM) 在 Win 上不可靠，用 taskkill /F）。"""
    if not pid:
        return False
    try:
        r = subprocess.run(
            ["taskkill", "/F", "/T", "/PID", str(int(pid))],
            capture_output=True,
            text=True,
            timeout=15,
        )
        return r.returncode == 0
    except (OSError, subprocess.TimeoutExpired):
        return False


def trainer_status(run_id: str):
    """返回 (pid, log_path, alive)。"""
    data = load_trainer_pids()
    entry = data.get(run_id) or {}
    pid = entry.get("pid")
    log = Path(entry["log"]) if entry.get("log") else None
    return pid, log, is_pid_alive(pid)


def any_other_trainer_alive(run_id: str) -> Optional[str]:
    """是否有其它 run-id 的 Trainer 仍在跑（抢端口警告用）。"""
    for rid, entry in (load_trainer_pids() or {}).items():
        if rid != run_id and is_pid_alive(entry.get("pid")):
            return rid
    return None


def spawn_detached(cmd: List[str], cwd: Path, log_path: Path) -> subprocess.Popen:
    """Windows 下分离启动长驻进程：不阻塞 Streamlit、进程存活于本页生命周期之外。"""
    log_path.parent.mkdir(parents=True, exist_ok=True)
    logf = open(log_path, "a", encoding="utf-8")
    logf.write(f"\n--- spawn {Path(cmd[0]).name} at {datetime.datetime.now().isoformat(timespec='seconds')} ---\n")
    logf.flush()
    flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    try:
        return subprocess.Popen(
            cmd,
            cwd=str(cwd),
            stdin=subprocess.DEVNULL,
            stdout=logf,
            stderr=subprocess.STDOUT,
            creationflags=flags,
        )
    except OSError:
        # 个别 Python/Windows 组合不支持该 flag，回落无 flag（仍重定向输出）
        return subprocess.Popen(cmd, cwd=str(cwd), stdin=subprocess.DEVNULL, stdout=logf, stderr=subprocess.STDOUT)


def tail_lines(path: Optional[Path], n: int = 10) -> str:
    if not path or not path.is_file():
        return ""
    try:
        lines = path.read_text(encoding="utf-8", errors="ignore").strip().splitlines()
    except OSError:
        return ""
    return "\n".join(lines[-n:])


def parse_trainer_console(log_path: Path) -> Dict[str, Any]:
    """解析 trainer_<run-id>.log 中最近一条 [INFO] Step 行，取实时步数 / 用时 / 奖励。

    格式示例：
      [INFO] ElephantCrisis. Step: 7000. Time Elapsed: 381.862 s. Mean Reward: -1.153. ...
    返回空 dict 表示尚无该日志或还没有 Step 输出。
    """
    if not log_path.is_file():
        return {}
    try:
        lines = log_path.read_text(encoding="utf-8", errors="ignore").splitlines()
    except OSError:
        return {}
    pat = re.compile(
        r"Step:\s*(\d+)\s*\.\s*Time Elapsed:\s*([\d.]+)\s*s\s*\.\s*Mean Reward:\s*(-?[\d.]+?)\s*\."
    )
    for line in reversed(lines):
        m = pat.search(line)
        if m:
            return {
                "step": int(m.group(1)),
                "elapsed_s": float(m.group(2)),
                "mean_reward": float(m.group(3)),
            }
    return {}


def session_elapsed_seconds(run_id: str) -> Optional[float]:
    """从 trainer_pids.json 的 started_at 计算本次训练已运行的墙钟秒数（含握手等待）。"""
    entry = (load_trainer_pids().get(run_id) or {})
    started = entry.get("started_at")
    if not started:
        return None
    try:
        t0 = datetime.datetime.fromisoformat(started)
        return (datetime.datetime.now() - t0).total_seconds()
    except (ValueError, TypeError):
        return None


def format_duration(total_s: Optional[float]) -> str:
    if total_s is None:
        return "—"
    s = max(0, int(total_s))
    h, rem = divmod(s, 3600)
    m, sec = divmod(rem, 60)
    if h:
        return f"{h}时{m:02d}分{sec:02d}秒"
    if m:
        return f"{m}分{sec:02d}秒"
    return f"{sec}秒"


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


def _write_square_cfg(
    base_cfg: Path,
    square_cfg: Path,
    build_exe: Path,
    num_envs: int,
    curriculum: str,
    role: str,
    league_dir: str = "",
    ghost_ratio: float = 0.3,
) -> None:
    """生成广场模式配置：官方配置 + env_path + num_envs + env_args（传给每个 Player）。"""
    data = yaml.safe_load(base_cfg.read_text(encoding="utf-8")) or {}
    env = data.setdefault("env_settings", {})
    env["env_path"] = str(build_exe)
    env["num_envs"] = int(num_envs)
    # 每个 spawn 的 Player 需要这些参数才会进入 RL 训练（RlTrainingRunner.Boot 检测）
    args = ["-rlTrain", "-rlCurriculum", curriculum, "-rlRole", role]
    if league_dir:
        args += ["-rlLeagueDir", league_dir, "-rlGhostRatio", str(ghost_ratio)]
    env["env_args"] = args
    square_cfg.write_text(yaml.safe_dump(data, allow_unicode=True), encoding="utf-8")


def run_resume_info(run_id: str) -> Dict[str, Any]:
    """检查 results/<run-id> 是否存在、可续训或需覆盖。"""
    run_dir = RESULTS / run_id
    if not run_dir.is_dir():
        return {"exists": False, "resumable": False, "steps": None}
    steps = steps_from_checkpoints(run_dir)
    ckpt = run_dir / BEHAVIOR / "checkpoint.pt"
    return {
        "exists": True,
        "resumable": ckpt.is_file(),
        "steps": steps,
    }


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
    """实时训练指标：Trainer 步数 / 本次用时 / 累计局数 / Unity 决策步，每 5s 自动刷新。"""
    st.markdown("#### 实时训练指标")

    runs = list_run_dirs()
    pick = None
    if runs:
        pick = st.selectbox(
            "Trainer run-id",
            [p.name for p in runs],
            key=f"{key_prefix}_run_id",
            help="来自 mlagents-learn 的 results/；这才是 max_steps 预算进度。",
        )

    @st.fragment(run_every="5s")
    def _live_metrics():
        progress = _read_json(REPORTS / "training_progress.json") or {}
        stats = _read_json(REPORTS / "training_stats.json") or {}
        unity_steps = progress.get("rl_decisions_total")
        if unity_steps is None:
            unity_steps = stats.get("rl_decisions_total")
        matches = progress.get("matches_total")
        if matches is None:
            matches = stats.get("matches_total")

        # 实时步数 / 用时：优先解析当前 run 的 trainer 日志（比 tensorboard/checkpoint 实时）
        realtime: Dict[str, Any] = {}
        if pick:
            realtime = parse_trainer_console(ROOT / f"trainer_{pick}.log")
        else:
            for f in sorted(ROOT.glob("trainer_*.log"), key=lambda p: p.stat().st_mtime, reverse=True):
                realtime = parse_trainer_console(f)
                if realtime:
                    break
        wall = session_elapsed_seconds(pick) if pick else None

        m1, m2, m3, m4 = st.columns(4)
        with m1:
            if realtime.get("step") is not None:
                st.metric("Trainer 实时步数", f"{int(realtime['step']):,}")
                st.caption("trainer 日志打点（summary_freq）")
            elif pick:
                info = read_trainer_progress(RESULTS / pick)
                steps = info.get("steps")
                st.metric("Trainer 步数", f"{int(steps):,}" if steps is not None else "—")
                st.caption("tensorboard / checkpoint 回退")
            else:
                st.metric("Trainer 步数", "—")
                st.caption("尚无 run / 日志")
        with m2:
            el = realtime.get("elapsed_s")
            if el is not None:
                st.metric("本次训练用时", format_duration(el))
                st.caption("trainer Time Elapsed")
            elif wall is not None:
                st.metric("本次训练用时", format_duration(wall))
                st.caption("自进程启动（墙钟）")
            else:
                st.metric("本次训练用时", "—")
                st.caption("启动后显示")
        with m3:
            st.metric("累计局数", f"{int(matches):,}" if matches is not None else "—")
            st.caption("Unity 每局结束写入")
        with m4:
            st.metric("Unity 决策步", f"{int(unity_steps):,}" if unity_steps is not None else "—")
            st.caption("每次微操作 +1")

        # 预算进度条（实时步数优先于 checkpoint）
        if pick:
            info = read_trainer_progress(RESULTS / pick)
            steps = info.get("steps")
            max_s = info.get("max_steps")
            src = info.get("source")
            if realtime.get("step") is not None and (steps is None or int(realtime["step"]) > int(steps)):
                steps = int(realtime["step"])
                src = "trainer_log"
            if steps is not None:
                frac = min(1.0, float(steps) / float(max_s)) if max_s else 0.0
                st.progress(frac, text=f"预算进度：{int(steps):,} / {int(max_s):,}（{100 * frac:.1f}%）")
                st.caption(f"来源：{src} · 目标 max_steps={max_s}")
            else:
                st.warning("该 run 尚无 step 记录（Trainer 未跑或刚启动）。")
                if max_s:
                    st.caption(f"配置 max_steps = {int(max_s):,}")
        elif not runs:
            st.warning("results/ 为空：还没有 mlagents-learn 写出的训练步。")
            st.caption("请先开终端 A 的 `mlagents-learn`，再开 Unity。")

    _live_metrics()


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

    base_cfg = CONFIG / ("ppo_selfplay.yaml" if curriculum == "selfplay" else "ppo_bootstrap.yaml")
    custom_cfg = CONFIG / f"ppo_{curriculum}_custom.yaml"
    src_cfg = custom_cfg if custom_cfg.is_file() else base_cfg
    cfg = custom_cfg if custom_cfg.is_file() else base_cfg

    # ---- PPO 常用超参编辑器 ----
    with st.expander(f"PPO 超参（当前课程：{curriculum}）", expanded=custom_cfg.is_file()):
        src = yaml.safe_load(src_cfg.read_text(encoding="utf-8")) or {}
        beh = (src.get("behaviors") or {}).get(BEHAVIOR) or {}
        hp = (beh.get("hyperparameters") or {})
        net = (beh.get("network_settings") or {})
        sp = (beh.get("self_play") or {})
        prefix = f"hp_{curriculum}"

        c1, c2 = st.columns(2)
        with c1:
            st.markdown("**PPO 核心**")
            new_hp = render_param_widgets(PPO_PARAM_SPECS, f"{prefix}_core", hp)
        with c2:
            st.markdown("**网络 / 训练**")
            new_net = render_param_widgets(PPO_NET_SPECS, f"{prefix}_net", net)
            new_train = render_param_widgets(PPO_TRAIN_SPECS, f"{prefix}_train", beh)

        new_sp: Dict[str, Any] = {}
        if curriculum == "selfplay":
            st.markdown("**自博弈（selfplay）**")
            new_sp = render_param_widgets(PPO_SP_SPECS, f"{prefix}_sp", sp)

        bc1, bc2, bc3 = st.columns([1, 1, 2])
        if bc1.button("生成自定义配置", type="primary", use_container_width=True):
            src.setdefault("behaviors", {}).setdefault(BEHAVIOR, {})
            cb = src["behaviors"][BEHAVIOR]
            cb["hyperparameters"] = {**(cb.get("hyperparameters") or {}), **new_hp}
            cb["network_settings"] = {**(cb.get("network_settings") or {}), **new_net}
            for k, v in new_train.items():
                cb[k] = v
            if curriculum == "selfplay" and new_sp:
                cb["self_play"] = {**(cb.get("self_play") or {}), **new_sp}
            custom_cfg.write_text(yaml.safe_dump(src, allow_unicode=True), encoding="utf-8")
            st.success(f"已写入 `{custom_cfg.name}`，下方命令将使用它")
            st.rerun()
        if bc2.button("恢复官方默认", use_container_width=True):
            if custom_cfg.is_file():
                custom_cfg.unlink()
                st.success(f"已删除 `{custom_cfg.name}`，回到 `{base_cfg.name}`")
                st.rerun()
            else:
                st.info("当前就在用官方默认配置")
        if custom_cfg.is_file():
            st.caption(f"正在使用自定义配置 `{custom_cfg.name}`（可随时改后重新生成，或恢复官方默认）")
        else:
            st.caption(f"正在使用官方配置 `{base_cfg.name}`")

    run_id = st.text_input("run-id", value=f"ec_{curriculum}_v1")
    run_info = run_resume_info(run_id)

    st.markdown("**启动方式**")
    mode_c1, mode_c2 = st.columns([2, 1])
    with mode_c1:
        mode = st.radio(
            "已有同名 run 时：",
            options=["新建（换个新 run-id）", "续训（--resume）", "覆盖（--force，清空重来）"],
            index=0,
            horizontal=True,
            help="`--resume` 加载 checkpoint.pt 从上次断点继续；`--force` 删除旧目录从零开始。",
        )
    with mode_c2:
        if run_info["exists"]:
            st.caption(f"已存在 `{run_id}`：")
            if run_info["resumable"]:
                st.success(f"可续训，已学约 {run_info['steps'] or '?'} 步")
            else:
                st.warning("有目录但无 checkpoint（可能中途崩了）")
        else:
            st.caption("`results/` 下暂无该 run-id")

    league = st.text_input(
        "League 目录（selfplay 可选）",
        value=str(RESULTS / run_id) if curriculum == "selfplay" else "",
    )
    ghost = st.slider("幽灵对手比例", 0.0, 1.0, 0.3, 0.05)

    # ---- 环境来源：Editor（无头）或 Player 广场模式 ----
    st.markdown("**环境来源**")
    e1, e2 = st.columns([1, 1])
    with e1:
        env_src = st.radio(
            "Unity 环境",
            options=["Editor（推荐）", "Player 广场（多开加速）"],
            index=0,
            key=f"env_src_{curriculum}",
            help="Editor=官方推荐、改代码后直接跑；Player=需先 build 训练 exe（菜单 Tools/RL/构建训练 Player）。",
        )
    headless = True
    num_envs = 1
    square_cfg = None
    square_mode = env_src == "Player 广场（多开加速）"
    if env_src == "Editor（推荐）":
        headless = st.checkbox(
            "无头模式 -batchmode -nographics（省美术渲染，推荐）",
            value=True,
            key=f"headless_{curriculum}",
        )
    else:
        num_envs = st.number_input(
            "广场并行环境数 num_envs",
            min_value=1,
            max_value=16,
            value=1,
            step=1,
            key=f"num_envs_{curriculum}",
            help="trainer 会同时开 N 个 Player 进程喂训练，吞吐约线性提升（受 CPU 核心数限制）。",
        )
        build_exe = ROOT / "build" / "ElephantCrisis.exe"
        if not build_exe.is_file():
            st.warning(
                f"未找到 `{build_exe}`。请先在 Unity 里菜单 `Tools/RL/构建训练 Player`，"
                "或命令行跑 `RlBuildScript.Build`。"
            )
        else:
            st.success(f"使用 build：`{build_exe}`；广场并行数 `{num_envs}`")
            square_cfg = CONFIG / f"ppo_{curriculum}_square.yaml"
            _write_square_cfg(
                base_cfg,
                square_cfg,
                build_exe,
                int(num_envs),
                curriculum,
                role,
                league_dir=league.strip(),
                ghost_ratio=float(ghost),
            )

    tpy = trainer_python()
    # 广场模式：用自动生成的 square 配置（env_path + num_envs）覆盖官方/自定义配置
    if square_cfg is not None and square_cfg.is_file():
        effective_cfg = square_cfg
    else:
        effective_cfg = cfg
    learn_cmd = [
        str(tpy),
        "-m",
        "mlagents.trainers.learn",
        str(effective_cfg),
        "--run-id",
        run_id,
        "--results-dir",
        str(RESULTS),
    ]
    if mode == "续训（--resume）":
        learn_cmd.append("--resume")
    elif mode == "覆盖（--force，清空重来）":
        learn_cmd.append("--force")

    # ---- 启动方式安全提示 ----
    if mode == "续训（--resume）" and not run_info["exists"]:
        st.info(f"⚠️ 续训需要 `results/{run_id}` 已存在，但当前没有。将退化为从零训练。")
    elif mode == "续训（--resume）" and not run_info["resumable"]:
        st.warning(
            f"⚠️ `results/{run_id}` 有目录但**没有 checkpoint.pt**（可能中途崩了/未到存档间隔），"
            "续训会失败。建议改用覆盖或新建。"
        )
    if mode == "新建（换个新 run-id）" and run_info["exists"]:
        st.warning(f"⚠️ `results/{run_id}` 已存在，直接新建会被 mlagents 拒绝。请改 run-id 或选覆盖。")
    if mode == "覆盖（--force，清空重来）" and run_info["exists"] and run_info["resumable"]:
        st.warning(f"⚠️ `results/{run_id}` 已有可续 checkpoint（约 {run_info['steps']} 步），覆盖将**全部清空**。确认要重来？")

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
    if env_src == "Editor（推荐）":
        if headless:
            env_cmd.append("--headless")
        else:
            env_cmd.append("--no-headless")
    else:
        # 广场模式：trainer 通过 env_path 自动 spawn 多环境，无需手动拉 Unity
        env_cmd = []

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
    if square_mode:
        st.markdown("#### 终端 B · Unity 环境")
        st.caption("广场模式：由 Trainer 通过 `env_path` 自动拉起 N 个 Player，无需单独启动。")
        st.code("(自动，无需手动启动)", language="text")
    else:
        st.markdown("#### 终端 B · Unity 环境")
        st.code(_ps(env_cmd), language="powershell")

    # ---- 点击式启动 ----
    st.divider()
    st.markdown("#### 🖱️ 一键运行（点击即启，无需敲命令）")

    t_pid, t_log, t_alive = trainer_status(run_id)
    if t_alive:
        st.success(f"Trainer 运行中（PID {t_pid}，日志 `{t_log.name if t_log else '-'}`）")
    else:
        st.warning("Trainer 未运行。")
    other = any_other_trainer_alive(run_id)
    if other:
        st.warning(f"注意：`{other}` 的 Trainer 仍在运行，可能占用端口；建议先停掉再启动新的。")

    t_log_path = ROOT / f"trainer_{run_id}.log"

    def _start_trainer() -> bool:
        nonlocal t_pid, t_log, t_alive
        if other:
            st.error(f"`{other}` 的 Trainer 还在运行，请先停止（否则端口冲突）。")
            return False
        proc = spawn_detached(learn_cmd, ROOT, t_log_path)
        data = load_trainer_pids()
        data[run_id] = {
            "pid": proc.pid,
            "log": str(t_log_path),
            "started_at": datetime.datetime.now().isoformat(timespec="seconds"),
        }
        save_trainer_pids(data)
        t_pid, t_log, t_alive = proc.pid, t_log_path, True
        st.toast(f"Trainer 已后台启动（PID {proc.pid}）", icon="🚀")
        return True

    c_a1, c_a2, c_a3 = st.columns([1, 1, 1])
    with c_a1:
        if st.button("① 启动 Trainer (A)", type="primary", use_container_width=True, disabled=t_alive):
            if _start_trainer():
                st.rerun()
    with c_a2:
        if st.button(
            "② 启动 Unity 环境 (B)",
            use_container_width=True,
            disabled=square_mode,
            help=("广场模式由 trainer 自动拉起多环境" if square_mode else None),
        ):
            if unity is None:
                st.error("未找到 Unity.exe，请设置环境变量 UNITY_EDITOR。")
            else:
                if not t_alive:
                    st.warning("Trainer (A) 尚未运行！Unity 连不上 trainer 会以启发式空跑。建议先点 ① 或 ③。")
                proc = spawn_detached(env_cmd, ROOT, ROOT / "unity_rl_train.log")
                st.toast(f"Unity 已后台启动（PID {proc.pid}）。冷启动约 1~3 分钟。", icon="🎮")
    with c_a3:
        if st.button("③ 一键 A + B", type="primary", use_container_width=True, disabled=False):
            if not square_mode and unity is None:
                st.error("未找到 Unity.exe，请设置环境变量 UNITY_EDITOR。")
            else:
                if not t_alive:
                    if not _start_trainer():
                        st.stop()
                if square_mode:
                    st.info("广场模式：Trainer 就绪后会自动拉起 N 个 Player 环境。")
                    deadline = time.time() + 120
                    ready = False
                    with st.spinner("等待 Trainer 就绪（端口 5004）…"):
                        while time.time() < deadline:
                            if not is_pid_alive(t_pid):
                                break
                            txt = tail_lines(t_log)
                            if "Listening on port" in txt:
                                ready = True
                                break
                            time.sleep(1)
                    if ready:
                        st.toast("Trainer 就绪，将自动拉起 Player 环境。", icon="🎮")
                        st.rerun()
                    else:
                        st.error("Trainer 120 秒内未就绪，请查看上方日志。")
                else:
                    st.info("Trainer 已启动，等待其监听端口后自动拉起 Unity …")
                    deadline = time.time() + 120
                    ready = False
                    with st.spinner("等待 Trainer 就绪（端口 5004）…"):
                        while time.time() < deadline:
                            if not is_pid_alive(t_pid):
                                break
                            txt = tail_lines(t_log)
                            if "Listening on port" in txt:
                                ready = True
                                break
                            time.sleep(1)
                    if ready:
                        proc = spawn_detached(env_cmd, ROOT, ROOT / "unity_rl_train.log")
                        st.toast(f"Trainer 就绪，Unity 已自动启动（PID {proc.pid}）。", icon="🎮")
                        st.rerun()
                    else:
                        st.error("Trainer 120 秒内未就绪，请查看上方日志。Unity 未启动。")

    @st.fragment(run_every="5s")
    def _tail_log():
        st.caption("Trainer 日志实时尾部（每 5 秒自动刷新）")
        st.code(tail_lines(t_log) or "（尚无日志）", language="text")

    _tail_log()

    c_s1, c_s2 = st.columns([1, 1])
    with c_s1:
        if st.button("刷新状态", use_container_width=True):
            st.rerun()
    with c_s2:
        if st.button("停止 Trainer (A)", use_container_width=True, disabled=not t_alive):
            if t_pid and kill_process(t_pid):
                st.success("已停止 Trainer。")
            else:
                st.warning("进程可能已自行退出，或需手动结束。")
            data = load_trainer_pids()
            data.pop(run_id, None)
            save_trainer_pids(data)
            st.rerun()


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
