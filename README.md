# 象群危机（ElephantCrisis）

四人 FFA 回合制棋盘游戏：象 / 人 / 猴 / 猫，在 18×18 地图上抢资源、互相伤害，最后活下来的赢。

```text
ElephantCrisis/
  Docs/                 # 文档（从 Docs/README.md 开始）
  Design/               # 策划表（数值真源：*.csv）
  Game/                 # Unity 游戏本体 —— 用 Unity 打开此文件夹
  Tools/                # 配套工具（见下）
```

## 一条命令看懂项目

```bash
python Tools/lab/run.py --name demo --matches 100 --seed 50000   # 跑 100 局（9 秒）
python Tools/qa/run.py --experiment demo                         # 查对错
python Tools/balance/run.py --experiment demo                    # 看数值
python Tools/lab/dashboard.py --checks                           # 生成 HTML 仪表盘
```

## 目录与职责

| 目录 | 是什么 | 入口 |
|---|---|---|
| `Game/Assets/Scripts/Simulation/` | **规则核心**：纯 C#，不认识 Unity | — |
| `Game/Assets/Scripts/View/` | 表现与交互：画面、输入、UI、相机 | — |
| `Game/Assets/Scripts/{App,AI,Editor}/` | 装配 / 决策 / 编辑器工具 | — |
| `Design/` | 策划表：46 张卡、7 地形、7 状态 | `python Tools/config/build.py` |
| `Tools/lab/` | 工具底座：跑批、实验、数据库、仪表盘 | `python Tools/lab/run.py` |
| `Tools/qa/` | 健康检查：14 组不变量 + 等价性 | `python Tools/qa/run.py` |
| `Tools/balance/` | 数值：指标 + 置信区间 + 配对 | `python Tools/balance/run.py` |
| `Tools/runner/` | C#：跑批器 + 引擎无关性检查 | `dotnet build Tools/runner/CoreCheck` |

## 核心设计

**规则只有一份，宿主可以换。**

```
Design/*.csv ──build.py──▶ game_rules.json
                                │
                    Simulation（纯 C#，不认识 Unity）
                       │                        │
              Unity 宿主（人玩）          dotnet 宿主（跑批，0.09 秒/局）
```

- 核心不含 Unity：`dotnet build Tools/runner/CoreCheck` 能过就说明成立（CI 守卫）
- 核心看不见表现层：`asmdef` 编译期强制
- 同种子可复现：跑两批逐局比对，必须完全一致

细节见 [`Docs/架构_总纲.md`](./Docs/架构_总纲.md)。

## Unity

用 Unity Hub / Editor **打开 `Game/`**（不要打开仓库根目录）。版本：Unity 6（6000.5.3f1）。

进 Play 前会自动检查 `Design/*.csv` 是否比 `game_rules.json` 新，是则自动重新生成；校验失败会**阻止进入 Play**。

## 改数值

不要手改 JSON。改 `Design/*.csv` → `python Tools/config/build.py` → JSON 自动生成。
流程见 [`Design/README.md`](./Design/README.md)。
