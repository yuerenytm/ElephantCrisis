# 架构 · Tools

> 适用：`Tools/`（三个子项目，约 4300 行 Python）
> 定位：**离线分析工具链**，不属于游戏运行时，通过"读 Unity 产出的日志"工作。

---

## 1. 一句话

**一套造数，两套分析**：`sim` 调 Unity 无头跑批产出对局日志，`logic_qa` 从"过程"里抓规则违规，`balance_analysis` 从"结果"里算数值强弱。

---

## 2. 全景

```
                 ┌──────────────────────────────┐
                 │  Unity 编辑器（批处理模式）    │
                 │  -executeMethod LogicSim...   │
                 └──────────────┬───────────────┘
                                │ 写文件
                                ▼
                    sim/output/match_*/
                    ├─ meta.json      成绩单（每局必有）
                    ├─ events.jsonl   逐事件流（collect=logic/both）
                    └─ batch_summary.json
                                │
              ┌─────────────────┴──────────────────┐
              ▼                                    ▼
   ┌─────────────────────┐              ┌─────────────────────┐
   │ logic_qa            │              │ balance_analysis    │
   │ 读 events           │              │ 读 meta             │
   │ ├ baseline 规则校验  │              │ ├ extract/aggregate │
   │ ├ anomaly 异常检测   │              │ ├ preprocess        │
   │ └ streamlit 回放     │              │ ├ llm advisor       │
   └──────────┬──────────┘              │ └ streamlit 工作台   │
              │                         └──────────┬──────────┘
              ▼                                    ▼
      reports/baseline_report.json        reports/balance_report.json
      reports/anomaly_report.json         reports/advisor_suggestions.md
```

**关键点**：两个分析器**互不调用**，都只依赖 `sim/output` 的目录约定；它们可以分别重跑而不用重跑游戏。

---

## 3. `sim` —— 唯一的数据生产者

```
sim/
├─ scripts/run_batch.py      CLI：解析参数 → 调 Unity → 汇总
├─ elephant_sim/
│  ├─ unity_batch.py         环境探测 + 拼命令行 + 错误翻译
│  ├─ log_io.py              读 match_*（meta / events）
│  └─ models.py              Role 枚举、ROLE_ORDER
```

### 3.1 进程边界

```
Python 进程                     Unity 进程（-batchmode -nographics）
  │                                 │
  ├─ subprocess.run(cmd) ──────────▶│ 加载工程、编译、进 PlayMode
  │                                 ├─ LogicSimRunner 连跑 N 局
  │                                 ├─ 写 match_*/meta.json + events.jsonl
  │◀──── returncode ────────────────┤ Quit(0) → Editor 退出
  ├─ 读目录 → batch_summary.json
```

**契约只有两样**：命令行参数、产物文件格式。没有共享代码、没有 API。

### 3.2 CLI

```bash
python scripts/run_batch.py --matches 100 --seed 1 \
  --max-rounds 100 --collect both --timeout 1800 --out output
```

| 参数 | 含义 |
|---|---|
| `--matches` | 局数 |
| `--seed` | 起始种子，逐局 +1（可复现） |
| `--max-rounds` | 单局完整回合上限 |
| `--collect` | `balance` 只写 meta / `logic` 只写 events / `both` |
| `--timeout` | 整批超时（超时返回 124） |
| `--no-clean` | 保留旧 `match_*` |

### 3.3 产物 schema

`meta.json`：

```json
{"seed":900,"strategies":{...},"winner":"elephant","reason":"last_standing",
 "full_rounds":24,"events":481,"collect":"both",
 "per_role":{"elephant":{"death_round":null,"survived":true},...},
 "source":"game_logic_sim"}
```

`events.jsonl`：每行一个事件，`t` 单调递增；类型包括
`match_start / turn_start / turn_end / snapshot / damage / move / status / clock / weather / equip / bomb / lava_shrink / heal / dying / death / match_end`。

---

## 4. `logic_qa` —— 抓规则违规

```
logic_qa/
├─ pathsetup.py             把 logic_qa 与 sim 加进 sys.path
├─ anomaly_analysis/
│  ├─ baseline/checks.py    13 条规则检查（734 行）
│  ├─ baseline/runner.py    遍历对局 → 产出 baseline_report.json
│  ├─ detection/features.py 快照 → 特征向量
│  ├─ detection/detector.py IsolationForest 异常检测
│  └─ detection/runner.py   训练 + 扫描
├─ scripts/                 run_qa_pipeline / run_baseline / run_anomaly / train_anomaly
├─ app/                     Streamlit 工作台（回放 + 报告）
└─ tests/                   conftest + 基线规则测试（约 700 行）
```

### 4.1 两级校验

| 级别 | 方法 | 能发现 |
|---|---|---|
| **规则基线** | 写死 13 条不变量 | 明确的规则违反（可解释） |
| **异常检测** | IsolationForest 无监督 | 规则没写、但"样子不对"的对局 |

**为什么要两级**：规则覆盖不到的情况（新 bug、数值异常组合），靠统计离群补。

### 4.2 13 条基线检查

```
_check_damage_rules               伤害来源/数值是否合法
_check_hp_bounds                  HP 不越界
_check_rain_clears_burn           雨天必须灭掉着火
_check_dying_no_draw              濒死不能再抽牌
_check_dead_no_action             死人不能再行动
_check_bag_over_cap_end           超重不能结束行动
_check_lava_shrink_monotonic      熔岩圈只增不减
_check_hidden_break_on_attack     攻击/被攻击破隐匿
_check_dmg_hp_consistent          事件里的伤害与快照 HP 对账
_check_no_turn_start_draw         行动开始不应摸牌
_check_quasi_nonneg               准静态数值不为负
_check_quasi_equipped_dependency  装备依赖关系
_check_visibility_period_weather  能见度符合时段×天气表
```

### 4.3 流水线

```bash
python scripts/run_qa_pipeline.py --matches 100 --collect logic
```

```
[1/4] batch      （可选）调 sim 跑局
[2/4] baseline   逐局检查 → baseline_report.json + quarantine.txt
[3/4] train      （可选）用无违规对局训练 IForest → models/iforest.joblib
[4/4] anomaly    扫描全部对局 → anomaly_report.json
                 汇总 → qa_summary.json
退出码：有违规局则 1
```

**设计要点**：基线先跑，训练只用"基线通过"的对局——避免把 bug 当正常样本学进去。

---

## 5. `balance_analysis` —— 算强弱

```
balance_analysis/
├─ extract.py        meta → 每局 4 条角色记录
├─ aggregate.py      胜率/死亡率/存活分位数
├─ preprocess.py     报告+规则+文档 → prompt 上下文
├─ llm_client.py     OpenAI 兼容 /chat/completions
├─ scripts/          run_balance / run_advisor / run_workbench / diff_balance_reports
├─ app/              Streamlit（统计 / 建议 / prompt 预览）
└─ tests/            extract + aggregate 的口径测试
```

### 5.1 数据流

```
match_*/meta.json
  → extract_match          一局 → 4 条记录（won/died/survival_rounds）
  → aggregate_records      按角色汇总（win_rate / death_rate / 分位数）
  → balance_report.json
       ├─ format_console_table     控制台表
       ├─ diff_balance_reports     调参前后对比
       └─ build_context            （+ game_rules.yaml + 规则文档摘录）
             → advisor_prompt.md（可审计）
             → chat_completion → advisor_suggestions.md
```

### 5.2 设计取舍

| 取舍 | 原因 |
|---|---|
| 只读 `meta.json` | 3000 局也能秒级统计；不需要 events |
| 存活回合对"活到最后"记整局长度 | meta 只有 `full_rounds`，属右截断 |
| 报告里写死 `disclaimer` | 防止把 AI 样本结论当真人平衡 |
| prompt 里给硬性约束 | 让 LLM 只基于聚合值、只改已有字段 |

---

## 6. 共享契约

| 契约 | 定义方 | 使用方 |
|---|---|---|
| 目录名 `match_<seed>` | `LogicSimRunner` | 所有分析器 |
| `meta.json` 字段 | `LogicMatchLogger.FinishMatch` | `balance_analysis`、`logic_qa` |
| `events.jsonl` 事件类型 | `LogicMatchLogger.Emit*` | `logic_qa` |
| `-collect` 三档语义 | `LogicSimRunner.ParseArgs` | 两个分析器 |

**改 schema 必须同时改三处**：`LogicMatchLogger`、`log_io` 的读取、以及 `checks.py`/`extract.py` 的消费逻辑。

---

## 7. 依赖与路径

```
python 侧：标准库 + feedparser 无关；依赖 PyYAML、numpy、scikit-learn、streamlit、pandas
路径策略：
  repo_root = ../../..（Tools/sim/elephant_sim）
  sys.path.insert（balance_analysis、logic_qa 都这么干）
```

**没有包安装、没有 pyproject**：三个子项目靠相对路径和 `sys.path` 拼接，直接在仓库里跑。

---

## 8. 扩展点

| 要加什么 | 改哪里 |
|---|---|
| 新规则检查 | `baseline/checks.py` 加 `_check_*`，在 `check_match_events` 注册 |
| 新统计指标 | `extract.py` 补字段 → `aggregate.py` 汇总 → `format_console_table` 展示 |
| 新异常特征 | `detection/features.py` 的 `_build_feature_names` + `_unit_vector` |
| 换 LLM | `config/advisor.yaml` 的 `base_url/model`，或环境变量 |
| 新分析子项目 | 新增 `Tools/<name>/`，约定只读 `sim/output` |

---

## 9. 已知债务

| 债务 | 说明 |
|---|---|
| **路径脆弱** | `repo_root()` 依赖 `parents[3]`，`Tools` 一挪就断 |
| **sys.path 注入** | `from aggregate import ...` 依赖入口脚本先插路径，换入口就 ImportError |
| **缺超时** | `subprocess.run` 在 `run_batch` 已有 `--timeout`，其他调用点仍是默认无限制 |
| **全量读 events** | `load_events` 一次性入内存；千局分析会吃内存 |
| **测试薄** | 只测 extract/aggregate 口径；基线 13 条规则有测试，但 anomaly 无 |
| **死代码** | `iter_matches`、`write_jsonl` 无调用方（保留为预留） |
| **密钥曾入库** | `advisor.yaml` 曾有明文 key，已作废并清空；改用环境变量 |
| **两套 Streamlit** | `logic_qa/app` 与 `balance_analysis/app` 各自独立，未抽公共组件 |

---

## 10. 速查命令

```bash
# 造数（100 局，写全量日志）
cd Tools/sim && python scripts/run_batch.py --matches 100 --collect both

# 规则校验 + 异常检测
cd Tools/logic_qa && python scripts/run_qa_pipeline.py

# 平衡统计
cd Tools/balance_analysis && python scripts/run_balance.py --input ../sim/output

# 调参前后对比
python scripts/diff_balance_reports.py reports/a.json reports/b.json

# LLM 建议（dry-run 不花钱）
python scripts/run_advisor.py --dry-run

# 工作台
python scripts/run_workbench.py
```
