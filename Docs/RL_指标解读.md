# RL 训练指标解读（自学手册）

> 面向"自己跑训练、自己盯曲线"的人。用本项目真实产生的数据讲解：怎么看日志、看哪些指标、怎么判断训练健不健康。
>
> 配套界面：TensorBoard（曲线）、Streamlit 面板（进度/胜率/日志尾）、原始日志文件（深挖）。

## 目录

1. [日志与文件地图](#1-日志与文件地图)
2. [三种查看方式](#2-三种查看方式)
3. [Console 那行日志怎么读](#3-console-那行日志怎么读)
4. [TensorBoard 十个指标逐讲](#4-tensorboard-十个指标逐讲)
5. [训练健康体检清单](#5-训练健康体检清单)
6. [几个最容易混淆的数字](#6-几个最容易混淆的数字)
7. [几千万步训练：实际要盯什么](#7-几千万步训练实际要盯什么)

---

## 1. 日志与文件地图

所有训练相关文件都在 `Tools/rl/` 下：

```
Tools/rl/
├── trainer_<run-id>.log        ← 面板点「①启动Trainer」时 trainer 的输出
│                                    （"Listening on port 5004"、连接信息、报错）
├── verify_resume.log           ← 手动命令行验证续训时留的 trainer 日志（临时）
├── train_console.log           ← 冒烟测试时 PowerShell 里 mlagents 的完整输出
├── unity_rl_train.log  (可到几十MB)  ← Unity 引擎自己的日志
│                                    （回合日志、[RlStats] 胜率统计、卡住时的线索）
├── results/<run-id>/           ← 训练产物
│   └── ElephantCrisis/
│       ├── checkpoint.pt       ← 最新模型存档
│       ├── ElephantCrisis-<步数>.pt   ← 按步数命名的历史存档
│       ├── ElephantCrisis-<步数>.onnx ← 可部署模型（Unity/LogicSim 用）
│       └── events.out.tfevents.*     ← ⭐ TensorBoard 数据（10 个指标在这里）
└── reports/
    ├── training_progress.json  ← Unity 侧进度（决策步数/局数）
    ├── training_stats.json     ← Unity 侧统计（胜率/死亡率/领袖宣言）
    └── training_matches.jsonl  ← 逐局明细（每行一局）
```

| 文件 | 谁在写 | 记录什么 |
|---|---|---|
| `trainer_<run-id>.log` | Python trainer（mlagents） | Step 行、连接、报错、超参打印 |
| `unity_rl_train.log` | Unity | 回合过程、`[RlStats]` 统计、异常线索 |
| `results/.../events.*` | trainer | 全部训练曲线（TensorBoard 数据源） |
| `reports/training_*.json` | Unity | 进度与胜率统计 |
| `training_matches.jsonl` | Unity | 逐局明细 |

---

## 2. 三种查看方式

### 方式一：TensorBoard（可视化曲线）—— 训练几千万步就靠它

启动：

```powershell
cd Tools/rl
.\.venv\Scripts\python -m tensorboard.main --logdir results --port 6006
```

浏览器打开 `http://localhost:6006`。左侧选 run（`ec_smoke_v1`、`ec_bootstrap_v1`…），展开 `Scalars` 看全部曲线。

- **能看**：Cumulative Reward / Policy Loss / Value Loss / Entropy / Episode Length 等 10 条曲线
- **不能看**：角色胜率（那是 Unity 侧统计，见方式二/三）

### 方式二：Streamlit 面板（按钮式）

`http://localhost:8501`，RL 训练台：

| 面板位置 | 能看到什么 |
|---|---|
| ② 训练启动 → 步数进度横幅 | Trainer 已学步数 / 进度条 / Unity 决策步 / 累计局数 |
| ② 训练启动 → 一键运行区底部 | `trainer_<run-id>.log` 最新几行（实时尾巴） |
| ⑤ 训练埋点 | 胜率 / 死亡率柱状图、领袖宣言、逐局明细、原始 JSON |

面板**没有**那 10 条训练曲线——曲线要看 TensorBoard。

### 方式三：直接看原始文件（深挖用）

- 大文件（`unity_rl_train.log` 可能几十 MB）**别用编辑器整个打开**，会吃光内存。用搜索：
  - VS Code：`Ctrl+Shift+F` 搜 `RlStats` / `Step:` / `ERROR` / `Exception`
  - PowerShell：`Select-String "RlStats" unity_rl_train.log | Select-Object -Last 5`
- trainer 连接状态：`Get-Content trainer_ec_bootstrap_v1.log -Tail 30`

---

## 3. Console 那行日志怎么读

训练时 trainer 每 `summary_freq` 步打印一行（下面是冒烟测试的真实输出）：

```
[INFO] ElephantCrisis. Step: 7000. Time Elapsed: 381.862 s. Mean Reward: -1.153. Std of Reward: 0.044. Training.
```

| 字段 | 含义 |
|---|---|
| `Step: 7000` | 累计训练步数（Unity 决策次数，不是更新次数） |
| `Time Elapsed: 381.8s` | 累计用时。**7000 步 / 381 秒 ≈ 18 步/秒** = SPS（每秒步数） |
| `Mean Reward: -1.153` | 最近一批对局的平均奖励 |
| `Std of Reward: 0.044` | 平均奖励的波动幅度 |
| `Training` | 状态：Training（学习中）/ Inference（推理中） |

**SPS 怎么算剩余时间**：`剩余步数 ÷ SPS = 剩余秒数`。
例：50 万步预算、已跑 7000 步、SPS=18 → `(500000-7000) / 18 ≈ 27444 秒 ≈ 7.6 小时`。

---

## 4. TensorBoard 十个指标逐讲

数据来自 `ec_smoke_v1`（8000 步冒烟测试）。10 个指标分三组：

### 组一：学习效果的"成绩单"（看学没学动）

**`Environment/Cumulative Reward`**（= `Policy/Extrinsic Reward`，同一指标两个名字）

- 真实数据：step 1000 = -1.140 → 5000 = -1.138 → 8000 = -1.154（一条平线）
- 怎么读：**最关键的指标**。成功训练的标志是它整体向上爬（本游戏赢 +2 / 死 -1，学会赢应趋近正值）
- 注意：它是滑动平均，会抖；**看趋势不看单点**

**`Environment/Episode Length`**

- 真实数据：31.9 → 39.3 → 31.5
- 怎么读：一局平均跑多少步。变长 = 更会苟/拖；变短 = 更早分出胜负。和 reward 合看

### 组二：学习动态的"仪表盘"（看学得稳不稳）

**`Losses/Policy Loss`**（策略损失）

- 真实数据：0.0425
- 怎么读：策略网络的"遗憾度"。正常在 0.01~0.1 小幅波动；**剧烈飙升 = 更新步长太大，要崩**

**`Losses/Value Loss`**（价值损失）

- 真实数据：0.0807
- 怎么读：价值网络（critic）对"未来回报"预测准不准。**应缓慢下降**（预测越来越准）。涨=预测在变差

**`Policy/Entropy`**（熵）

- 真实数据：6.38 → 6.39 → 6.40（几乎不动）
- 怎么读：策略的随机/犹豫程度。**健康训练应缓慢下降**（agent 越来越确定）。不动 = 还在乱逛没形成偏好；**断崖归零 = 过早锁死**，危险

**`Policy/Epsilon`**（裁剪阈值）

- 真实数据：0.1917（初始 0.2，线性衰减中）
- 怎么读：PPO 的"保险丝"宽度，随训练线性缩小。**不用管它**，确认在减小即可

**`Policy/Beta`**（熵系数）

- 真实数据：0.005 恒定（配置 `beta_schedule: constant`）
- 怎么读：鼓励探索的强度。想让它更爱探索就调大；想更专注就调小

**`Policy/Learning Rate`**

- 真实数据：0.0003 恒定（配置 `learning_rate_schedule: linear`，但早期还没开始衰减）
- 怎么读：学习率曲线。正常情况下应**从初始值线性降到 0**（随训练进度）。未衰减 = 离 max_steps 还远

### 组三：价值判断的"准心"（看学得对不对）

**`Policy/Extrinsic Value Estimate`**（价值估计）

- 真实数据：+0.033 → -0.436 → -0.485（往下走）
- 怎么读：价值网络预测"当前局面值多少分"。**应和 Cumulative Reward 逐渐趋近**（critic 学着预测真实回报）
- 冒烟数据里 reward 在 -1.14、value 掉到 -0.485——两者在靠近 = 健康迹象（critic 在努力对齐）
- 面试点：value 与 reward 的差就是"TD 误差"的来源，即 critic 的后悔程度

---

## 5. 训练健康体检清单

| 看什么 | 健康的表现 | 危险信号 |
|---|---|---|
| Cumulative Reward | 整体上升 | 一条死线 / 持续暴跌 |
| Value Loss | 缓慢下降 | 剧烈震荡 |
| Entropy | 缓慢下降 | 断崖式归零（过早收敛） |
| Policy Loss | 0.01~0.1 小幅波动 | 突然爆到 >0.5 |
| Value Estimate | 跟 Reward 靠拢 | 和 Reward 背道而驰 |
| Episode Length | 有变化（长/短都行） | 一直不动 |

**smoke 数据体检结论**：reward 平线、entropy 不动、policy loss 稳、value 在靠拢 → "8000 步太早啥也没学到，但链路健康没崩"。**这其实是好的冒烟结果**。

---

## 6. 几个最容易混淆的数字

| 数字 | 出现在哪 | 是什么 | 影响 agent 吗 |
|---|---|---|---|
| `batch_size: 256` | `hyperparameters` | 每次更新时切小批的粒度 | 只影响一次更新怎么算，不影响时机 |
| `buffer_size: 4096` | `hyperparameters` | 攒经验的蓄水池容量 | **攒满就更新一次**——决定更新时机 |
| `summary_freq: 10000` | 训练配置 | 日志/曲线打点的频率 | 否，只影响记录密度 |
| `checkpoint_interval: 100000` | 训练配置 | 存档频率 | 否，只影响备份多少 |
| `max_steps: 500000` | 训练配置 | 总步数预算 | 否，到点自动停 |
| `150`（文件名） | `-150.pt` | 上次崩溃时的实际步数 | 否，纯意外快照 |

**agent 改变的真实节奏**：

```
每一步决策 → 经验进 buffer（+1）
                    │
            buffer 攒到 4096 条
                    │
            ★ 做一次 PPO 更新 ★
                    │
        切成 256 的小批，每批算一次梯度
        共 4096/256 = 16 次小步更新
```

- agent 在**每攒满 4096 步时**更新一次；256 只是更新内部的切块粒度
- **单次更新极其微小**（有 `epsilon` 裁剪限制），要变到"明显变强"需要成百上千轮更新（几十万步）
- 3332 步时连第一次完整更新（4096）都没攒够——所以看不出变化是正常的

---

## 7. 几千万步训练：实际要盯什么

1. **Cumulative Reward 曲线整体趋势**（每几万步扫一眼）
2. **Value Loss + Entropy**（确认没崩、没过早锁死）
3. **SPS**（步/秒，估算剩余时间）

其它指标（epsilon、beta、lr）是"环境背景板"，偶尔确认正常即可。

### 启动训练台全家桶（面板 + 曲线）

```powershell
cd Tools/rl
# 终端1：面板
.\.venv\Scripts\python -m streamlit run app\streamlit_app.py --server.port 8501 --browser.gatherUsageStats false
# 终端2：TensorBoard
.\.venv\Scripts\python -m tensorboard.main --logdir results --port 6006
# 然后面板里点「③ 一键 A+B」开始训练
```
