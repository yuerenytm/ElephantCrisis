-- ---------------------------------------------------------------
-- 象群危机：对局数据仓库
--
-- 注意：这里的库**不是游戏运行时的依赖**。跑批仍然只写本地 JSON 文件，
-- 本文件配套的 ingest.py 负责把那些文件导入进来，供 SQL 查询/复盘。
-- 因此：库可以随时删掉重建，跑批不受影响。
-- ---------------------------------------------------------------

CREATE DATABASE IF NOT EXISTS elephant_crisis
  DEFAULT CHARACTER SET utf8mb4
  DEFAULT COLLATE utf8mb4_0900_ai_ci;

USE elephant_crisis;

-- 一局对局（对应一个 match_<seed>/ 目录）
CREATE TABLE IF NOT EXISTS matches (
    id           BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    seed         INT             NOT NULL,
    winner       VARCHAR(16)     NULL,
    reason       VARCHAR(24)     NOT NULL,
    full_rounds  INT             NOT NULL,
    collect      VARCHAR(8)      NOT NULL DEFAULT 'both',
    source       VARCHAR(24)     NOT NULL DEFAULT 'unknown',
    event_count  INT             NOT NULL DEFAULT 0,
    strategies   JSON            NULL,
    per_role     JSON            NULL,
    path         VARCHAR(512)    NOT NULL,          -- 源目录，用于幂等
    file_hash    CHAR(64)        NOT NULL,          -- meta+events 的内容哈希
    imported_at  TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,

    PRIMARY KEY (id),
    UNIQUE KEY uk_matches_path (path),
    KEY idx_matches_seed (seed),
    KEY idx_matches_winner (winner),
    KEY idx_matches_source (source)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 逐事件流（对应 events.jsonl 的每一行）
-- 只有少量字段需要建列，其余塞进 payload，避免每加一种事件就改表。
CREATE TABLE IF NOT EXISTS match_events (
    id       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    match_id BIGINT UNSIGNED NOT NULL,
    t        INT             NOT NULL,              -- 事件序号
    type     VARCHAR(24)     NOT NULL,              -- turn_start / damage / move ...
    actor    VARCHAR(24)     NULL,
    target   VARCHAR(24)     NULL,
    round    INT             NULL,
    payload  JSON            NOT NULL,

    PRIMARY KEY (id),
    KEY idx_events_match_t (match_id, t),
    KEY idx_events_type (type),
    KEY idx_events_actor (actor),
    CONSTRAINT fk_events_match FOREIGN KEY (match_id) REFERENCES matches (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 每个角色在一局里的结局（从 meta.per_role 摊平，方便 SQL 统计）
CREATE TABLE IF NOT EXISTS match_roles (
    id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    match_id    BIGINT UNSIGNED NOT NULL,
    role        VARCHAR(16)     NOT NULL,
    survived    TINYINT(1)      NOT NULL,
    death_round INT             NULL,

    PRIMARY KEY (id),
    UNIQUE KEY uk_roles_match_role (match_id, role),
    KEY idx_roles_role (role),
    CONSTRAINT fk_roles_match FOREIGN KEY (match_id) REFERENCES matches (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 每次导入的批次记录（排查"这批数据什么时候进来的"）
CREATE TABLE IF NOT EXISTS import_batches (
    id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    out_dir     VARCHAR(512)    NOT NULL,
    matches_new INT             NOT NULL DEFAULT 0,
    matches_skip INT            NOT NULL DEFAULT 0,
    note        VARCHAR(255)    NULL,
    started_at  TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,

    PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
