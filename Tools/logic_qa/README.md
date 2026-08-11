# 逻辑 QA（微观：规则对不对）

对 **逻辑对局** 日志做微观检测：基线硬断言 + Isolation Forest 可疑状态 + Streamlit 工作台。

造数与平衡共用 [`../sim/`](../sim/README.md) 的 Game LogicSim 输出；本工具只负责「事件合不合法 / 状态像不像 bug」，不做胜率统计。

实际对局的帧率 / 美术加载见 [`../perf_analysis/`](../perf_analysis/README.md)（另一套采集，勿混用 JSONL）。

## 目录

```text
logic_qa/
  anomaly_analysis/   # 基线 + IF
  app/                # Streamlit
  scripts/
  config/runtime/
  models/ reports/
```

## 安装

```bash
cd Tools/logic_qa
pip install -r requirements.txt
```

## 用法

```bash
# 造数（与 balance 同一套）
cd ../sim
python scripts/run_batch.py --matches 30 --seed 1 --out output

# 微观基线
cd ../logic_qa
python scripts/run_baseline.py --input ../sim/output --out reports

# 一键造数 + 基线 +（可选）异常
python scripts/run_qa_pipeline.py --matches 30 --train

# 工作台
python scripts/run_workbench.py
```

或资源管理器中双击 [`打开工作台.bat`](打开工作台.bat)（会开浏览器，**http://localhost:8502**；勿与 RL 的 8501 混淆）。

测试：`pytest -q`

## 怎么读结果

- **基线失败**（`baseline_bug`）：优先当真违规查  
- **`ai_suspect`**：软告警，抽查用  
- **`clean`**：两边都没报警  

缺陷故事请用真实跑批自行沉淀。

## 基线与现行规则（摘要）

近期规则变更后基线已对齐，重点包括：

| 规则 id | 含义 |
|---------|------|
| `dmg_true_no_mitigation` | 真伤 `dealt` 须为 `raw` 或 `0`（护身符全免） |
| `dmg_physical_bound` | 物伤 `dealt` 在 `[0, raw]`（减防只能减免） |
| `dmg_magic_bound` | 法伤 `dealt` 在 `[0, raw]`（法抗 `⌊raw × (1 − 法抗%)⌋`，只能减免） |
| `dmg_magic_formula` | 法伤精确公式校验（`melee_magic`/`molotov` 对快照 `magic_resist` 逐条核对；`0` 视为护盾全额吸收） |
| `no_turn_start_draw` | 禁止旧式「draw → turn_start」行动开始摸牌 |
| `hidden_break_on_attack` | 识别 `melee` / `melee_magic` / `melee_pierce` / `bow` / `bomb` 等 via；破隐 op 含 `break_attack` / `break_attacked` / `break_burning` / `jungle_flame` |
| `dying_no_draw` 等 | 濒死不可抽牌/用卡（仍有效） |
| `quasi_nonneg` | 准状态计数器（摩托回合/红牛/CD/护盾/濒死倒数等）不得为负 |
| `quasi_crossbow_requires_equipped` | 弩已蓄力时须仍装备弩（卸下会清蓄力） |
| `quasi_motorcycle_requires_equipped` | 摩托发动中须仍装备摩托车 |

snapshot 单位新增**准状态**字段（非具名状态但影响结算，供上述基线）：

| 字段 | 含义 |
|------|------|
| `bag_items` | 背包物品详情 `{kind, charges, equipped}`（甲耐久、诅咒刃次数、弹药支数） |
| `magic_resist` | 当前法抗（基础+地形+天气，已夹下限 0），供法伤精确公式校验 |
| `crossbow_charged` / `crossbow_charged_this_action` | 弩蓄力 / 本行动已蓄力 |
| `motorcycle_active` | 摩托发动剩余完整回合 |
| `pending_extra_actions` | 红牛预约的额外行动次数 |
| `skill_cooldown` / `skill_shield` / `amulet_shield` / `amulet_buff` | 技能 CD / 技能护盾 / 护身符护盾与增益 |
| `adrenaline_rounds` | 肾上腺素剩余回合 |
| `hidden_from_jungle` | 隐匿是否来自丛林 |
| `dying_rounds` | 濒死剩余行动数（进入时 12） |
| `leader_declared` | 是否已发动领袖宣言 |
| `flamethrower_cooldown` | 喷火冷却 |

开局牌库散落到地图后，snapshot 的 `deck_draw` 通常为 **0**；异常检测特征仍保留该字段。
