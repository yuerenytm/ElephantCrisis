# 架构 · Game

> 适用：`Game/Assets/Scripts/`（48 个 .cs，约 1.6 万行）
> 引擎：Unity 6（6000.5.3f1），运行时无场景切换，所有系统由代码创建。

---

## 1. 一句话

**一个单进程、单场景、代码自举的回合制桌游引擎**：`GameBootstrap` 在运行时造出全部系统，`TurnManager` 驱动回合，`ActionService` 收口所有动作，`UnitActor` 承担全部结算，表现层（UI / 地图 / 氛围）全部可裁剪。

---

## 2. 分层

```
┌──────────────────────────────────────────────────────────┐
│ 表现层   GameUI / MainMenuUI / MapVisual / AtmosphereVisual │
│          BoardCameraController / PlayerInputController     │
│          SpriteFactory / RoleModelFactory / UiTheme         │
└───────────────▲──────────────────────────┬────────────────┘
                │ 读状态                     │ 发动作
┌───────────────┴──────────────────────────▼────────────────┐
│ 编排层   GameBootstrap（装配）  TurnManager（节拍/结算顺序）  │
│          GameManager（胜负/缩圈）  AiController（AI 触发）    │
└───────────────▲──────────────────────────┬────────────────┘
                │                          │
┌───────────────┴──────────────────────────▼────────────────┐
│ 服务层   ActionService（动作唯一入口）  SkillService         │
│          VisibilityService  StealthService                 │
│          WeatherService  HazardManager  GroundItemManager   │
│          DeckManager  LeaderDeclarationService             │
└───────────────▲──────────────────────────┬────────────────┘
                │                          │
┌───────────────┴──────────────────────────▼────────────────┐
│ 领域层   UnitActor（状态+结算） Inventory  StatusEffect      │
│          ItemInfo/ItemKind  GameRulesConfig  GameClock      │
│          GridManager  TerrainGenerator  MatchConfig         │
└───────────────────────────────────────────────────────────┘
```

**依赖方向只有一条**：表现 → 编排 → 服务 → 领域。领域层不引用上层。

---

## 3. 模块清单

| 目录 | 文件数 | 职责 | 代表文件 |
|---|---|---|---|
| `Core/` | 24 | 启动、回合、动作、配置、时钟、能见度、UI 外壳 | `GameBootstrap` `TurnManager` `ActionService` `GameRulesConfig` |
| `Units/` | 5 | 角色状态与结算、背包、AI 决策 | `UnitActor` `Inventory` `SimpleHeuristicAi` |
| `World/` | 8 | 地图、地形、天气表现、危险物、地面掉落 | `TerrainGenerator` `HazardManager` `GroundItemManager` `MapVisual` |
| `Cards/` | 1 | 共用牌库（构建 / 散落 / 弃牌） | `DeckManager` |
| `Skills/` | 3 | 主动技能、隐匿、领袖宣言 | `SkillService` `LeaderDeclarationService` |
| `AI/` | 2 | AI 触发与启发式策略 | `AiController` `SimpleHeuristicAi` |
| `LogicSim/` | 4 | 无头跑批 + 事件日志 | `LogicSimRunner` `LogicMatchLogger` |
| `UI/` | 4 | HUD、主菜单、设置、主题 | `GameUI` `UiTheme` |
| 根目录 | 1 | 网格数据与坐标转换 | `GridManager.cs` |

---

## 4. 运行时形态

```
Unity 进程（单场景，无 LoadScene）
  ├─ GameBootstrap（DontDestroyOnLoad）
  │    ├─ 主菜单模式：只建 UI
  │    └─ 对局模式：EnsureSystems 造出所有系统
  ├─ 四种模式（MatchConfig）
  │    ├─ Hotseat   四人手动
  │    ├─ AiBattle  一人手动 + 三 AI
  │    ├─ AdminMode 可印牌 / 切天气 / 切时段
  │    └─ LogicSim  四人全 AI + 无 UI + 写日志
  └─ 重开 / 回菜单 = 销毁重建，不切场景
```

**四个模式共用同一套规则代码**，差异全部收敛在 `MatchConfig` 与少量 `if (!IsLogicSim)` 判断里。

---

## 5. 关键数据流

### 5.1 开局

```
GameBootstrap.StartMatch
  ├─ ValidateKeysOnce            校验 yaml 键名
  ├─ EnsureSystems(lean)         造单例（lean 跳过 UI/地图/氛围）
  ├─ GridManager.InitTiles       → TerrainGenerator.Generate
  ├─ SpawnUnits                  → UnitActor.Setup
  ├─ GameManager.RegisterUnits
  ├─ DeckManager.BuildDemoDeck   → ScatterDrawPileOntoMap
  ├─ LogicMatchLogger.EmitSnapshot   必须在 Setup 之前
  └─ TurnManager.Setup           → BeginTurnFor(第一位)
```

### 5.2 一个行动

```
BeginTurnFor（行动开始结算链：护身符→着火→领袖→状态→沼泽毒→熔岩→火焰→冰→晕眩）
   ↓ OnTurnChanged
   ├─ 玩家：PlayerInputController → ActionService.Try*
   └─ AI：AiController → SimpleHeuristicAi.TryActOnce（一次一个动作，循环调用）
   ↓
ActionService → UnitActor.TakeDamage / ApplyStatus / ...
   ↓
TurnManager.RequestEndTurn（超重拦截）→ EndTurn（行动结束结算）
   ↓ 推进下一位；回到 0 号位则跨轮结算（状态/冷却/熔岩/天气）
   ↓
GameManager.CheckWinConditions
```

### 5.3 无头跑批

```
LogicSimRunner.CoRunOne
  ├─ Random.InitState(seed)     全局随机流起点
  ├─ MatchConfig.SetLogicSim
  ├─ GameBootstrap.StartLogicSimMatch
  └─ 帧循环等待结束 / 回合上限 / 180 秒超时
       └─ AiController.PumpLogicSim（同步泵，避免递归爆栈）
            └─ PlayTurnSync → SimpleHeuristicAi.TryActOnce
```

---

## 6. 配置与真源

| 事实 | 真源 | 读取方 |
|---|---|---|
| 数值（HP/攻防/印数/天气/地形比例） | `StreamingAssets/Config/game_rules.yaml` | `GameRulesConfig` |
| 规则语义 | `Docs/规则_*.md` | 人 |
| 实际行为 | `Assets/Scripts/*.cs` | 引擎 |
| 平衡数据 | `Tools/sim/output/match_*` | `Tools/balance_analysis` |

`GameRulesConfig.ValidateKeysOnce()` 会比对"yaml 里的键"与"代码读过的键"，**拼错或废弃的键会告警**——这是防止"改 yaml 不生效"的唯一机制。

---

## 7. 扩展点

| 要加什么 | 标准动作 |
|---|---|
| **新卡牌/物品** | `ItemKind` 加枚举 → `ItemInfo` 补元数据 → `ActionService.TryUse*` 加分支 → yaml 加印数 |
| **新角色** | `RoleType` → yaml `roles` 与 `spawn_cells` → `SkillService`/`SkillInfo` 加技能 → `SimpleHeuristicAi` 补评估 |
| **新地形** | `TileType` → `TerrainGenerator.Biomes` → `TerrainInfo` 修正 → `MapVisual` 贴图 |
| **新状态** | `StatusType` → `UnitActor.ApplyStatus/Tick*` → `StatusInfo` 显示 |
| **新逻辑校验** | `Tools/logic_qa/anomaly_analysis/baseline/checks.py` 加 `_check_*` 并注册 |
| **新 UI 面板** | 只读 `GameManager/TurnManager` 状态，动作一律走 `ActionService` |

---

## 8. 已知债务

| 债务 | 位置 | 影响 |
|---|---|---|
| **上帝类** | `UnitActor`（1634 行） | 状态、结算、表现混在一起，难测 |
| **巨型服务** | `ActionService`（1761 行，30+ 个 `TryUse*`） | 高度重复，适合按物品类型拆分 |
| **模式判断散落** | `if (!MatchConfig.IsLogicSim)` 多处 | 逻辑与表现耦合，无头模式靠"打补丁"实现 |
| **全局随机流** | `UnityEngine.Random` | 改任意一处随机调用，整局结果偏移 |
| **单例遍地** | 多数 Manager 是 `Instance` | 隐式依赖，测试难注入 |
| **目录不一致** | `GridManager.cs` 在 Scripts 根 | 与 `World/` 同类职责却不同位置 |
| **战斗规则无独立层** | 伤害公式在 `UnitActor`，命中在 `ActionService` | 规则分散，文档与代码易漂移 |

---

## 9. 目录速查

```
Scripts/
├─ Core/        启动 · 回合 · 动作 · 配置 · 时钟 · 能见度 · UI 外壳
├─ Units/       角色 · 背包 · AI 决策
├─ World/       网格表现 · 地形 · 天气表现 · 危险物 · 掉落
├─ Cards/       牌库
├─ Skills/      技能 · 隐匿 · 领袖宣言
├─ AI/          AI 触发与策略
├─ LogicSim/    跑批与日志
├─ UI/          HUD · 菜单 · 主题
└─ GridManager  网格数据（根目录）
```
