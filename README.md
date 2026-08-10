# 象群危机（ElephantCrisis）

```text
ElephantCrisis/
  Docs/                 # 规则与计划文档
  Game/                 # Unity 游戏本体 —— 请用 Unity 打开此文件夹
  Tools/                # 配套工具
    sim/                # 逻辑对局造数（Game LogicSim）+ 平衡宏观统计
    logic_qa/           # 逻辑微观检测（基线 / 异常 / Streamlit）
    balance_advisor/    # 数值平衡 LLM 顾问（吃 balance 报告）
    perf_analysis/      # 实际对局性能（帧率 / 美术加载 / 玩法共现）
    rl/                 # ML-Agents 自博弈（工作台 + PPO 配置）
```## 三件套怎么分工

| | 跑什么 | 看什么 |
|--|--|--|
| **logic_qa** | 逻辑对局日志（与 balance **同一套**造数） | **微观**：行动/状态是否违规 |
| **balance**（`sim/balance_analysis`） | 同上 | **宏观**：胜率、死亡率、存活回合… |
| **perf_analysis** | **实际对局**（带表现的 Unity 局） | **时刻**：帧耗时、资源加载、技能/演算附近卡顿 |

逻辑侧：**一套造数，两套分析**。性能侧：**另一套实机采集**，日志路径与 schema 都不同。

## Unity

用 Unity Hub / Editor **打开 `Game/`**（不要打开仓库根目录）。

## 工具速查

```bash
# 逻辑对局造数（Game Scripts 真源）
cd Tools/sim
python scripts/run_batch.py --matches 50 --out output

# 宏观平衡
python scripts/run_balance.py --input output --out reports

# 微观逻辑 QA
cd ../logic_qa
python scripts/run_baseline.py --input ../sim/output --out reports
python scripts/run_workbench.py

# 实际对局性能采集 + 离线分析
python ../perf_analysis/scripts/run_perf_collect.py
cd ../perf_analysis
python tools/analyze_client_perf.py --input samples/session_demo
```

共用数值配置：`Game/Assets/StreamingAssets/Config/game_rules.yaml`。
