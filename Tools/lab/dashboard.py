#!/usr/bin/env python3
"""生成一个单文件仪表盘（HTML），给 qa 与 balance 看结果。

    python Tools/lab/dashboard.py                 # 用本地 SQLite
    python Tools/lab/dashboard.py --dsn "mysql://user:pw@host/elephant_crisis"
    python Tools/lab/dashboard.py --checks        # 同时跑不变量检查（慢一些）

产物：Tools/lab/reports/dashboard.html —— 双击即可打开，无依赖、可离线、可发人。

设计取舍：
- 不引第三方前端框架，不启服务：一个文件搞定，避免又长出一个 Streamlit 应用
- 指标与检查结果全部复用 balance / qa 的现成代码，页面只负责展示
- 交互（切换实验、两实验对比）用原生 JS 在客户端完成
"""
from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "Tools"))

from lab.report import write_markdown  # noqa: F401,E402  (保持 exports 一致)
from lab.run import DEFAULT_DSN  # noqa: E402
from lab.store import Store  # noqa: E402

from balance.metrics import aggregate_logs  # noqa: E402
from balance.stats import wilson_interval  # noqa: E402

OUT_PATH = REPO_ROOT / "Tools" / "lab" / "reports" / "dashboard.html"


def collect(store: Store, with_checks: bool) -> dict:
    """把页面需要的全部数据算出来。"""
    experiments = []
    for exp in store.list_experiments():
        name = exp["name"]
        try:
            records = store.load_matches(name, with_events=with_checks)
        except KeyError:
            continue

        report = aggregate_logs(records)
        summary = report["summary"]

        roles = {}
        for role, info in report["by_role"].items():
            n = info["matches"]
            if not n:
                continue
            lo, hi = wilson_interval(info["wins"], n)
            roles[role] = {
                "n": n,
                "wins": info["wins"],
                "win_rate": info["win_rate"],
                "ci": [round(lo, 4), round(hi, 4)],
                "avg_death_round": (info.get("survival_rounds") or {}).get("mean"),
            }

        violations = None
        if with_checks:
            from qa.runner import run_checks

            tmp = OUT_PATH.parent / "_tmp_reports" / name
            rep = run_checks(records, tmp)
            s = rep["summary"]
            violations = {
                "total": s["total_violations"],
                "blocking": s["blocking_violations"],
                "advisory": s["advisory_violations"],
                "by_rule": s["by_rule"],
                "advisory_rules": s["advisory_rules"],
            }

        experiments.append({
            "name": name,
            "created_at": exp.get("created_at"),
            "matches": summary["matches"],
            "seed_range": [exp.get("seed_start"), exp.get("seed_end")],
            "collect": exp.get("collect"),
            "config_fingerprint": exp.get("config_fingerprint"),
            "code_revision": exp.get("code_revision"),
            "avg_rounds": summary["avg_full_rounds"],
            "rounds": summary.get("full_rounds"),
            "reasons": summary.get("end_reasons"),
            "roles": roles,
            "violations": violations,
            "metas": [
                {
                    "seed": r.meta.get("seed"),
                    "winner": r.meta.get("winner"),
                    "reason": r.meta.get("reason"),
                    "rounds": r.meta.get("full_rounds"),
                }
                for r in records
            ],
        })

    return {
        "generated_at": datetime.now().isoformat(timespec="seconds"),
        "with_checks": with_checks,
        "experiments": experiments,
    }


def render(data: dict) -> str:
    payload = json.dumps(data, ensure_ascii=False)
    return _TEMPLATE.replace("__DATA__", payload)


def main() -> int:
    p = argparse.ArgumentParser(description="生成 qa/balance 的 HTML 仪表盘")
    p.add_argument("--dsn", default=None)
    p.add_argument("--checks", action="store_true", help="同时跑不变量检查")
    p.add_argument("--out", default=None)
    args = p.parse_args()

    store = Store(args.dsn or DEFAULT_DSN)
    data = collect(store, args.checks)
    if not data["experiments"]:
        print("[dashboard] 数据库里还没有实验数据，先跑 python Tools/lab/run.py --name v1")
        return 2

    out = Path(args.out) if args.out else OUT_PATH
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(render(data), encoding="utf-8")
    print(f"[dashboard] 已生成 {out}（{len(data['experiments'])} 个实验）")
    print(f"[dashboard] 直接用浏览器打开：file:///{out.as_posix()}")
    return 0


_TEMPLATE = r"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>象群危机 · 质量与数值</title>
<style>
  :root{--bg:#f7f7f8;--card:#fff;--line:#e6e6ea;--ink:#1c1c1e;--dim:#6b6b73;
        --good:#1a7f37;--warn:#b26a00;--bad:#c01c28;--accent:#2f6fdf}
  *{box-sizing:border-box}
  body{margin:0;font:14px/1.6 -apple-system,"Segoe UI","Microsoft YaHei",sans-serif;
       background:var(--bg);color:var(--ink)}
  header{padding:18px 24px;border-bottom:1px solid var(--line);background:var(--card);
         display:flex;align-items:baseline;gap:16px;flex-wrap:wrap}
  header h1{font-size:17px;margin:0}
  header .dim{color:var(--dim);font-size:12px}
  main{display:grid;grid-template-columns:240px 1fr;min-height:calc(100vh - 62px)}
  aside{border-right:1px solid var(--line);background:var(--card);padding:12px;overflow:auto}
  aside .exp{padding:8px 10px;border-radius:8px;cursor:pointer;margin-bottom:4px}
  aside .exp:hover{background:#f0f2f7}
  aside .exp.active{background:#e6efff;color:var(--accent);font-weight:600}
  aside .exp small{display:block;color:var(--dim);font-weight:400}
  section{padding:20px 24px;overflow:auto}
  .cards{display:flex;gap:12px;flex-wrap:wrap;margin-bottom:18px}
  .card{background:var(--card);border:1px solid var(--line);border-radius:10px;
        padding:12px 16px;min-width:130px}
  .card b{display:block;font-size:22px;line-height:1.3}
  .card span{color:var(--dim);font-size:12px}
  table{border-collapse:collapse;width:100%;background:var(--card);
        border:1px solid var(--line);border-radius:10px;overflow:hidden}
  th,td{padding:8px 12px;text-align:left;border-bottom:1px solid var(--line);font-size:13px}
  th{background:#fafafc;color:var(--dim);font-weight:600}
  tr:last-child td{border-bottom:none}
  .tag{display:inline-block;padding:1px 7px;border-radius:99px;font-size:12px}
  .ok{background:#e7f5ec;color:var(--good)}
  .warn{background:#fdf0dc;color:var(--warn)}
  .bad{background:#fde8e9;color:var(--bad)}
  h2{font-size:14px;margin:22px 0 10px;color:var(--dim);text-transform:uppercase;
     letter-spacing:.04em}
  .bar{height:8px;background:#eceef3;border-radius:99px;overflow:hidden;min-width:80px}
  .bar>i{display:block;height:100%;background:var(--accent)}
  select{padding:5px 8px;border:1px solid var(--line);border-radius:7px;background:#fff}
  .muted{color:var(--dim)}
  footer{padding:14px 24px;color:var(--dim);font-size:12px;border-top:1px solid var(--line)}
</style>
</head>
<body>
<header>
  <h1>象群危机 · 质量与数值</h1>
  <span class="dim" id="meta"></span>
</header>
<main>
  <aside id="list"></aside>
  <section id="view"></section>
</main>
<footer>数据来自实验数据库；本页为静态快照，重新跑 <code>python Tools/lab/dashboard.py</code> 可刷新。</footer>

<script>
const DATA = __DATA__;

function pct(x){ return x==null? "–" : (x*100).toFixed(1)+"%"; }
function ci(c){ return c? `[${(c[0]*100).toFixed(0)}–${(c[1]*100).toFixed(0)}%]` : ""; }
function esc(s){ return String(s==null?"":s).replace(/[&<>]/g, m=>({"&":"&amp;","<":"&lt;",">":"&gt;"}[m])); }

document.getElementById("meta").textContent =
  `生成于 ${DATA.generated_at} · ${DATA.experiments.length} 个实验` +
  (DATA.with_checks ? " · 含不变量检查" : " · 未跑检查（加 --checks）");

let current = DATA.experiments[DATA.experiments.length-1].name;

function renderList(){
  const box = document.getElementById("list");
  box.innerHTML = DATA.experiments.map(e=>
    `<div class="exp ${e.name===current?'active':''}" data-name="${esc(e.name)}">
       ${esc(e.name)}
       <small>${e.matches} 局 · ${esc(e.code_revision||"?")}</small>
     </div>`).join("");
  box.querySelectorAll(".exp").forEach(el=>{
    el.onclick = ()=>{ current = el.dataset.name; renderList(); renderView(); };
  });
}

function expByName(n){ return DATA.experiments.find(e=>e.name===n); }

function renderView(){
  const e = expByName(current);
  if(!e) return;
  const roles = Object.entries(e.roles||{}).sort((a,b)=>b[1].win_rate-a[1].win_rate);
  const reasons = Object.entries(e.reasons||{});
  const r = e.rounds||{};

  const v = e.violations;
  const health = v
    ? (v.blocking>0 ? `<span class="tag bad">阻断 ${v.blocking}</span>`
       : v.total>0 ? `<span class="tag warn">仅参考 ${v.advisory}</span>`
       : `<span class="tag ok">无违规</span>`)
    : `<span class="muted">未检查</span>`;

  document.getElementById("view").innerHTML = `
    <div class="cards">
      <div class="card"><span>对局</span><b>${e.matches}</b></div>
      <div class="card"><span>平均回合</span><b>${e.avg_rounds==null?"–":e.avg_rounds.toFixed(1)}</b>
        <span>中位 ${r.median ?? "–"} / p90 ${r.p90 ?? "–"}</span></div>
      <div class="card"><span>健康</span><b style="font-size:15px">${health}</b></div>
      <div class="card"><span>配置指纹</span><b style="font-size:14px">${esc(e.config_fingerprint||"–")}</b>
        <span>${esc(e.code_revision||"")}</span></div>
    </div>

    <h2>角色胜率（95% 区间）</h2>
    <table><tr><th>角色</th><th>胜场</th><th>胜率</th><th style="width:32%">区间</th></tr>
      ${roles.map(([role,i])=>`<tr>
        <td>${esc(role)}</td><td>${i.wins}/${i.n}</td>
        <td><b>${pct(i.win_rate)}</b> <span class="muted">${ci(i.ci)}</span></td>
        <td><div class="bar"><i style="width:${(i.win_rate*100).toFixed(1)}%"></i></div></td>
      </tr>`).join("") || `<tr><td colspan="4" class="muted">无数据</td></tr>`}
    </table>

    <h2>结束原因</h2>
    <table><tr><th>原因</th><th>局数</th></tr>
      ${reasons.map(([k,n])=>`<tr><td>${esc(k)}</td><td>${n}</td></tr>`).join("") ||
        `<tr><td colspan="2" class="muted">无数据</td></tr>`}
    </table>

    ${v ? `<h2>不变量违规</h2>
      <table><tr><th>规则</th><th>次数</th><th>级别</th></tr>
      ${Object.entries(v.by_rule||{}).sort((a,b)=>b[1]-a[1]).map(([rule,n])=>{
        const adv = (v.advisory_rules||[]).includes(rule);
        return `<tr><td>${esc(rule)}</td><td>${n}</td>
          <td>${adv?`<span class="tag warn">参考</span>`:`<span class="tag bad">阻断</span>`}</td></tr>`;
      }).join("") || `<tr><td colspan="3" class="muted">无</td></tr>`}
      </table>`:""}

    <h2>与另一个实验对比</h2>
    <div class="muted" style="margin-bottom:8px">
      选一个基线，按种子逐局比较胜者是否一致（同种子才有意义）。
    </div>
    <select id="base">
      ${DATA.experiments.filter(x=>x.name!==e.name)
        .map(x=>`<option value="${esc(x.name)}">${esc(x.name)}（${x.matches} 局）</option>`).join("")
        || `<option>（没有其他实验）</option>`}
    </select>
    <div id="cmp" style="margin-top:12px"></div>
  `;

  const sel = document.getElementById("base");
  if(sel) sel.onchange = ()=>renderCompare(e, expByName(sel.value));
  if(sel && DATA.experiments.length>1) renderCompare(e, expByName(sel.value));
}

function renderCompare(a, b){
  const box = document.getElementById("cmp");
  if(!b){ box.innerHTML = ""; return; }
  const bi = new Map(b.metas.map(m=>[m.seed, m]));
  let common=0, same=0, diffs=[];
  for(const m of a.metas){
    const o = bi.get(m.seed);
    if(!o) continue;
    common++;
    if(o.winner===m.winner && o.reason===m.reason && o.rounds===m.rounds) same++;
    else diffs.push({seed:m.seed, a:`${m.winner??"-"}/${m.rounds}`, b:`${o.winner??"-"}/${o.rounds}`});
  }
  const verdict = diffs.length===0
    ? `<span class="tag ok">一致</span>`
    : `<span class="tag bad">${diffs.length} 局不同</span>`;
  box.innerHTML = `
    <div class="cards">
      <div class="card"><span>共同种子</span><b>${common}</b></div>
      <div class="card"><span>一致</span><b>${same}</b></div>
      <div class="card"><span>不同</span><b>${diffs.length}</b></div>
      <div class="card"><span>结论</span><b style="font-size:15px">${verdict}</b></div>
    </div>
    ${diffs.length? `<table><tr><th>种子</th><th>${esc(a.name)}</th><th>${esc(b.name)}</th></tr>
      ${diffs.slice(0,20).map(d=>`<tr><td>${d.seed}</td><td>${esc(d.a)}</td><td>${esc(d.b)}</td></tr>`).join("")}
      </table>${diffs.length>20?`<div class="muted">仅显示前 20 条</div>`:""}`:""}
  `;
}

renderList();
renderView();
</script>
</body>
</html>
"""


if __name__ == "__main__":
    raise SystemExit(main())
