# Design —— 策划工作区

**这里就是数值真源。** 改表 → 跑一条命令 → 游戏生效。

```
Design/*.csv  ──校验──>  Game/Assets/StreamingAssets/Config/game_rules.json  ──>  游戏
```

没有中间 yaml、没有手写解析器（游戏侧用 `JsonUtility` 读）。原 `game_rules.yaml`
已删除；要回到旧形态只能 `git revert`。

## 六张表

| 文件 | 内容 | 谁能改 |
|---|---|---|
| `roles.csv` | 四角色五维、法抗、出生点 | 策划 |
| `cards.csv` | 卡牌身份：名字、描述、槽位、使用方式、印数 | 策划 |
| `card_stats.csv` | 卡牌数值（长表：卡牌 / 参数 / 值） | 策划 |
| `tiles.csv` | 地形修正（空单元格 = 没有这个键） | 策划 |
| `statuses.csv` | 状态数值；列名即键名，保证可逆 | 策划 |
| `rules.csv` | 其余全局旋钮（天气 / 时钟 / 熔岩 / 技能等） | 程序 |

表里**只放能改的数据**。行为（例如"状态何时递减"）在代码里，不放表。

## 改数值

```bash
# 1) 改表（Excel 直接打开，utf-8-sig 不会乱码）
# 2) 校验 + 生成
python Tools/config/build.py
# 只校验不落盘：
python Tools/config/build.py --check
```

校验不过会**拒绝生成**并逐条报错，退出码 1。

### 校验拦什么

| 类别 | 例子 |
|---|---|
| 列结构 | `roles.csv` 少一列「血量」 |
| 值域 | 血量 0（允许 1～500）、炸弹伤害 999（允许 0～200）、沙地移速 99（允许 −5～5） |
| 枚举 | 使用方式写成 `Throw`（只认 8 个合法值）、装备槽写成 `Head` |
| 坐标 | 出生点超出 18×18 |
| 引用 | `card_stats.csv` 指向不存在的卡牌 |
| 唯一键 | 同一张卡 / 同一参数出现两行 |

## 加一张新卡

| 步 | 谁做 | 做什么 |
|---|---|---|
| 1 | 程序 | `ItemKind` 加枚举值；需要新玩法才加 `Try*` |
| 2 | 程序 | 跑 `ConfigExport` 重新导出契约快照（**必须**，否则 meta 过期） |
| 3 | — | `python Tools/config/build_tables.py` → 表里自动多一行（印数 0、名字取代码兜底） |
| 4 | 策划 | 填名字、描述、印数、数值 |
| 5 | — | `python Tools/config/build.py` → 生效 |

`build_tables.py` **只补缺失的行，从不改已有行**——重跑不会撤销任何手工改动。

## meta.json 是什么

不是数据源，是"**代码契约快照**"：代码里有哪些卡 / 地形 / 状态 / 角色，
以及各类的装备槽与使用方式（用于生成新行的初始值）。

- 被 `.gitignore` 忽略，随时可重生成
- `build_tables.py` 发现它比 C# 源码旧会告警："改了代码忘了重新导出"

## 名字与描述

也在表里改。它们进配置的 `display` 段，游戏侧
`GameRulesConfig.GetString("display", "card_<id>_name", 代码兜底)` 读取。
改完跑 `build.py` 即生效。

## 已知缺口

| 缺口 | 在哪 | 说明 |
|---|---|---|
| 高地抬升 0.25 | `GridManager.HighlandElevation` | 硬编码常量 |
| `molotov_*`（4 个键） | `rules.csv` 的 `items` 分组 | 键名按"行为"起，卡牌 id 是 `gasoline_bottle` |
| `adrenaline_*` / `dying_*` | `rules.csv` 的 `status` 分组 | **不是** `StatusType`：临时加成 / `IsDying` 字段 |
| 结算顺序、动作次数上限 | 代码 | 规则骨架，永远不上表 |
