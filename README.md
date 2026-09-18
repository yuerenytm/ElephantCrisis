# 象群危机（ElephantCrisis）

```text
ElephantCrisis/
  Docs/                 # 规则与计划文档
  Game/                 # Unity 游戏本体 —— 请用 Unity 打开此文件夹
  Tools/                # 配套工具
    sim/                # 逻辑对局造数（Game LogicSim）
    logic_qa/           # 逻辑微观检测（基线 / 异常 / Streamlit）
    balance_analysis/   # 数值平衡统计 + LLM 顾问（吃 meta / balance 报告）
```

## 两件套怎么分工

| | 跑什么 | 看什么 |
|--|--|--|
| **logic_qa** | 逻辑对局日志（与 balance **同一套**造数） | **微观**：行动/状态是否违规 |
| **balance**（`balance_analysis`） | 同上 | **宏观**：胜率、死亡率、存活回合… |

逻辑侧：**一套造数，两套分析**。

## Unity

用 Unity Hub / Editor **打开 `Game/`**（不要打开仓库根目录）。

## 工具速查

```bash
# 逻辑对局造数（Game Scripts 真源）
cd Tools/sim
python scripts/run_batch.py --matches 50 --out output

# 宏观平衡
cd ../balance_analysis
python scripts/run_balance.py --input ../sim/output --out reports

# 微观逻辑 QA
cd ../logic_qa
python scripts/run_baseline.py --input ../sim/output --out reports
python scripts/run_workbench.py
```

共用数值配置：`Game/Assets/StreamingAssets/Config/game_rules.yaml`。
