# 逻辑对局造数 + 平衡宏观统计

**造数真源：Unity `Game/Assets/Scripts`（LogicSim）**。  
本目录拉起批跑、写出 `match_*/events.jsonl`，并做 **宏观** 平衡聚合。

| 谁用这些日志 | 用途 |
|--|--|
| [`../logic_qa/`](../logic_qa/README.md) | **微观**：规则违规 / 异常状态 |
| 本目录 `balance_analysis` | **宏观**：胜率、死亡率、存活回合… |
| [`../perf_analysis/`](../perf_analysis/README.md) | **不用**这份日志（实机性能另一套采集） |

独立 Python 规则引擎已移除；勿再维护双轨数值配置。

## 目录

```text
sim/
  elephant_sim/        # log_io、Unity 编排
  balance_analysis/    # 宏观指标聚合
  scripts/run_batch.py
  scripts/run_balance.py
  scripts/diff_balance_reports.py
  output/              # LogicSim → match_*/events.jsonl
  reports/
```

数值配置（唯一权威）：`../../Game/Assets/StreamingAssets/Config/game_rules.yaml`。

## 依赖

- `pip install -r requirements.txt`
- Unity Editor（或 `UNITY_EDITOR` 指向 `Unity.exe`）
- 批跑前关闭已打开该 Game 工程的 Editor

## 造数

```bash
cd Tools/sim
python scripts/run_batch.py --matches 20 --seed 1 --out output
```

`meta.source = game_logic_sim`。同批日志可先跑 balance，再跑 logic_qa，无需重打。

## 宏观平衡

```bash
python scripts/run_balance.py --matches 50 --seed 1 --out reports
# 或已有 output/：
python scripts/run_balance.py --input output --out reports
python scripts/diff_balance_reports.py reports_a/balance_report.json reports_b/balance_report.json
```

**口径：当前 Game 启发式 AI + 规则下的相对强弱，≠ 真人平衡。**
