# 数值平衡分析 + 顾问（balance_analysis）

消费 sim 产出的 **宏观结果**（`match_*/meta.json`），先聚合为 `balance_report.json`（胜率 / 死亡率 / 存活回合），再（可选）调用 LLM 给出调参建议。

**不读** `events.jsonl`——那是 `logic_qa` 的微观日志。聚合所需数据全部来自 `meta.json` 的 `per_role`（sim 已在 C# 里算好死亡回合）。

## 与上下游的关系

```text
sim（造数，只认 -collect 开关）
  ├─ match_*/meta.json ─────► balance_analysis（本目录）
  │                             ├─ aggregate → balance_report.json
  │                             └─ advisor(LLM) → advisor_suggestions.md
  └─ match_*/events.jsonl ──► logic_qa（微观规则校验）
```

- 本目录只调用 `elephant_sim`（sim 对外暴露的 SDK：`unity_batch` / `log_io` / `models`），不 import `logic_qa`。
- `logic_qa` 同理，只调用 `elephant_sim`，不 import 本目录。

## 目录

```text
balance_analysis/
  extract.py           # 单局 meta → 角色级指标（死亡回合 / 存活）
  aggregate.py         # 多局聚合 → balance_report.json
  llm_client.py        # OpenAI 兼容 Chat API
  preprocess.py        # 报告 + 规则 → prompt 上下文
  config/balance.yaml  # 聚合默认约定
  config/advisor.yaml  # LLM API / 预处理配置
  scripts/run_balance.py         # 造数(collect=balance) + 聚合
  scripts/run_advisor.py         # 预处理 + LLM 建议
  scripts/diff_balance_reports.py# 对比两份报告
  app/streamlit_app.py           # 工作台 (8503)
  tests/
```

## 安装与运行

```bash
cd Tools/balance_analysis
pip install -r requirements.txt

# 1) 造数 + 聚合（只写 meta.json，不写 events）
python scripts/run_balance.py --matches 100 --seed 1

# 2) LLM 建议（--dry-run 只生成 prompt，不调 API）
python scripts/run_advisor.py --dry-run
python scripts/run_advisor.py

# UI
python scripts/run_workbench.py   # 或双击 打开工作台.bat (8503)
```

## 喂给模型的内容（预处理）

| 纳入 | 不纳入（默认） |
|--|--|
| `balance_report.json` 精简字段 | 每局完整事件流 events.jsonl |
| `game_rules.yaml` | 全部战报原文 |
| `Docs/规则_游戏概述与规则.md` 摘录 | |
| `Docs/规则_角色与基础属性.md` 摘录 | |

## API Key

任选其一（**环境变量优先于配置文件**）：

1. `config/advisor.yaml` 的 `api.api_key`（未公开前可用；开源前请清空）
2. 环境变量 `BALANCE_ADVISOR_API_KEY` 或 `OPENAI_API_KEY`

产物在 `reports/`：`balance_report.json`、`advisor_context.json`、`advisor_prompt.md`、`advisor_suggestions.md`。

测试：`pytest -q`
