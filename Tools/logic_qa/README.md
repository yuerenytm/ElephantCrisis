# 逻辑 QA（微观：规则对不对）

对 **逻辑对局** 日志做微观检测：基线硬断言 + Isolation Forest 可疑状态 + Streamlit 工作台。

造数与平衡共用 [`../sim/`](../sim/README.md) 的 Game LogicSim 输出；本工具只负责「事件合不合法 / 状态像不像 bug」，不做胜率统计。

实际对局的帧率 / 美术加载见 [`../perf_analysis/`](../perf_analysis/README.md)（另一套采集，勿混用 JSONL）。

## 目录

```text
logic_qa/
  anomaly_analysis/   # 基线 + IF
  app/                # Streamlit
  scripts/
  config/runtime/
  models/ reports/
```

## 安装

```bash
cd Tools/logic_qa
pip install -r requirements.txt
```

## 用法

```bash
# 造数（与 balance 同一套）
cd ../sim
python scripts/run_batch.py --matches 30 --seed 1 --out output

# 微观基线
cd ../logic_qa
python scripts/run_baseline.py --input ../sim/output --out reports

# 一键造数 + 基线 +（可选）异常
python scripts/run_qa_pipeline.py --matches 30 --train

# 工作台
python scripts/run_workbench.py
```

或资源管理器中双击 [`打开工作台.bat`](打开工作台.bat)（会开浏览器，一般是 http://localhost:8501）。

测试：`pytest -q`

## 怎么读结果

- **基线失败**（`baseline_bug`）：优先当真违规查  
- **`ai_suspect`**：软告警，抽查用  
- **`clean`**：两边都没报警  

缺陷故事请用真实跑批自行沉淀。
