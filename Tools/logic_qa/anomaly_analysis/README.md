# 异常分析（微观检测实现）

`logic_qa/anomaly_analysis`：只做**规则对不对 / 状态像不像正常**（微观）。

- 造数：[`../../sim/`](../../sim/README.md) Game LogicSim  
- 宏观胜率等：[`../../balance_analysis`](../../balance_analysis/README.md)  
- 实机帧率：[`../../perf_analysis/`](../../perf_analysis/README.md)

```text
anomaly_analysis/
  baseline/       # 硬性规则基线
  detection/      # Isolation Forest
  config/
  tests/
```

```bash
cd Tools/logic_qa
python scripts/run_baseline.py --input ../sim/output --out reports
python scripts/train_anomaly.py --input ../sim/output --out models --baseline-report reports/baseline_report.json
python scripts/run_anomaly.py --input ../sim/output --model models --out reports
```
