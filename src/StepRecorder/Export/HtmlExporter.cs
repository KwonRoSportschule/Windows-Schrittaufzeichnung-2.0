using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using StepRecorder.Core;
using StepRecorder.Services;

namespace StepRecorder.Export;

/// <summary>Single self-contained HTML file: images embedded as base64, no external resources.</summary>
public static class HtmlExporter
{
    public static void Export(ExportData data, string path, IProgress<double>? progress = null)
    {
        var culture = Loc.Instance.Culture;
        var steps = data.Steps.Select((s, i) =>
        {
            string img = "";
            if (File.Exists(s.ImagePath))
            {
                var (jpeg, _, _) = ScreenCapture.LoadAsJpeg(s.ImagePath, data.JpegQuality, 2400);
                img = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);
            }
            progress?.Report((i + 1) / (double)data.Steps.Count);
            return new
            {
                n = s.Number,
                title = Loc.F("Step_N", s.Number),
                desc = s.Description,
                note = s.Note,
                time = s.Time.ToString("T", culture),
                window = s.Window,
                img
            };
        }).ToList();

        var labels = new
        {
            search = Loc.T("Html_Search"),
            all = Loc.T("Html_All"),
            single = Loc.T("Html_Single"),
            prev = Loc.T("Html_Prev"),
            next = Loc.T("Html_Next"),
            theme = Loc.T("Html_Theme"),
            print = Loc.T("Html_Print"),
            hint = Loc.T("Html_Hint"),
            note = Loc.T("Rep_Note"),
            noResults = Loc.T("Html_NoResults"),
            stepOf = Loc.T("Lbl_StepOf")
        };

        // System.Text.Json escapes <, > and & by default, so the JSON is safe inside <script>.
        var json = JsonSerializer.Serialize(new { steps, labels });
        var meta = $"{Loc.F("Rep_RecordedOn", data.Created.ToString("f", culture))} · {Loc.F("Rep_Steps", data.Steps.Count)}";

        var html = new StringBuilder(Template)
            .Replace("{{LANG}}", Loc.Instance.Language)
            .Replace("{{TITLE}}", WebUtility.HtmlEncode(data.Title))
            .Replace("{{META}}", WebUtility.HtmlEncode(meta))
            .Replace("{{FOOTER}}", WebUtility.HtmlEncode(Loc.T("Rep_CreatedWith")))
            .Replace("{{DATA}}", json)
            .ToString();
        File.WriteAllText(path, html, new UTF8Encoding(false));
    }

    private const string Template = """
<!DOCTYPE html>
<html lang="{{LANG}}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="generator" content="StepRecorder 2.0">
<title>{{TITLE}}</title>
<style>
:root{--bg:#f3f3f3;--card:#fff;--text:#1a1a1a;--muted:#5f5f5f;--border:#e5e5e5;--accent:#0067c0;--accent-text:#fff;--hover:#f0f0f0;--sel:#e8f1fb;--note:#fff8e1;--note-b:#f2b01e;color-scheme:light}
:root[data-theme=dark]{--bg:#202020;--card:#2b2b2b;--text:#fff;--muted:#c5c5c5;--border:#3a3a3a;--accent:#4cc2ff;--accent-text:#000;--hover:#353535;--sel:#1f3a50;--note:#3a3220;--note-b:#f2b01e;color-scheme:dark}
@media (prefers-color-scheme:dark){:root:not([data-theme=light]){--bg:#202020;--card:#2b2b2b;--text:#fff;--muted:#c5c5c5;--border:#3a3a3a;--accent:#4cc2ff;--accent-text:#000;--hover:#353535;--sel:#1f3a50;--note:#3a3220;--note-b:#f2b01e;color-scheme:dark}}
*{box-sizing:border-box}
body{margin:0;font:14px/1.5 "Segoe UI Variable Text","Segoe UI",system-ui,-apple-system,sans-serif;background:var(--bg);color:var(--text)}
header{position:sticky;top:0;z-index:5;display:flex;flex-wrap:wrap;gap:12px;align-items:center;justify-content:space-between;padding:14px 24px;background:var(--card);border-bottom:1px solid var(--border)}
header h1{margin:0;font-size:20px;font-weight:600}
header .meta{color:var(--muted);font-size:12px}
.tools{display:flex;gap:8px;flex-wrap:wrap}
button,input{font:inherit;color:inherit}
button{background:var(--card);border:1px solid var(--border);border-radius:6px;padding:6px 12px;cursor:pointer}
button:hover{background:var(--hover)}
button.primary{background:var(--accent);color:var(--accent-text);border-color:transparent}
input[type=search]{background:var(--bg);border:1px solid var(--border);border-radius:6px;padding:6px 10px;min-width:220px}
.layout{display:grid;grid-template-columns:320px 1fr;gap:16px;padding:16px 24px;max-width:1600px;margin:0 auto}
aside{background:var(--card);border:1px solid var(--border);border-radius:8px;padding:6px;height:calc(100vh - 110px);overflow:auto;position:sticky;top:90px}
.item{display:flex;gap:10px;padding:8px;border-radius:6px;cursor:pointer;align-items:flex-start;position:relative}
.item:hover{background:var(--hover)}
.item.sel{background:var(--sel)}
.item.sel::before{content:"";position:absolute;left:0;top:12px;bottom:12px;width:3px;border-radius:2px;background:var(--accent)}
.item img{width:84px;height:54px;object-fit:cover;border-radius:4px;border:1px solid var(--border);flex:none}
.num{flex:none;min-width:24px;height:24px;border-radius:12px;background:var(--accent);color:var(--accent-text);font-size:12px;font-weight:600;display:grid;place-items:center;padding:0 6px}
.item .d{font-size:12.5px;display:-webkit-box;-webkit-line-clamp:3;-webkit-box-orient:vertical;overflow:hidden}
main{min-width:0}
.step{background:var(--card);border:1px solid var(--border);border-radius:8px;padding:18px 20px;margin-bottom:16px}
.step h2{margin:0 0 2px;font-size:16px;color:var(--accent);display:flex;justify-content:space-between;gap:12px}
.step h2 small{color:var(--muted);font-weight:400;font-size:12px}
.step p{margin:6px 0 10px}
.note{background:var(--note);border-left:3px solid var(--note-b);padding:8px 12px;border-radius:4px;margin:8px 0 12px;white-space:pre-wrap;font-style:italic}
.shot{display:block;max-width:100%;border:1px solid var(--border);border-radius:6px;cursor:zoom-in}
.nav{display:flex;justify-content:space-between;align-items:center;margin-top:12px;color:var(--muted);font-size:12px}
.hidden{display:none!important}
#lightbox{position:fixed;inset:0;background:rgba(0,0,0,.88);display:flex;align-items:center;justify-content:center;z-index:20;cursor:zoom-out;padding:16px}
#lightbox img{max-width:100%;max-height:100%;box-shadow:0 8px 40px rgba(0,0,0,.5)}
footer{text-align:center;color:var(--muted);font-size:12px;padding:8px 0 24px}
@media (max-width:820px){.layout{grid-template-columns:1fr}aside{position:static;height:auto;max-height:40vh}}
@media print{header .tools,aside,.nav,#lightbox{display:none!important}.layout{display:block;padding:0}.step{break-inside:avoid;border:none;padding:8px 0}.hidden{display:block!important}body{background:#fff;color:#000}header{position:static;border:none}}
</style>
</head>
<body>
<header>
  <div><h1>{{TITLE}}</h1><div class="meta">{{META}}</div></div>
  <div class="tools">
    <input type="search" id="q">
    <button id="mode" class="primary"></button>
    <button id="theme"></button>
    <button id="print"></button>
  </div>
</header>
<div class="layout">
  <aside id="list"></aside>
  <main id="view"></main>
</div>
<footer>{{FOOTER}}</footer>
<div id="lightbox" class="hidden"><img alt=""></div>
<script>
const DATA = {{DATA}};
const L = DATA.labels, S = DATA.steps;
let cur = 0, showAll = false, visible = S.map((_, i) => i);
const $ = id => document.getElementById(id);
const esc = s => (s || "").replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));

$("q").placeholder = L.search; $("theme").textContent = L.theme; $("print").textContent = L.print;

function card(i) {
  const s = S[i];
  return `<section class="step" id="s${s.n}">
    <h2><span>${esc(s.title)}</span><small>${esc(s.window)}${s.window ? " · " : ""}${esc(s.time)}</small></h2>
    <p>${esc(s.desc)}</p>
    ${s.note ? `<div class="note">${esc(s.note)}</div>` : ""}
    ${s.img ? `<img class="shot" src="${s.img}" alt="${esc(s.title)}" data-i="${i}">` : ""}
    ${showAll ? "" : `<div class="nav"><button id="prev">← ${esc(L.prev)}</button>
      <span>${esc(L.stepOf.replace("{0}", visible.indexOf(i) + 1).replace("{1}", visible.length))} · ${esc(L.hint)}</span>
      <button id="next">${esc(L.next)} →</button></div>`}
  </section>`;
}

function render() {
  $("mode").textContent = showAll ? L.single : L.all;
  $("list").innerHTML = visible.length ? visible.map(i => {
    const s = S[i];
    return `<div class="item ${i === cur ? "sel" : ""}" data-i="${i}"><span class="num">${s.n}</span>
      ${s.img ? `<img src="${s.img}" alt="" loading="lazy">` : ""}<div class="d">${esc(s.desc)}</div></div>`;
  }).join("") : `<div class="item">${esc(L.noResults)}</div>`;
  if (!visible.length) { $("view").innerHTML = ""; return; }
  if (!visible.includes(cur)) cur = visible[0];
  $("view").innerHTML = showAll ? visible.map(card).join("") : card(cur);
  if (!showAll) {
    $("prev").onclick = () => go(-1);
    $("next").onclick = () => go(1);
  }
  const sel = document.querySelector(".item.sel");
  if (sel) sel.scrollIntoView({ block: "nearest" });
}

function go(d) {
  const p = visible.indexOf(cur) + d;
  if (p >= 0 && p < visible.length) { cur = visible[p]; render(); if (!showAll) window.scrollTo({ top: 0 }); }
}

$("list").onclick = e => {
  const it = e.target.closest(".item[data-i]");
  if (!it) return;
  cur = +it.dataset.i;
  render();
  if (showAll) document.getElementById("s" + S[cur].n).scrollIntoView({ behavior: "smooth" });
};
$("view").onclick = e => {
  if (e.target.classList.contains("shot")) {
    $("lightbox").querySelector("img").src = e.target.src;
    $("lightbox").classList.remove("hidden");
  }
};
$("lightbox").onclick = () => $("lightbox").classList.add("hidden");
$("mode").onclick = () => { showAll = !showAll; render(); };
$("print").onclick = () => { showAll = true; render(); setTimeout(() => window.print(), 50); };
$("theme").onclick = () => {
  const r = document.documentElement;
  const dark = r.dataset.theme ? r.dataset.theme === "dark" : matchMedia("(prefers-color-scheme: dark)").matches;
  r.dataset.theme = dark ? "light" : "dark";
};
$("q").oninput = e => {
  const q = e.target.value.trim().toLowerCase();
  visible = S.map((s, i) => i).filter(i => !q || (S[i].desc + " " + S[i].note + " " + S[i].window).toLowerCase().includes(q));
  render();
};
document.onkeydown = e => {
  if (e.target.tagName === "INPUT") return;
  if (e.key === "Escape") $("lightbox").classList.add("hidden");
  if (showAll) return;
  if (e.key === "ArrowRight" || e.key === "ArrowDown") { go(1); e.preventDefault(); }
  if (e.key === "ArrowLeft" || e.key === "ArrowUp") { go(-1); e.preventDefault(); }
};
render();
</script>
</body>
</html>
""";
}
