# Design —— 策划工作区

这里的表由 `Tools/config/build_tables.py` 生成：

```
game_rules.yaml（数值，当前权威） +  meta/meta.json（代码导出的元数据）  ->  *.csv
```

## 现状（重要）

**权威仍是 `game_rules.yaml`。改这里的 CSV 目前不会影响游戏。**
这是迁移期的"影子副本"，按 [`架构_总纲.md`](../Docs/架构_总纲.md) §3 第 1 条，
并存必须满足三条：脚本生成、有漂移检查、写明退场条件。

- 脚本生成 ✅（`build_tables.py`）
- 漂移检查 ✅（`build_tables.py --check`）
- 退场条件：`build.py` 出 JSON → 代码改读 JSON → 对拍 0 差异 → 删掉 yaml 对应四段

## 五张表

| 文件 | 内容 | 谁能改 |
|---|---|---|
| `roles.csv` | 四角色五维、法抗、出生点 | 策划 |
| `cards.csv` | 卡牌身份：名字、描述、槽位、使用方式、印数 | 策划 |
| `card_stats.csv` | 卡牌数值（长表：卡牌 / 参数 / 值） | 策划 |
| `tiles.csv` | 地形数值与生成 | 策划 |
| `statuses.csv` | 状态数值与上限 | 策划 |
| `rules.csv` | 其余全局旋钮 | 程序 |

表里**只放能改的数据**。行为（例如"状态何时递减"）不放表——
那是欠账 #2，等它变成字段后才有资格进表。

## 已知缺口（表里没有的东西）

| 缺口 | 在哪 | 说明 |
|---|---|---|
| 高地抬升 0.25 | `GridManager.HighlandElevation` | 硬编码常量，不在 yaml |
| 地形斑块生成参数 | `rules.csv` 的 `terrain` 分组 | 是全局参数，不属于某个地形 |
| `molotov_*`（4 个键） | `rules.csv` 的 `items` 分组 | 键名按"行为"起，卡牌 id 是 `gasoline_bottle` |
| `thorns_reflect_pct` | `rules.csv` 的 `items` 分组 | 应为 `thorns_armor_*` 前缀 |
| `adrenaline_*` / `dying_*` | `rules.csv` 的 `status` 分组 | **不是** `StatusType`：前者是临时加成，后者是 `IsDying` 字段（"影子状态"） |
| 结算顺序、动作次数上限 | 代码 | 规则骨架，永远不上表 |

## 重新生成

```bash
# 1. 元数据（改过代码里的名字/描述后要重跑，需要 Unity）
Unity.exe -batchmode -nographics -projectPath Game \
  -executeMethod ConfigExport.Run -configOut Design/meta/meta.json -logFile export.log

# 2. 合成表
python Tools/config/build_tables.py
python Tools/config/build_tables.py --check   # 有漂移则非零退出
```
