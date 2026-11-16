# 架构 · Tools

> 适用：`Tools/`（5 个目录，约 3,400 行）
> 配套：[`架构_总纲.md`](./架构_总纲.md)、[`架构_Game.md`](./架构_Game.md)

---

## 一、五个目录

| 目录 | 文件 | 行数 | 一句话 | 入口 |
|---|---|---|---|---|
| `lab/` | 9 | 789 | **底座**：跑批、实验记录、事件读取、报告、仪表盘、数据库 | `python Tools/lab/run.py` |
| `qa/` | 7 | 1,703 | **健康检查**：14 组不变量 + 等价性 + 45 个测试 | `python Tools/qa/run.py` |
| `balance/` | 5 | 354 | **数值**：指标 + Wilson 区间 + 配对比较 | `python Tools/balance/run.py` |
| `config/` | 2 | 384 | 设计表 → `game_rules.json` | `python Tools/config/build.py` |
| `runner/` | 5 | 191 | C#：`SimRunner`（跑批）+ `CoreCheck`（引擎无关性） | `dotnet run --project ...` |

**为什么只有五个**：一个目录 = 一个入口（或一个独立构建单元）。同语言、同入口的东西不拆。

---

## 二、数据流

```
Design/*.csv ──[config/build.py]──▶ game_rules.json
                                          │
                              Game/Assets/Scripts/Simulation
                                          │
                        [runner/SimRunner] 编译并运行（不需要 Unity）
                                          │
                                  临时目录（传输管道）
                                          │  导入后立即删除
                                          ▼
                                    数据库（lab/store.py）
                                   SQLite 或 MySQL
                                          │
                    ┌─────────────────────┼─────────────────────┐
                    ▼                     ▼                     ▼
              qa/run.py            balance/run.py        lab/dashboard.py
            不变量 + 等价性          指标 + 区间            单文件 HTML
                    └─────────────────────┴─────────────────────┘
                                     reports/
```

**磁盘上不存对局数据。** 跑批器产出的文件只是"跑批器 → 数据库"的管道，导入后立刻删除。

---

## 三、四个入口

```bash
# 1. 跑一批对局（写数据库；默认本地 SQLite）
python Tools/lab/run.py --name v1 --matches 100 --seed 50000

# 2. 健康检查（阻断违规 → 退出码 1）
python Tools/qa/run.py --experiment v1
python Tools/qa/run.py --experiment v2 --baseline v1     # 等价性对拍

# 3. 数值指标（只出报告，不阻断）
python Tools/balance/run.py --experiment v1 --baseline v0

# 4. 生成仪表盘（单文件 HTML，双击打开）
python Tools/lab/dashboard.py --checks
```

换 MySQL 只加 `--dsn "mysql://user:pw@host/elephant_crisis"`，表会自动创建。

---

## 四、qa 的判定分级

| 级别 | 含义 | 是否阻断 |
|---|---|---|
| **阻断** | 任何情况下都不允许发生 | ✅ 退出码 1 |
| **参考（advisory）** | 依赖快照细节、已知会误报 | ❌ 默认不拦，`--strict` 才拦 |

**当前没有参考项**——两条曾经的误报规则都已从根上修掉：

| 曾经的误报 | 根因 | 修法 |
|---|---|---|
| `dmg_magic_formula` | 用**快照时刻**的法抗去核算**受击时刻**的伤害 | 引擎在伤害事件里记录受击时刻的 `mr`；规则优先用它，老数据才回退快照 |
| `quasi_crossbow_requires_equipped` | 弩被丢弃/偷走时蓄力没清除，出现"没弩却仍蓄力" | 引擎把"蓄力 ⇒ 弩在身"做成构造性质（读 `CrossbowCharged` 时自动清），规则升级为阻断 |

另外规则本身也补了一条真实约束：**溢出击杀合法**（目标只剩 2 血吃 10 点法伤只结算 2），
只有"多扣"才是 bug。

**原则：不变量必须零误报。** 一条会误报的规则留在门禁里，等于让所有人忽略报告。

---

## 五、数据库

四张表（`lab/store.py`，建表幂等）：

| 表 | 内容 |
|---|---|
| `experiments` | 实验名、种子区间、**配置指纹**、**git 版本** |
| `matches` | 每局成绩单 |
| `match_events` | 逐事件（payload 存原 JSON） |
| `match_roles` | 每角色结局（便于 SQL 统计） |

**为什么记配置指纹与 git 版本**：判定结论只有"可比"才有意义。半年后回看，必须知道那批数据是哪版跑出来的。

**规模提示**：明细放行式数据库在几百局规模是合理的；到几万局应改为 Parquet / DuckDB（只需实现一个新的 `Store`）。

---

## 六、测试

```bash
python -m pytest Tools/qa/tests -q      # 45 通过，0.2 秒
```

这些测试守的是 `qa/checks.py` 里那 14 组规则本身——**规则写错了，测试会先叫**。

---

## 七、CI 现状与计划

尚未接入。建议的最小 workflow：

| 阶段 | 命令 | 耗时 | 抓什么 |
|---|---|---|---|
| 秒级 | `dotnet build Tools/runner/CoreCheck` | 2 秒 | 核心被写入 Unity 依赖 |
| 秒级 | `pytest Tools/qa/tests` | 0.2 秒 | 规则写错 |
| 秒级 | `build.py` + `git diff --exit-code game_rules.json` | 1 秒 | 改了表没提交生成物 |
| 分钟级 | `lab/run.py` → `qa/run.py` | ~15 秒 | 结算逻辑坏了 |

**原则**：不变量与等价性阻断；数值只出报告（数值是设计选择，不让 CI 替人决定）。
