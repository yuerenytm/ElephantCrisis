# RL 自博弈训练（Unity ML-Agents）

| 项 | 内容 |
|----|------|
| 状态 | 环境与课程管道已落地；长训与人口收敛需算力 |
| 真源 | `Game/Assets/Scripts` + `GameMode.RlTraining` lean |
| 工具目录 | [`Tools/rl/`](../Tools/rl/README.md) |
| 包 | `com.unity.ml-agents` 4.0.3（`Game/Packages/manifest.json`） |

## 目标

四人 FFA **自博弈**（参数共享、观测含角色 one-hot），经 bootstrap → self-play → league，最终可进 LogicSim/对战作对手。

## 验收口径（分阶段）

1. **A**：固定评估下，RL 席对 `SimpleHeuristicAi` 胜率可复现上升。  
2. **B**：四席自博弈胜率接近 25%±带宽，且对启发式有优势。  
3. **C**：导出策略可供批跑；**不等于真人数值平衡**。

## 设计摘要

- 1 RL step = 一次微操作（对齐原 AI 泵）。  
- 离散分支 + action mask。  
- 终局：胜 +2 / 死 -1 / 败存活 -0.5 / 超时平 -0.2；过程：人偶 ±0.1、击杀 +0.25、伤害 0.003× / 受伤 0.001×。  
- 埋点：`Tools/rl/reports/training_stats.json`（滚动 100 局胜率·死亡率·领袖宣言）。  
- Bootstrap：`-rlCurriculum bootstrap`。  
- Self-play：`-rlCurriculum selfplay` + `ppo_selfplay.yaml` 的 `self_play` 块 + `-rlLeagueDir`。

## 与 balance / logic_qa

- 平衡报告仍基于启发式或明确标注的 RL 后端；勿把 RL 胜率直接写成「角色强度定论」。  
- 规则正确性继续用 logic_qa；RL 可能钻空子时应用基线扫描。

## 快速入口

- 说明与命令：[`Tools/rl/README.md`](../Tools/rl/README.md)  
- 工作台：`python Tools/rl/scripts/run_workbench.py` 或双击 `Tools/rl/打开工作台.bat`
