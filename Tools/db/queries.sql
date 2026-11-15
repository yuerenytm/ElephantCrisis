-- ---------------------------------------------------------------
-- 常用查询示例（导完数据后直接跑）
--
-- 用 `mysql -u root -p elephant_crisis < Tools/db/queries.sql` 执行全部，
-- 或者挑一段在客户端里跑。
-- ---------------------------------------------------------------

USE elephant_crisis;

-- 1) 全局盘子有多大
SELECT
    (SELECT COUNT(*) FROM matches)      AS matches,
    (SELECT COUNT(*) FROM match_events) AS events,
    (SELECT COUNT(*) FROM match_roles)  AS role_rows;


-- 2) 各角色胜率（只统计正常出胜负的对局）
SELECT
    r.role,
    COUNT(*)                                              AS games,
    SUM(r.survived)                                       AS survivals,
    ROUND(SUM(r.survived) / COUNT(*) * 100, 1)            AS survival_rate_pct,
    ROUND(AVG(r.death_round), 1)                          AS avg_death_round
FROM match_roles r
JOIN matches m ON m.id = r.match_id
WHERE m.winner IS NOT NULL
GROUP BY r.role
ORDER BY survival_rate_pct DESC;


-- 3) 每个数据来源的对局量与平均回合（看不同宿主的差异）
SELECT
    source,
    COUNT(*)                        AS games,
    ROUND(AVG(full_rounds), 2)      AS avg_rounds,
    MIN(full_rounds)                AS min_rounds,
    MAX(full_rounds)                AS max_rounds
FROM matches
GROUP BY source;


-- 4) 结束原因分布（last_standing / max_rounds / timeout）
SELECT reason, COUNT(*) AS games, ROUND(COUNT(*) / (SELECT COUNT(*) FROM matches) * 100, 1) AS pct
FROM matches
GROUP BY reason
ORDER BY games DESC;


-- 5) 伤害来源分布（看"谁在真正造成伤害"）
SELECT
    JSON_UNQUOTE(JSON_EXTRACT(payload, '$.via'))  AS via,
    COUNT(*)                                      AS hits,
    SUM(CAST(JSON_EXTRACT(payload, '$.dealt') AS SIGNED)) AS total_dealt,
    ROUND(AVG(CAST(JSON_EXTRACT(payload, '$.dealt') AS SIGNED)), 2) AS avg_dealt
FROM match_events
WHERE type = 'damage'
GROUP BY via
ORDER BY total_dealt DESC;


-- 6) 某个种子的完整事件流（复盘用）
--    把 :seed 换成你想看的种子
SELECT t, type, actor, target, payload
FROM match_events e
JOIN matches m ON m.id = e.match_id
WHERE m.seed = 50021
ORDER BY t
LIMIT 200;


-- 7) 事件最多的几局（排查异常长局）
SELECT m.seed, m.winner, m.reason, m.full_rounds, COUNT(e.id) AS events
FROM matches m
JOIN match_events e ON e.match_id = m.id
GROUP BY m.id
ORDER BY events DESC
LIMIT 10;


-- 8) 同一种子在不同来源下结果是否一致（跨宿主一致性检查）
SELECT
    m.seed,
    GROUP_CONCAT(CONCAT(m.source, ':', COALESCE(m.winner, 'null'), ':', m.full_rounds)
                 ORDER BY m.source SEPARATOR ' | ') AS outcomes
FROM matches m
GROUP BY m.seed
HAVING COUNT(DISTINCT CONCAT(COALESCE(m.winner, 'null'), ':', m.full_rounds)) > 1
LIMIT 20;
