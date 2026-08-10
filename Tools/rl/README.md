# 《象群危机》深度强化学习（Unity ML-Agents）

四人 FFA **自博弈**训练台：造数真源是 Unity Game 脚本（`GameMode.RlTraining` lean），不恢复 Python 规则引擎。

| | 本工具 | logic_qa / balance |
|--|--|--|
| 跑什么 | ML-Agents PPO + Unity RL lean 环境 | LogicSim 对局日志 |
| 看什么 | 策略是否学会赢 / 自博弈是否收敛 | 规则违规 / 角色胜率宏观 |
| 结论 | **≠ 真人平衡** | 平衡以 Game AI/配置为准 |

更完整的设计口径见 [`Docs/RL_自博弈训练.md`](../../Docs/RL_自博弈训练.md)。

## 目录

```text
rl/
  app/streamlit_app.py     # 可视化训练台
  config/                  # PPO yaml
  scripts/
    run_workbench.py
    run_train_env.py       # 拉起 Unity -rlTrain
    run_eval.py            # Unity -rlEval
  results/                 # mlagents-learn 输出（gitignore）
  reports/                 # 评估 JSON、trainer_pids.json（点击式启动的 PID 追踪）
  打开工作台.bat
```

## 安装

`mlagents 1.1` **只要 Python 3.10.x**（系统若是 3.14 会装不上，命令行也会报 `mlagents-learn` 找不到）。

```powershell
cd Tools/rl
py -3.10 -m venv .venv
.\.venv\Scripts\python -m pip install -U pip
.\.venv\Scripts\python -m pip install -r requirements.txt
```

之后 Trainer / 工作台都用 `.venv\Scripts\python`。Unity：打开 `Game/`，确认已解析 `com.unity.ml-agents` 4.0.3。

## 工作台（推荐）

```bash
python scripts/run_workbench.py
```

或双击 [`打开工作台.bat`](打开工作台.bat) / [`start_workbench.bat`](start_workbench.bat)（**http://localhost:8501**；logic_qa 为 8502）。

页签：

1. **说明** — 课程与配置预览  
2. **训练启动** — 生成 `mlagents-learn` / Unity 命令；也可**点击式启动**（① Trainer / ② Unity / ③ 一键 A+B），带 PID 追踪、日志尾部和停止按钮  
3. **评估** — 固定局数 `-rlEval`，展示 `reports/rl_eval.json`  
4. **结果** — 浏览 `results/<run-id>/`

### 实时训练指标（训练启动 / 训练埋点页签顶部）

两个页签顶部都有**每 5 秒自动刷新**的实时指标条：

- **Trainer 实时步数** — 解析 `trainer_<run-id>.log` 的 `Step:` 行（trainer 每 `summary_freq` 打点，比 tensorboard/checkpoint 实时得多）
- **本次训练用时** — 同日志的 `Time Elapsed`（trainer 自启动的累计训练时长）；日志未写入时回退到进程启动墙钟
- **累计局数** — Unity 侧 `training_progress.json` 的 `matches_total`（每局结束写入）
- **Unity 决策步** — 每次微操作 +1
- 下方进度条：实时步数优先于 checkpoint，显示对 `max_steps` 的预算完成度

「训练启动」页签底部的 **Trainer 日志尾部**同样每 5 秒自动刷新（此前需手动点刷新）。

### 点击式启动（训练页签底部）

无需敲命令行，后台分离启动长驻进程（不阻塞页面）：

- **① 启动 Trainer (A)** — 拉起 `mlagents-learn`，PID 记录到 `reports/trainer_pids.json`，日志 `trainer_<run-id>.log`
- **② 启动 Unity 环境 (B)** — 拉起 `run_train_env.py`（Unity 为其子进程，默认 **无头** `-batchmode -nographics`）
- **③ 一键 A + B** — 先启 Trainer，轮询日志等 `Listening on port 5004` 就绪后**自动**拉起 Unity
- 下方显示 Trainer 运行状态、日志实时尾部；「刷新状态」手动刷新，「停止 Trainer」强制终止（`taskkill`）

注意：停止 Trainer 不会连带停止 Unity 子进程；如要完整停训，请分别停止。跨 run-id 的 Trainer 冲突会告警（端口 5004 被占）。

### 环境来源：无头 Editor / 广场 Player（面板「环境来源」单选）

面板里训练参数区新增「环境来源」选择：

- **Editor（推荐）** — 官方推荐：改代码直接跑。默认勾选**无头模式**（`-batchmode -nographics`，省美术渲染）；取消勾选即带画面，方便观察对局。
- **Player 广场（多开加速）** — 先 build 训练 exe，再设并行数 `num_envs`：trainer 通过 `env_path` **自动拉起 N 个 Player 进程**喂训练，吞吐约线性提升（受 CPU 核心数限制）。此模式下终端 B 无需手动启动，按钮 ② 不可用、③ 就绪后自动拉起多环境。

```powershell
# 无头 Editor（面板默认）
python scripts/run_train_env.py --curriculum bootstrap --role human          # 等价 --headless
python scripts/run_train_env.py --curriculum bootstrap --role human --no-headless   # 带画面调试

# 广场模式（面板自动生成 ppo_<curriculum>_square.yaml，Trainer 用它）
Unity -batchmode -nographics -quit -projectPath Game -executeMethod RlBuildScript.Build   # 只 build 一次
# 然后面板选「Player 广场」设 num_envs，点 ③ 一键即可
```

### 广场模式注意

- **需先 build**：Unity 菜单 `Tools/RL/构建训练 Player`（输出到 `Tools/rl/build/ElephantCrisis.exe`），或命令行 `-executeMethod RlBuildScript.Build -rlBuildOut <dir>`。
- build 出的 exe 带 `-rlTrain -rlCurriculum -rlRole`（selfplay 另加 `-rlLeagueDir`）即被 `RlTrainingRunner` 接管，无需 Editor。
- 面板自动生成的 square 配置写在 `config/ppo_<curriculum>_square.yaml`，`env_settings` 含 `env_path` + `num_envs` + `env_args`；重复生成会覆盖。

## 命令行

**务必先关闭**已打开该 Game 工程的 Unity Editor。

### Bootstrap（1 RL vs 3 启发式）

```powershell
# 终端 A（务必用 .venv）
.\.venv\Scripts\python -m mlagents.trainers.learn config/ppo_bootstrap.yaml --run-id ec_bootstrap_v1 --results-dir results --force

# 终端 B（默认无头；加 --no-headless 看画面）
python scripts/run_train_env.py --curriculum bootstrap --role human
```

### Self-play

```powershell
.\.venv\Scripts\python -m mlagents.trainers.learn config/ppo_selfplay.yaml --run-id ec_selfplay_v1 --results-dir results
python scripts/run_train_env.py --curriculum selfplay --league-dir results/ec_selfplay_v1
```

### 评估

```bash
python scripts/run_eval.py --episodes 50 --seed 100 --role human --out reports/rl_eval.json
```

## 动作 / 观测

| 项 | 说明 |
|----|------|
| BehaviorName | `ElephantCrisis` |
| 观测 | 96 维向量 |
| 动作 | Op(12) × dx(7) × dy(7) × target(4) + mask |

## 与质量工具边界

| 工具 | 关系 |
|------|------|
| LogicSim / balance | 更强策略可作对手；RL 胜率 ≠ 平衡结论 |
| logic_qa | 可选扫 RL 局防钻空 |
| perf | 训练必须 lean；与客户端 perf 隔离 |

## 奖励（当前实现）

| 事件 | 奖励 |
|------|------|
| 胜利 | +2.0 |
| 死亡（且未胜） | -1.0 |
| 失败但存活 | -0.5 |
| 平局（无人存活等） | -0.2 |
| 获得 / 失去人偶 | +0.1 / -0.1 |
| 击杀 | +0.25 |
| 造成伤害 | +0.003 × dealt（单步上限 0.1） |
| 受到伤害 | -0.001 × taken |
| 非法动作兜底 | -0.01 |

无「任意用卡 +0.01」。RL **无回合上限**：不强制 `max_rounds`，由熔岩缩圈与规则决出胜负/平局。

## 训练埋点

每局结束写入：

- `reports/training_matches.jsonl` — 逐局
- `reports/training_stats.json` — **最近 100 局**汇总（含 `rl_decisions_total`）
- `reports/training_progress.json` — 轻量进度（约每 50 次决策刷新）

工作台 **② / ⑤** 顶部显示 **Trainer 已学步数**（`results/<run-id>` + `max_steps` 进度条）和 **Unity 决策步**。点「刷新」更新。

Unity Console 也会打 `[RlStats] last …` 一行摘要。

## 产物

- `results/<run-id>/` — checkpoint / 摘要  
- `reports/rl_eval.json` — 评估胜负统计  
- Unity 日志：`unity_rl_train.log` / `unity_rl_eval.log`
