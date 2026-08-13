# 对局造数（sim）

**造数真源：Unity `Game/Assets/Scripts`（LogicSim）**。  
本目录只负责拉起批跑、产出 `match_*/meta.json`（宏观结果）+ `match_*/events.jsonl`（微观事件，按需）。解析 / 建议由下游各自负责。

## 与下游的关系

```text
sim（本目录：只造数，只认 -collect 开关）
  ├─ elephant_sim/            # 对外 SDK：unity_batch（拉起 Unity）/ log_io（读产出）/ models（共享枚举）
  ├─ scripts/run_batch.py     # 造数入口
  ├─ match_*/meta.json ─────► balance_analysis（宏观聚合 + LLM 建议）
  └─ match_*/events.jsonl ──► logic_qa（微观规则校验）
```

- 下游只 import `elephant_sim`（本目录的 SDK），互不调用。
- 本目录不 import `balance_analysis` / `logic_qa`（它们的专有逻辑不在 sim 内）。

## 目录

```text
sim/
  elephant_sim/        # unity_batch、log_io、models（下游共用的 SDK）
  scripts/run_batch.py # 造数入口（--collect 开关）
  output/              # LogicSim → match_*/meta.json + events.jsonl
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

**采集模式 `--collect`**（决定产出哪些文件，`sim` 只认这个开关，对下游零感知）：

| 模式 | 写 `meta.json` | 写 `events.jsonl` | 谁消费 |
|--|--|--|--|
| `balance` | ✅（含 per_role 死亡回合） | ❌ | `balance_analysis`（省 I/O/CPU） |
| `logic` | ✅ | ✅ | `logic_qa`（+ balance 顺带可用） |
| `both`（默认） | ✅ | ✅ | 两者 |

```bash
python scripts/run_batch.py --matches 20 --collect balance   # 只跑宏观
python scripts/run_batch.py --matches 20 --collect logic     # 只跑微观日志
```

`meta.source = game_logic_sim`。注意：`logic`/`both` 的产物是 `balance` 的超集，先跑 logic 可同时喂 balance；但 `balance` 只留宏观摘要，**无法反推微观日志**，想再 QA 规则需用 `logic` 重跑。
