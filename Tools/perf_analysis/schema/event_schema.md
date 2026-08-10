# 客户端性能事件 schema（JSONL，一行一个 JSON）

## 通用字段
- `t_ms`: 自 session 开始的毫秒时间戳（浮点）
- `type`: 事件类型

## session_start
- `platform`, `unity_version`, `product_name`, `session_id`

## session_end
- `reason`: `timeout` | `max_rounds` | `match_end` | `manual` | `quit` | `destroy`
- `duration_ms`, `hitch_count`, `load_count`, `action_count`

## hitch / frame
- `dt_ms`, `frame`
- `recent_loads`: 可选，最近加载路径列表
- `recent_actions`: 可选，最近玩法行动 kind 列表（hitch）

## resource_load
- `path`, `asset_type`, `dt_ms`, `ok`

## game_action
- `kind`: `combat` | `aoe_bomb` | `aoe_flame` | `util_flash` | `place_hazard` | `skill` | `item` | `move` | `turn` | `world_hazard` | `log_other`
- `detail`: 可选，简短说明（如 melee / Roles 路径旁注 / 战报截断）

## second_sample
每约 1 秒一条，供 Isolation Forest 秒级筛查：
- `frames`, `fps`, `avg_dt_ms`, `max_dt_ms`
- `hitch_count`, `load_count`, `action_count`
- `mono_mb`, `mono_delta_mb`

## memory
- `mono_mb`, `total_alloc_mb`（若 API 可用）
