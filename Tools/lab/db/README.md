# 对局数据仓库（MySQL）

把跑批产出的 `meta.json` / `events.jsonl` 导入 MySQL，方便用 SQL 做统计与复盘。

**它不是游戏运行时的依赖。** 跑批只写本地文件，数据库是"查询加速器"：

- 库删了可以随时从文件重建
- 库挂了不影响跑批
- 数据真源永远是 `Tools/sim/output/` 下的文件

## 目录内容

| 文件 | 作用 |
|---|---|
| `schema.sql` | 建库建表（`matches` / `match_events` / `match_roles` / `import_batches`） |
| `ingest.py` | 文件 → 库的导入器（幂等，可重复跑） |
| `queries.sql` | 常用查询：胜率、伤害来源、跨宿主一致性检查 |
| `docker-compose.yml` | 本地开发用 MySQL 8（首次启动自动建表） |

## 三步用起来

```bash
# 1. 起库（首次会自动执行 schema.sql）
docker compose -f Tools/db/docker-compose.yml up -d

# 2. 装客户端库
pip install pymysql

# 3. 导入（可反复跑，已导入且内容未变会自动跳过）
python Tools/db/ingest.py --dsn "mysql://root:ec_root_pw@127.0.0.1:3306/elephant_crisis"
```

想先看数据结构、不连库：

```bash
python Tools/db/ingest.py --dry-run
# 对局 4798 局 / 事件 3311 条 / 角色记录 19192 条
```

## 表结构

```
matches ──┬── match_events   (逐事件，payload 存原始 JSON)
          └── match_roles    (每角色结局，从 meta.per_role 摊平)
import_batches               (每次导入的批次记录)
```

事件表只把 `t / type / actor / target / round` 建成列，其余塞进 `payload`。
好处：**新增一种事件类型不用改表**。

```sql
-- 举例：谁在真正造成伤害
SELECT JSON_UNQUOTE(payload->'$.via') AS via, SUM(payload->>'$.dealt') AS total
FROM match_events WHERE type = 'damage' GROUP BY via ORDER BY total DESC;
```

## 幂等怎么做的

每条 `matches` 记录保存：
- `path`：源目录（唯一键）
- `file_hash`：`meta.json` + `events.jsonl` 内容的 sha256

重跑时若路径已存在且哈希相同 → 跳过；哈希变了（重跑过该种子）→ 删旧行重插。
加 `--force` 可强制重写。

## 为什么不直连

游戏客户端里放数据库账号密码，等于把库交出去；断网也不能玩。
将来做联机时，正确路径是：

```
Unity 客户端 ──HTTP──▶ 你的服务器 ──▶ MySQL
```

而这个"服务器"可以直接引用 `ElephantCrisis.Core`（规则只有一份）。
本目录的导入器可以先用来验证"服务器能读核心、能进库"这条链。
