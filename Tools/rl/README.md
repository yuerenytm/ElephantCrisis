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
  reports/                 # 评估 JSON
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

或双击 [`打开工作台.bat`](打开工作台.bat) / [`start_workbench.bat`](start_workbench.bat)（一般是 http://localhost:8501）。

页签：

1. **说明** — 课程与配置预览  
2. **训练启动** — 生成 `mlagents-learn` / Unity 命令，可一键拉 Unity 环境  
3. **评估** — 固定局数 `-rlEval`，展示 `reports/rl_eval.json`  
4. **结果** — 浏览 `results/<run-id>/`

## 命令行

**务必先关闭**已打开该 Game 工程的 Unity Editor。

### Bootstrap（1 RL vs 3 启发式）

```powershell
# 终端 A（务必用 .venv）
.\.venv\Scripts\python -m mlagents.trainers.learn config/ppo_bootstrap.yaml --run-id ec_bootstrap_v1 --results-dir results --force

# 终端 B
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
