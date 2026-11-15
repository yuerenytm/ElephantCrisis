# Design —— 策划工作区

**权威数据在这里。** 六张表由 `build_tables.py` 生成（原 yaml + 代码元数据 → CSV），
再由 `build.py` 反向合成游戏实际读取的配置：

```
game_rules.yaml（数值原文） + meta/meta.json（代码导出的元数据）
        │ build_tables.py
        ▼
   六张 CSV  ← 改这里
        │ build.py（校验 + 反向合成 + 与原 yaml 比对）
        ▼
Game/Assets/StreamingAssets/Config/game_rules.gen.yaml  ← 游戏优先读它
```

## 回退方式（保守迁移）

原 `game_rules.yaml` **保留不动**。删掉 `game_rules.gen.yaml`，
游戏下次启动就读回原文件——不改代码、不改 git。

## 已通过的验收

| 层级 | 怎么验 | 结果 |
|---|---|---|
| 数据 | CSV 反向合成 250 个键，逐键比对 + 整文档解析比对 | 0 差异 |
| 行为 | 同种子 50000 起 100 局，对拍切换前基线 | 0 差异 |

## 六张表

| 文件 | 内容 | 谁能改 |
|---|---|---|
| `roles.csv` | 四角色五维、法抗、出生点 | 策划 |
| `cards.csv` | 卡牌身份：名字、描述、槽位、使用方式、印数 | 策划 |
| `card_stats.csv` | 卡牌数值（长表：卡牌 / 参数 / 值） | 策划 |
| `tiles.csv` | 地形修正（空单元格 = 原 yaml 里没有该键） | 策划 |
| `statuses.csv` | 状态数值；列名即 yaml 键名，保证可逆 | 策划 |
| `rules.csv` | 其余全局旋钮 + 未能归属到条目的键 | 程序 |

表里**只放能改的数据**。行为（例如"状态何时递减"）不放表——
那是欠账 #2，等它变成字段后才有资格进表。

## 已知缺口（表里没有的东西）

| 缺口 | 在哪 | 说明 |
|---|---|---|
| 高地抬升 0.25 | `GridManager.HighlandElevation` | 硬编码常量，不在 yaml |
| 地形斑块生成参数 | `rules.csv` 的 `terrain` 分组 | 全局参数，不属于某个地形 |
| `molotov_*`（4 个键） | `rules.csv` 的 `items` 分组 | 键名按"行为"起，卡牌 id 是 `gasoline_bottle` |
| `thorns_reflect_pct` | `rules.csv` 的 `items` 分组 | 应为 `thorns_armor_*` 前缀 |
| `adrenaline_*` / `dying_*` | `rules.csv` 的 `status` 分组 | **不是** `StatusType`：前者是临时加成，后者是 `IsDying` 字段 |
| 结算顺序、动作次数上限 | 代码 | 规则骨架，永远不上表 |

## 校验（构建期拦截，不留给运行时）

`build.py` 生成前会拒绝这些情况：

| 类别 | 例子 |
|---|---|
| 列结构 | `roles.csv` 少一列「血量」 |
| 值域 | 血量 0（允许 1～500）、炸弹伤害 999（允许 0～200）、沙地移速 99（允许 −5～5） |
| 枚举 | 使用方式写成 `Throw`（只认 8 个合法值）、装备槽写成 `Head` |
| 坐标 | 出生点超出 18×18 |
| 引用 | `card_stats.csv` 指向不存在的卡牌 |
| 唯一键 | 同一张卡出现两行 |

**`--strict` 是迁移校验模式**：要求表与 `game_rules.yaml` 完全一致（0 差异），
用来证明"CSV 能无损还原原配置"。日常改动不要加这个参数——
改了数值本来就会和旧快照不一致，那不是错误，而是**改动摘要**（`build.py` 会逐条列出来）。

## 重新生成

```bash
# 1. 元数据（改过代码里的名字/描述后要重跑，需要 Unity）
Unity.exe -batchmode -nographics -projectPath Game   -executeMethod ConfigExport.Run -configOut Design/meta/meta.json -logFile export.log

# 2. 表：yaml/元数据 → CSV
python Tools/config/build_tables.py
python Tools/config/build_tables.py --check   # 表与源不一致则非零退出

# 3. 配置：CSV → 游戏读的 yaml
python Tools/config/build.py
python Tools/config/build.py --check          # 与原 yaml 有差异则非零退出
```
