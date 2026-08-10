# 客户端性能分析（实际对局）

在 **带表现的真实 Unity 对局** 里采帧率、卡顿、内存、美术/`Resources.Load`、技能与玩法行动附近的耗时，再离线标可疑卡顿秒并做共现归因。

| | 本工具 | logic_qa / balance |
|--|--|--|
| 跑什么 | **实际对局**（渲染、UI、资源加载都在） | **逻辑对局** LogicSim（可瘦开局） |
| 看什么 | 这一刻卡不卡、慢在加载还是演算 | 规则违规模观 / 胜率死亡率宏观 |
| 日志 | `persistentDataPath/ElephantPerf/` | `Tools/sim/output/match_*/` |

两套 JSONL **schema 不同，不可混喂**。

> 共现 ≠ 精确根因：报告是「卡顿窗口里抬升的事件」排名，需人工复核。

## 目录

```text
Tools/perf_analysis/
  config/client_perf.yaml
  schema/event_schema.md
  tools/analyze_client_perf.py
  tools/pull_latest.py
  scripts/run_perf_collect.py
  reports/
  samples/

Game/Assets/Scripts/Perf/
Game/Assets/StreamingAssets/Config/client_perf.yaml
```

## 采集

```bash
python Tools/perf_analysis/scripts/run_perf_collect.py
```

或打开 `Game/`，启动参数加 `-perfAuto`。

日志：`Application.persistentDataPath/ElephantPerf/session_*/events.jsonl`。

## 离线分析

```bash
cd Tools/perf_analysis
python tools/analyze_client_perf.py --input samples/session_demo
```
