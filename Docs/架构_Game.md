# 架构 · Game

> 适用：`Game/Assets/Scripts/`（62 个 .cs，约 1.72 万行）
> 配套：[`架构_总纲.md`](./架构_总纲.md)（验收标准）、[`架构_Tools.md`](./架构_Tools.md)（工具链）

---

## 一、五个顶层目录

| 目录 | 文件 | 行数 | 一句话 | 谁依赖它 |
|---|---|---|---|---|
| `Simulation/` | 33 | 8,714 | 规则核心，纯 C# | 所有人 |
| `View/` | 24 | 7,016 | 表现与交互（画面 / 输入 / UI / 相机） | `App` 与 Unity 宿主 |
| `App/` | 1 | 310 | 装配与生命周期 | 无（最上层） |
| `AI/` | 2 | 837 | 决策 + 演出节奏 | — |
| `Editor/` | 2 | 312 | 仅编辑器：配置构建、元数据导出 | — |

**依赖只能向下**，其中两条由 asmdef 在**编译期**强制：

```
ElephantCrisis.Simulation   references: []            ← 不许引用任何人
ElephantCrisis.View         references: Simulation    ← 不许引用 App
Assembly-CSharp             App / AI / Editor         ← 自动引用上面两个
```

在 `Simulation/` 里写一行 `MapVisual.Instance` 或 `GameBootstrap.Instance`，**编译直接失败**。

---

## 二、Simulation 的子模块

| 子目录 | 文件 | 行数 | 职责 |
|---|---|---|---|
| `Turn/` | 3 | 827 | 回合节拍（`TurnManager`）、胜负（`GameManager`）、虚拟时钟 |
| `Units/` | 5 | 1,814 | 单位（`UnitActor`，属性现算）、背包、角色定义 |
| `Actions/` | 1 | 1,566 | **唯一**允许改状态的地方：35 个 `Try*` |
| `Hazards/` | 2 | 921 | 火焰 / 炸弹 / 地雷 / 香蕉皮；地面牌堆 |
| `World/` | 5 | 867 | 网格、地形生成、能见度、天气 |
| `Cards/` | 2 | 735 | 牌库与卡牌元数据 |
| `Logging/` | 2 | 538 | 事件流（`events.jsonl`）与成绩单（`meta.json`） |
| `Skills/` | 3 | 461 | 四角色技能、领袖宣言 |
| `Config/` | 1 | 656 | 读 `game_rules.json`、键登记与校验 |
| `Host/` | 2 | 147 | `MatchSetup` / `MatchDriver`：一局的装配与推进 |
| `Status/` | 1 | 96 | 7 种状态 + `TickPolicy` |
| `Presentation/` | 1 | 102 | 表现层门面 + 空实现 |

根目录另有 5 个小文件：`Int2`（坐标）、`DeterministicRng`（随机）、`Rgba`（颜色）、`Log`（日志出口）、`MatchConfig`（模式）。

### 三条设计原则

1. **属性不存储，每次现算**
   `UnitActor.CurrentAtk` = 基础 + 永久 + 临时 − 状态减益 + 装备。不用维护"改装备要刷新攻击力"。

2. **状态变更只有一个入口**
   所有操作走 `ActionService.Try*`，返回 `bool` 表示"规则允许吗"。UI 不允许直接改血量。

3. **表现层只用句柄**
   视觉对象一律 `int` 句柄，逻辑层不认识 `GameObject`。无头模式下 `IPresentation` 是空实现，调用自动空转。

---

## 三、一局是怎么跑的

```
MatchSetup.AssembleLean()        // 建系统 → 生成地图 → 造单位 → 造牌库 → 开回合
   └─ TurnManager.Setup(units)
        └─ BeginTurnFor(unit)    // 行动开始结算链（状态 → 着火 → 熔岩/冰地 → 晕眩…）
             ├─ 人类：View/Input → ActionService.Try*
             └─ AI  ：SimpleHeuristicAi.TryActOnce → ActionService.Try*
                  └─ RequestEndTurn → 下一个单位 → GameManager 判胜负
```

两个宿主（Unity / dotnet）**共用 `MatchSetup` 与 `MatchDriver`**，区别只在：

| | Unity 宿主 | dotnet 宿主 |
|---|---|---|
| 入口 | `GameBootstrap`（`RuntimeInitializeOnLoadMethod`） | `Tools/runner/SimRunner/Program.cs` |
| 表现层 | `UnityPresentation`（真实绘制） | `NullPresentation`（空转） |
| 推进 | `AiController` 协程（每手有延时） | `MatchDriver` 同步跑完 |

---

## 四、配置怎么进来

```
Design/rules.csv ─┐
Design/roles.csv  ├─ build.py（校验 6 类）─▶ game_rules.json（1,876 行）
Design/cards.csv  │                          │
Design/tiles.csv  │                          ▼
Design/statuses.csv┘                 GameRulesConfig 静态属性
```

当前规模：**46 张卡 / 7 种地形 / 7 种状态 / 4 个角色**。

- 校验拦"错误"（必填键缺失、未知键、数值越界、重复、引用不存在、牌库总数不符），不拦设计选择
- 键登记：`GetInt("terrain","sand_move")` 会记下键名，启动时与配置比对，拼错会告警
- 宿主注入：配置目录与 JSON 解析器都由宿主给（Unity 用 `JsonUtility`，dotnet 用 `System.Text.Json`）

---

## 五、怎么读这个模块

1. `Simulation/Host/MatchSetup.cs` + `MatchDriver.cs`（147 行，看清全局）
2. `Simulation/Turn/TurnManager.cs` 的 `BeginTurnFor`（一回合发生了什么）
3. `Simulation/Units/UnitActor.cs` 的属性计算（`CurrentAtk` 那一片）
4. `Simulation/Actions/ActionService.cs` 挑三个 `Try*` 看（移动 / 近战 / 用道具）

**前两步读完，就掌握了 80% 的运行原理。**
