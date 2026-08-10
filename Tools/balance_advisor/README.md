# 数值平衡顾问（LLM）

读 **预处理后的统计** + **游戏规则摘要**，调用兼容 OpenAI 的 Chat API，给出调参建议。

位于 `Tools/balance_advisor/`。规则文档仍读仓库根目录 `Docs/`；数值配表读 `Game/Assets/StreamingAssets/Config/game_rules.yaml`。

> **不要**把 `sim/output` 里上百局完整 `events.jsonl` 全喂给模型。  
> 先用 `Tools/sim` 聚合成 `balance_report.json`，再把精简上下文送给 API。

## 喂给模型的内容（预处理）

| 纳入 | 不纳入（默认） |
|--|--|
| `balance_report.json` 精简字段 | 每局完整事件流 |
| `game_rules.yaml` | 全部战报原文 |
| `Docs/规则_游戏概述与规则.md` 摘录 | |
| `Docs/规则_角色与基础属性.md` 摘录 | |

## 安装与运行

```bash
cd Tools/balance_advisor
pip install -r requirements.txt

# CLI
python scripts/run_advisor.py --balance-report ../sim/reports/balance_report.json --dry-run
python scripts/run_advisor.py --balance-report ../sim/reports/balance_report.json

# UI
python scripts/run_workbench.py
```

或资源管理器中双击 [`打开工作台.bat`](打开工作台.bat)（**http://localhost:8503**）。

## API Key

任选其一（**环境变量优先于配置文件**）：

1. `config/advisor.yaml` 的 `api.api_key`（未公开前可用；开源前请清空）  
2. 环境变量 `BALANCE_ADVISOR_API_KEY` 或 `OPENAI_API_KEY`

产物在 `reports/`：`advisor_context.json`、`advisor_prompt.md`、`advisor_suggestions.md`。
