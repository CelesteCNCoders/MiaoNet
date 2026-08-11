using System.Net;
using System.Text;

namespace MiaoNet.Server;

public sealed partial class MiaoHttpService
{
    private async Task AdminPageAsync(HttpListenerContext context, AdminSessionStore.AdminSession session)
    {
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        string html = $$$"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>管理后台 - MiaoNet</title>
            <style>
            *{box-sizing:border-box;}
            html{color-scheme:dark;}
            body{
              background:#0a0e14;color:#c8d3d8;margin:0;
              font-family:"Segoe UI","Microsoft YaHei",sans-serif;
              background-image:
                linear-gradient(rgba(0,229,255,.025) 1px,transparent 1px),
                linear-gradient(90deg,rgba(0,229,255,.025) 1px,transparent 1px);
              background-size:44px 44px;
            }
            body::before{
              content:"";position:fixed;inset:0;pointer-events:none;z-index:60;
              background:repeating-linear-gradient(0deg,rgba(255,255,255,.018) 0 1px,transparent 1px 3px);
            }
            a{color:#00e5ff;text-decoration:none;}
            .mono{font-family:Consolas,"Courier New",monospace;}

            /* ---------- header ---------- */
            header{display:flex;align-items:center;gap:16px;padding:0 24px;height:52px;background:rgba(10,14,20,.92);
              border-bottom:1px solid rgba(0,229,255,.25);position:sticky;top:0;z-index:50;}
            .brand{font-size:15px;font-weight:600;letter-spacing:3px;color:#00e5ff;font-family:Consolas,"Courier New",monospace;
              text-shadow:0 0 8px rgba(0,229,255,.5);white-space:nowrap;}
            .brand-sub{color:#5f7a85;letter-spacing:2px;margin-left:8px;font-size:12px;font-family:"Segoe UI","Microsoft YaHei",sans-serif;text-shadow:none;}
            .live{display:flex;align-items:center;gap:6px;font-size:12px;color:#00ff9c;letter-spacing:2px;white-space:nowrap;}
            .live .bcursor{color:#00ff9c;font-family:Consolas,monospace;animation:blink 1.1s steps(1) infinite;text-shadow:0 0 6px rgba(0,255,156,.7);}
            .live.down{color:#ff3860;}
            .live.down .bcursor{color:#ff3860;text-shadow:0 0 6px rgba(255,56,96,.7);}
            @keyframes blink{50%{opacity:0;}}
            #conn-term{font-family:Consolas,"Courier New",monospace;font-size:11px;line-height:1.45;color:#4f99a8;
              overflow:hidden;white-space:nowrap;}
            #conn-term div{overflow:hidden;text-overflow:ellipsis;}
            #conn-term div:last-child{color:#8fd8e8;}
            #conn-term div{animation:lineIn .25s ease;}
            .spacer{flex:1;}
            .user{font-size:12px;color:#5f7a85;font-family:Consolas,"Courier New",monospace;white-space:nowrap;}
            .logout{font-size:12px;padding:5px 12px;border:1px solid rgba(0,229,255,.3);letter-spacing:1px;transition:all .15s;white-space:nowrap;}
            .logout:hover{background:rgba(0,229,255,.08);box-shadow:0 0 10px rgba(0,229,255,.25);}

            /* ---------- layout ---------- */
            main{padding:18px 24px 60px;max-width:1560px;margin:0 auto;display:grid;grid-template-columns:repeat(12,1fr);gap:16px;}
            .cards{grid-column:1/-1;display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:12px;}
            .sec-players{grid-column:span 7;}
            .sec-chat{grid-column:span 5;}
            .sec-logs{grid-column:1/-1;}
            .sec-metrics{grid-column:1/-1;}
            @media (max-width:1100px){
              .sec-players,.sec-chat{grid-column:1/-1;}
              #conn-term{display:none;}
            }

            /* ---------- panels (直角科技风) ---------- */
            .panel{background:rgba(13,17,23,.9);border:1px solid rgba(0,229,255,.18);padding:14px 16px;position:relative;
              box-shadow:inset 0 0 30px rgba(0,229,255,.02);animation:panelIn .3s ease;}
            .panel::before,.panel::after{content:"";position:absolute;width:10px;height:10px;pointer-events:none;}
            .panel::before{top:-1px;left:-1px;border-top:1px solid rgba(0,229,255,.7);border-left:1px solid rgba(0,229,255,.7);}
            .panel::after{bottom:-1px;right:-1px;border-bottom:1px solid rgba(0,229,255,.7);border-right:1px solid rgba(0,229,255,.7);}
            @keyframes panelIn{from{opacity:0;transform:translateY(8px);}to{opacity:1;transform:none;}}
            .sec-title{font-size:12px;margin:0 0 12px;color:#9fdcec;letter-spacing:4px;text-transform:uppercase;font-weight:600;
              display:flex;align-items:center;gap:8px;}
            .sec-title::before{content:"";width:8px;height:8px;background:#00e5ff;box-shadow:0 0 8px rgba(0,229,255,.8);flex:none;}
            .sec-title.sub{margin-top:16px;}

            /* ---------- stat cards ---------- */
            .card{background:rgba(13,17,23,.9);border:1px solid rgba(0,229,255,.18);padding:12px 14px;position:relative;
              transition:transform .15s,box-shadow .15s,border-color .15s;}
            .card:hover{transform:translateY(-2px);border-color:rgba(0,229,255,.5);box-shadow:0 0 16px rgba(0,229,255,.15);}
            .card .label{font-size:11px;color:#5f7a85;margin-bottom:6px;letter-spacing:2px;}
            .card .value{font-size:24px;font-weight:600;font-variant-numeric:tabular-nums;color:#00e5ff;
              font-family:Consolas,"Courier New",monospace;text-shadow:0 0 10px rgba(0,229,255,.4);}

            /* ---------- table ---------- */
            .table-wrap{max-height:320px;overflow-y:auto;border:1px solid rgba(0,229,255,.1);}
            table{border-collapse:collapse;width:100%;font-family:Consolas,"Courier New",monospace;}
            th,td{border-bottom:1px solid rgba(0,229,255,.08);padding:7px 10px;text-align:left;font-size:12px;}
            th{color:#4f99a8;font-weight:500;background:rgba(0,229,255,.05);letter-spacing:1px;position:sticky;top:0;}
            tbody tr{transition:background .15s;}
            tbody tr:hover{background:rgba(0,229,255,.05);}
            tr.row-new{animation:rowIn .4s ease;}
            @keyframes rowIn{from{opacity:0;transform:translateX(-10px);}to{opacity:1;transform:none;}}

            /* ---------- inputs & buttons ---------- */
            input{background:#0a0e14;border:1px solid rgba(0,229,255,.25);color:#c8d3d8;padding:8px 10px;font-size:13px;
              font-family:Consolas,"Courier New",monospace;transition:border-color .15s,box-shadow .15s;}
            input:focus{outline:none;border-color:#00e5ff;box-shadow:0 0 0 1px rgba(0,229,255,.35),0 0 12px rgba(0,229,255,.15);}
            input::placeholder{color:#3d5560;}
            button{font-family:inherit;letter-spacing:1px;}
            button.act{background:rgba(0,229,255,.1);color:#00e5ff;border:1px solid rgba(0,229,255,.5);padding:8px 16px;
              cursor:pointer;font-size:13px;transition:all .15s;}
            button.act:hover{background:rgba(0,229,255,.2);box-shadow:0 0 12px rgba(0,229,255,.3);}
            button.act:active{transform:translateY(1px);}
            button.kick{background:none;border:1px solid rgba(255,56,96,.5);color:#ff6b81;padding:4px 10px;cursor:pointer;
              font-size:12px;font-family:Consolas,"Courier New",monospace;transition:all .15s;}
            button.kick:hover{background:rgba(255,56,96,.15);box-shadow:0 0 10px rgba(255,56,96,.3);color:#ff3860;}
            button.kick:active{transform:translateY(1px);}
            .bar{display:flex;gap:10px;margin-bottom:12px;}
            .bar input{flex:1;max-width:480px;}

            /* ---------- streams ---------- */
            .stream-wrap{position:relative;}
            .stream{background:#070a0f;border:1px solid rgba(0,229,255,.12);overflow-y:auto;padding:10px 14px;
              font-size:12px;line-height:1.7;font-family:Consolas,"Courier New",monospace;}
            #chat-stream{height:438px;}
            #log-stream{height:320px;}
            .stream .line{animation:lineIn .3s ease;word-break:break-all;}
            @keyframes lineIn{from{opacity:0;transform:translateY(6px);}to{opacity:1;transform:none;}}
            .stream .time{color:#3d5560;font-variant-numeric:tabular-nums;margin-right:8px;font-size:11px;}
            .badge{display:inline-block;padding:0 6px;font-size:11px;margin-right:8px;border:1px solid currentColor;letter-spacing:1px;}
            .badge-global{color:#00e5ff;background:rgba(0,229,255,.07);}
            .badge-channel{color:#00ff9c;background:rgba(0,255,156,.06);}
            .badge-map{color:#b98cff;background:rgba(185,140,255,.07);}
            .badge-server{color:#ffb300;background:rgba(255,179,0,.07);}
            .chat-name{color:#9fdcec;margin-right:6px;}
            .lv-Trace,.lv-Debug{color:#4a5a63;}
            .lv-Information{color:#00e5ff;}
            .lv-Warning{color:#ffb300;}
            .lv-Error,.lv-Critical{color:#ff3860;}
            .log-cat{color:#3d5560;margin-right:8px;font-size:11px;}
            .log-exc{color:#ff3860;white-space:pre-wrap;font-size:11px;opacity:.85;}
            .filters{display:flex;gap:8px;margin-bottom:10px;}
            .filters button{background:none;border:1px solid rgba(0,229,255,.2);color:#5f7a85;padding:5px 14px;cursor:pointer;
              font-size:12px;font-family:Consolas,"Courier New",monospace;transition:all .15s;}
            .filters button:hover{border-color:rgba(0,229,255,.5);color:#9fdcec;}
            .filters button.active{background:rgba(0,229,255,.15);border-color:#00e5ff;color:#00e5ff;box-shadow:0 0 10px rgba(0,229,255,.2);}
            .back-bottom{position:absolute;right:14px;bottom:14px;background:rgba(10,14,20,.9);color:#00e5ff;
              border:1px solid rgba(0,229,255,.5);padding:6px 14px;cursor:pointer;font-size:12px;
              font-family:Consolas,"Courier New",monospace;display:none;animation:panelIn .2s ease;z-index:5;letter-spacing:1px;}
            .back-bottom:hover{background:rgba(0,229,255,.15);box-shadow:0 0 12px rgba(0,229,255,.3);}
            .empty{color:#3d5560;padding:18px;text-align:center;}

            /* ---------- toasts ---------- */
            #toasts{position:fixed;top:64px;right:24px;z-index:100;display:flex;flex-direction:column;gap:8px;}
            .toast{padding:10px 16px;font-size:12px;background:rgba(10,14,20,.95);max-width:380px;
              font-family:Consolas,"Courier New",monospace;letter-spacing:1px;animation:toastIn .25s ease;}
            .toast.ok{border:1px solid rgba(0,255,156,.6);color:#00ff9c;box-shadow:0 0 14px rgba(0,255,156,.15);}
            .toast.err{border:1px solid rgba(255,56,96,.6);color:#ff6b81;box-shadow:0 0 14px rgba(255,56,96,.15);}
            .toast.out{animation:toastOut .25s ease forwards;}
            @keyframes toastIn{from{opacity:0;transform:translateX(40px);}to{opacity:1;transform:none;}}
            @keyframes toastOut{to{opacity:0;transform:translateX(40px);}}

            /* ---------- charts ---------- */
            .charts-grid{display:grid;grid-template-columns:1fr 1fr;gap:12px;}
            @media (max-width:900px){.charts-grid{grid-template-columns:1fr;}}
            .chart-wrap{background:#070a0f;border:1px solid rgba(0,229,255,.12);padding:12px 14px;position:relative;}
            .chart-wrap h3{margin:0 0 8px;font-size:11px;color:#4f99a8;font-weight:500;letter-spacing:2px;}
            .chart-wrap canvas{width:100%;height:200px;display:block;}
            .chart-tip{position:absolute;pointer-events:none;background:rgba(7,10,15,.95);border:1px solid rgba(0,229,255,.4);
              padding:6px 10px;font-size:11px;display:none;z-index:5;white-space:nowrap;
              font-family:Consolas,"Courier New",monospace;box-shadow:0 0 12px rgba(0,229,255,.15);}
            .legend{display:flex;gap:14px;font-size:11px;color:#5f7a85;margin-top:6px;font-family:Consolas,"Courier New",monospace;}
            .legend span::before{content:"";display:inline-block;width:8px;height:8px;background:var(--c);margin-right:5px;
              vertical-align:middle;box-shadow:0 0 6px var(--c);}

            /* ---------- scrollbars ---------- */
            ::-webkit-scrollbar{width:8px;height:8px;}
            ::-webkit-scrollbar-track{background:#070a0f;}
            ::-webkit-scrollbar-thumb{background:rgba(0,229,255,.2);border:1px solid rgba(0,229,255,.1);}
            ::-webkit-scrollbar-thumb:hover{background:rgba(0,229,255,.4);}

            /* ---------- boot terminal ---------- */
            #boot{position:fixed;inset:0;background:#05070b;z-index:200;display:flex;align-items:center;justify-content:center;
              transition:opacity .35s ease;}
            #boot.done{opacity:0;pointer-events:none;}
            .boot-box{width:min(600px,90vw);border:1px solid rgba(0,229,255,.35);background:rgba(7,10,15,.95);
              padding:22px 26px;font-family:Consolas,"Courier New",monospace;font-size:13px;line-height:2;color:#8fd8e8;
              box-shadow:0 0 40px rgba(0,229,255,.12),inset 0 0 60px rgba(0,229,255,.03);position:relative;}
            .boot-box::before{content:"";position:absolute;top:-1px;left:-1px;width:12px;height:12px;
              border-top:1px solid #00e5ff;border-left:1px solid #00e5ff;}
            .boot-box::after{content:"";position:absolute;bottom:-1px;right:-1px;width:12px;height:12px;
              border-bottom:1px solid #00e5ff;border-right:1px solid #00e5ff;}
            .boot-head{font-size:10px;color:#3d5560;letter-spacing:3px;margin-bottom:10px;}
            .bline{white-space:pre-wrap;word-break:break-all;}
            .bline:first-child{color:#00e5ff;text-shadow:0 0 8px rgba(0,229,255,.5);}
            .bcursor{animation:blink .9s steps(1) infinite;}
            </style>
            </head>
            <body>
            <div id="boot">
              <div class="boot-box">
                <div class="boot-head">MIAONET // TERMINAL LINK</div>
                <div id="boot-text"></div>
              </div>
            </div>
            <header>
              <div class="brand">MIAONET<span class="brand-sub">管理后台</span></div>
              <div class="live" id="live"><span class="bcursor">█</span><span id="live-text">实时</span></div>
              <div id="conn-term"></div>
              <div class="spacer"></div>
              <span class="user">{{{HtmlEncode(session.NickName)}}}（{{{HtmlEncode(session.UserName)}}}）</span>
              <a class="logout" href="/admin/logout">退出登录</a>
            </header>
            <main>
              <div class="cards">
                <div class="card"><div class="label">在线玩家</div><div class="value" id="d-players">0</div></div>
                <div class="card"><div class="label">频道数</div><div class="value" id="d-channels">0</div></div>
                <div class="card"><div class="label">累计会话</div><div class="value" id="d-sessions">0</div></div>
                <div class="card"><div class="label">累计消息</div><div class="value" id="d-chat">0</div></div>
                <div class="card"><div class="label">运行时间</div><div class="value" id="d-uptime" style="font-size:17px;">-</div></div>
                <div class="card"><div class="label">托管内存</div><div class="value" id="d-mem" style="font-size:17px;">-</div></div>
              </div>
              <section class="panel sec-players">
                <h2 class="sec-title">在线玩家</h2>
                <div class="bar">
                  <input id="announce-input" placeholder="> 广播公告内容..." maxlength="200">
                  <button class="act" id="announce-send">发送公告</button>
                </div>
                <div class="table-wrap">
                  <table>
                    <thead><tr><th>连接 ID</th><th>名称</th><th>AuthID</th><th>频道</th><th>位置</th><th>操作</th></tr></thead>
                    <tbody id="players-body"></tbody>
                  </table>
                </div>
                <h2 class="sec-title sub">频道</h2>
                <div class="table-wrap" style="max-height:150px;">
                  <table><thead><tr><th>ID</th><th>名称</th><th>玩家数</th></tr></thead><tbody id="d-channels-body"></tbody></table>
                </div>
              </section>
              <section class="panel sec-chat">
                <h2 class="sec-title">实时聊天</h2>
                <div class="stream-wrap">
                  <div class="stream" id="chat-stream"><div class="empty">暂无聊天消息</div></div>
                  <button class="back-bottom" id="chat-bottom">回到底部</button>
                </div>
              </section>
              <section class="panel sec-logs">
                <h2 class="sec-title">实时日志</h2>
                <div class="filters" id="log-filters">
                  <button data-lv="0" class="active">全部</button>
                  <button data-lv="2">信息+</button>
                  <button data-lv="3">警告+</button>
                  <button data-lv="4">错误</button>
                </div>
                <div class="stream-wrap">
                  <div class="stream" id="log-stream"></div>
                  <button class="back-bottom" id="log-bottom">回到底部</button>
                </div>
              </section>
              <section class="panel sec-metrics">
                <h2 class="sec-title">指标图表</h2>
                <div class="charts-grid">
                  <div class="chart-wrap"><h3>在线玩家</h3><canvas id="chart-players"></canvas><div class="chart-tip"></div><div class="legend"><span style="--c:#00e5ff">在线玩家</span><span style="--c:#00ff9c">频道数</span></div></div>
                  <div class="chart-wrap"><h3>聊天消息（条 / 5 秒）</h3><canvas id="chart-chat"></canvas><div class="chart-tip"></div><div class="legend"><span style="--c:#00ff9c">消息数</span></div></div>
                  <div class="chart-wrap"><h3>TCP 包速率（包 / 秒）</h3><canvas id="chart-packets"></canvas><div class="chart-tip"></div><div class="legend"><span style="--c:#00e5ff">上行</span><span style="--c:#ffb300">下行</span></div></div>
                  <div class="chart-wrap"><h3>TCP 字节速率（KB / 秒）</h3><canvas id="chart-bytes"></canvas><div class="chart-tip"></div><div class="legend"><span style="--c:#00e5ff">上行</span><span style="--c:#ffb300">下行</span></div></div>
                </div>
              </section>
            </main>
            <div id="toasts"></div>
            <script>
            "use strict";
            const $ = s => document.querySelector(s);

            function toast(msg, ok) {
              const t = document.createElement("div");
              t.className = "toast " + (ok ? "ok" : "err");
              t.textContent = (ok ? "[OK] " : "[ERR] ") + msg;
              $("#toasts").appendChild(t);
              setTimeout(() => { t.classList.add("out"); setTimeout(() => t.remove(), 280); }, 3200);
            }

            async function api(path, opts) {
              const r = await fetch(path, opts);
              if (r.status === 401) { location.href = "/admin/login"; throw new Error("401"); }
              return r;
            }
            async function apiJson(path, opts) { return (await api(path, opts)).json(); }
            function postJson(path, body) {
              return apiJson(path, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
            }

            // ---- terminal: mini connection console + boot sequence ----
            function termPrint(text) {
              const t = $("#conn-term");
              const div = document.createElement("div");
              div.textContent = text;
              t.appendChild(div);
              while (t.children.length > 3) t.removeChild(t.firstChild);
              return div;
            }
            const connFail = {}, retryLines = {};
            function setLive(ok) {
              $("#live").classList.toggle("down", !ok);
              $("#live-text").textContent = ok ? "实时" : "重连中";
            }
            function pollOk(tag, label) {
              if (connFail[tag] > 0) {
                connFail[tag] = 0; retryLines[tag] = null;
                termPrint("> " + label + " 连接已恢复 [OK]");
              }
              if (Object.keys(connFail).every(k => !connFail[k])) setLive(true);
            }
            function pollFail(tag, label) {
              const c = (connFail[tag] = (connFail[tag] || 0) + 1);
              if (c === 1) retryLines[tag] = termPrint("> " + label + " 连接中断, 正在重试... (1)");
              else if (retryLines[tag]) retryLines[tag].textContent = "> " + label + " 连接中断, 正在重试... (" + c + ")";
              setLive(false);
            }
            (async function boot() {
              const bootEl = $("#boot"), bootText = $("#boot-text");
              let skip = false;
              bootEl.addEventListener("click", () => skip = true);
              const sleep = ms => new Promise(r => setTimeout(r, skip ? 0 : ms));
              const lines = [
                "> MIAONET ADMIN CONSOLE v2.0",
                "> 正在建立连接...",
                "> 握手成功 [OK]",
                "> 拉取服务器状态... [OK]",
                "> 数据流已同步 — 实时监控中"
              ];
              for (const line of lines) {
                const div = document.createElement("div");
                div.className = "bline";
                const cur = document.createElement("span");
                cur.className = "bcursor"; cur.textContent = "█";
                div.appendChild(cur); bootText.appendChild(div);
                for (const ch of line) { cur.before(document.createTextNode(ch)); await sleep(9); }
                cur.remove();
                await sleep(70);
              }
              for (const line of lines.slice(-3)) termPrint(line);
              await sleep(150);
              bootEl.classList.add("done");
              setTimeout(() => bootEl.remove(), 420);
            })();

            // ---- animated numbers ----
            const animated = new WeakMap();
            function setNumber(el, to, fmt) {
              fmt = fmt || (v => Math.round(v).toLocaleString());
              const from = animated.has(el) ? animated.get(el) : 0;
              animated.set(el, to);
              if (from === to) { el.textContent = fmt(to); return; }
              const start = performance.now(), dur = 500;
              function step(now) {
                const k = Math.min(1, (now - start) / dur);
                const v = from + (to - from) * (1 - Math.pow(1 - k, 3));
                el.textContent = fmt(k >= 1 ? to : v);
                if (k < 1 && animated.get(el) === to) requestAnimationFrame(step);
              }
              requestAnimationFrame(step);
            }
            function fmtBytes(b) {
              if (b < 1024) return Math.round(b) + " B";
              if (b < 1048576) return (b / 1024).toFixed(1) + " KB";
              if (b < 1073741824) return (b / 1048576).toFixed(1) + " MB";
              return (b / 1073741824).toFixed(2) + " GB";
            }
            function fmtUptime(s) {
              const h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), sec = Math.floor(s % 60);
              return h > 0 ? h + " 时 " + m + " 分" : (m > 0 ? m + " 分 " + sec + " 秒" : sec + " 秒");
            }
            function fmtTime(iso) { const d = new Date(iso); return d.toLocaleTimeString("zh-CN", { hour12: false }); }
            function esc(s) { const d = document.createElement("div"); d.textContent = s; return d.innerHTML; }

            // ---- stream helper (chat / logs) ----
            function makeStream(streamEl, bottomBtn) {
              let pinned = true;
              streamEl.addEventListener("scroll", () => {
                pinned = streamEl.scrollTop + streamEl.clientHeight >= streamEl.scrollHeight - 30;
                bottomBtn.style.display = pinned ? "none" : "block";
              });
              bottomBtn.addEventListener("click", () => {
                streamEl.scrollTop = streamEl.scrollHeight;
                pinned = true; bottomBtn.style.display = "none";
              });
              return {
                append(node) {
                  streamEl.appendChild(node);
                  while (streamEl.children.length > 1200) streamEl.removeChild(streamEl.firstChild);
                  if (pinned) streamEl.scrollTop = streamEl.scrollHeight;
                },
                clear() { streamEl.innerHTML = ""; },
                scrollToBottom() { streamEl.scrollTop = streamEl.scrollHeight; }
              };
            }
            const chatStream = makeStream($("#chat-stream"), $("#chat-bottom"));
            const logStream = makeStream($("#log-stream"), $("#log-bottom"));

            // ---- players ----
            const knownPlayers = new Set();
            async function refreshPlayers() {
              try {
                const data = await apiJson("/admin/api/players");
                pollOk("players", "玩家数据");
                const body = $("#players-body");
                body.innerHTML = "";
                if (data.players.length === 0) {
                  body.innerHTML = '<tr><td colspan="6" class="empty">当前没有在线玩家</td></tr>';
                }
                const seen = new Set();
                for (const p of data.players) {
                  seen.add(p.connectionID);
                  const tr = document.createElement("tr");
                  if (!knownPlayers.has(p.connectionID)) tr.className = "row-new";
                  tr.innerHTML = "<td>" + p.connectionID + "</td><td>" + esc(p.name) + "</td><td>" + p.authID +
                    "</td><td>" + esc(p.channel || "-") + "</td><td>" + esc(p.location) + "</td>";
                  const td = document.createElement("td");
                  const btn = document.createElement("button");
                  btn.className = "kick"; btn.textContent = "踢出";
                  btn.addEventListener("click", () => kickPlayer(p));
                  td.appendChild(btn); tr.appendChild(td);
                  body.appendChild(tr);
                }
                knownPlayers.clear(); seen.forEach(id => knownPlayers.add(id));
                // channels table
                const cb = $("#d-channels-body");
                cb.innerHTML = "";
                if (!data.channels || data.channels.length === 0) {
                  cb.innerHTML = '<tr><td colspan="3" class="empty">当前没有频道</td></tr>';
                } else {
                  for (const c of data.channels) {
                    const tr = document.createElement("tr");
                    tr.innerHTML = "<td>" + c.id + "</td><td>" + esc(c.name) + "</td><td>" + c.players + "</td>";
                    cb.appendChild(tr);
                  }
                }
              } catch (e) { pollFail("players", "玩家数据"); }
            }
            async function kickPlayer(p) {
              const reason = prompt("踢出 " + p.name + "（连接 " + p.connectionID + "）的原因（可留空）：", "");
              if (reason === null) return;
              if (!confirm("确定要踢出 " + p.name + " 吗？")) return;
              try {
                const r = await postJson("/admin/api/kick", { connectionID: p.connectionID, authID: p.authID, reason });
                if (r.ok) { toast("已踢出 " + p.name, true); refreshPlayers(); }
                else toast(r.error || "踢出失败", false);
              } catch (e) { toast("请求失败", false); }
            }
            $("#announce-send").addEventListener("click", sendAnnounce);
            $("#announce-input").addEventListener("keydown", e => { if (e.key === "Enter") sendAnnounce(); });
            async function sendAnnounce() {
              const input = $("#announce-input");
              const message = input.value.trim();
              if (!message) { toast("公告内容不能为空", false); return; }
              try {
                const r = await postJson("/admin/api/announce", { message });
                if (r.ok) { toast("公告已发送", true); input.value = ""; }
                else toast(r.error || "发送失败", false);
              } catch (e) { toast("请求失败", false); }
            }

            // ---- chat ----
            let chatAfter = -1, chatFirst = true;
            const chatTypes = { global: ["全局", "badge-global"], channel: ["频道", "badge-channel"], map: ["地图", "badge-map"], server: ["服务器", "badge-server"] };
            async function pollChat() {
              try {
                const data = await apiJson("/admin/api/chat?after=" + chatAfter);
                pollOk("chat", "聊天流");
                if (chatFirst) { chatStream.clear(); chatFirst = false; }
                for (const e of data.entries) {
                  const t = chatTypes[e.type] || chatTypes.global;
                  const div = document.createElement("div");
                  div.className = "line";
                  div.innerHTML = '<span class="time">' + fmtTime(e.time) + '</span>' +
                    '<span class="badge ' + t[1] + '">' + t[0] + (e.channel ? "·" + esc(e.channel) : "") + "</span>" +
                    '<span class="chat-name">' + esc(e.player) + "</span>" + esc(e.content);
                  chatStream.append(div);
                }
                chatAfter = data.latest;
              } catch (e) { pollFail("chat", "聊天流"); }
            }

            // ---- logs ----
            let logAfter = -1, logMinLevel = 0;
            const logEntries = [];
            const lvOrder = { Trace: 0, Debug: 1, Information: 2, Warning: 3, Error: 4, Critical: 5 };
            $("#log-filters").querySelectorAll("button").forEach(b => {
              b.addEventListener("click", () => {
                $("#log-filters").querySelectorAll("button").forEach(x => x.classList.toggle("active", x === b));
                logMinLevel = parseInt(b.dataset.lv);
                renderLogs();
              });
            });
            function logNode(e) {
              const div = document.createElement("div");
              div.className = "line lv-" + e.level;
              const cat = (e.category || "").split(".").pop();
              div.innerHTML = '<span class="time">' + fmtTime(e.time) + "</span>" +
                '<span class="log-cat">[' + e.level + "] " + esc(cat) + "</span>" + esc(e.message) +
                (e.exception ? '<div class="log-exc">' + esc(e.exception) + "</div>" : "");
              return div;
            }
            function renderLogs() {
              logStream.clear();
              for (const e of logEntries)
                if ((lvOrder[e.level] ?? 2) >= logMinLevel) logStream.append(logNode(e));
              logStream.scrollToBottom();
            }
            async function pollLogs() {
              try {
                const data = await apiJson("/admin/api/logs?after=" + logAfter + "&limit=200");
                pollOk("logs", "日志流");
                for (const e of data.entries) {
                  logEntries.push(e);
                  if ((lvOrder[e.level] ?? 2) >= logMinLevel) logStream.append(logNode(e));
                }
                if (logEntries.length > 1000) logEntries.splice(0, logEntries.length - 1000);
                logAfter = data.latest;
              } catch (e) { pollFail("logs", "日志流"); }
            }

            // ---- metrics & charts ----
            async function pollMetrics() {
              try {
                const data = await apiJson("/admin/api/metrics");
                pollOk("metrics", "指标流");
                const c = data.current;
                setNumber($("#d-players"), c.onlinePlayers);
                setNumber($("#d-channels"), c.channels);
                setNumber($("#d-sessions"), c.sessions);
                setNumber($("#d-chat"), c.chatMessagesTotal);
                $("#d-uptime").textContent = fmtUptime(c.uptimeSeconds);
                $("#d-mem").textContent = fmtBytes(c.gcTotalMemory);
                const s = data.series;
                const labels = s.time.map(t => new Date(t * 1000).toLocaleTimeString("zh-CN", { hour12: false }));
                drawChart($("#chart-players"), labels, [
                  { name: "在线玩家", color: "#00e5ff", data: s.onlinePlayers },
                  { name: "频道数", color: "#00ff9c", data: s.channels }
                ]);
                drawChart($("#chart-chat"), labels, [
                  { name: "消息数", color: "#00ff9c", data: s.chatMessagesPerInterval }
                ]);
                drawChart($("#chart-packets"), labels, [
                  { name: "上行", color: "#00e5ff", data: s.upPacketsPerSecond },
                  { name: "下行", color: "#ffb300", data: s.downPacketsPerSecond }
                ]);
                drawChart($("#chart-bytes"), labels, [
                  { name: "上行", color: "#00e5ff", data: s.upBytesPerSecond.map(v => v / 1024) },
                  { name: "下行", color: "#ffb300", data: s.downBytesPerSecond.map(v => v / 1024) }
                ]);
              } catch (e) { pollFail("metrics", "指标流"); }
            }

            const chartState = new WeakMap();
            function drawChart(canvas, labels, series) {
              const dpr = window.devicePixelRatio || 1;
              const W = canvas.clientWidth, H = canvas.clientHeight;
              if (W === 0) return;
              if (canvas.width !== W * dpr) { canvas.width = W * dpr; canvas.height = H * dpr; }
              const ctx = canvas.getContext("2d");
              ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
              ctx.clearRect(0, 0, W, H);
              const padL = 44, padR = 10, padT = 8, padB = 22;
              const iw = W - padL - padR, ih = H - padT - padB;
              const n = labels.length;
              let max = 1;
              for (const s of series) for (const v of s.data) if (v > max) max = v;
              max *= 1.1;
              const xOf = i => padL + (n <= 1 ? iw / 2 : i / (n - 1) * iw);
              const yOf = v => padT + ih - (v / max) * ih;
              // grid + y labels
              ctx.font = "10px Consolas, monospace"; ctx.fillStyle = "#3d5560";
              ctx.strokeStyle = "rgba(0,229,255,.1)"; ctx.lineWidth = 1;
              for (let g = 0; g <= 4; g++) {
                const y = padT + ih * g / 4, val = max * (1 - g / 4);
                ctx.beginPath(); ctx.moveTo(padL, y); ctx.lineTo(W - padR, y); ctx.stroke();
                ctx.textAlign = "right";
                ctx.fillText(val >= 100 ? Math.round(val) : val.toFixed(1), padL - 6, y + 3);
              }
              // x labels
              ctx.textAlign = "center";
              const ticks = Math.min(6, n);
              for (let t = 0; t < ticks; t++) {
                const i = Math.round(t * (n - 1) / Math.max(1, ticks - 1));
                if (labels[i]) ctx.fillText(labels[i], xOf(i), H - 6);
              }
              // axis
              ctx.strokeStyle = "rgba(0,229,255,.3)";
              ctx.beginPath(); ctx.moveTo(padL, padT); ctx.lineTo(padL, padT + ih); ctx.lineTo(W - padR, padT + ih); ctx.stroke();
              // series
              for (const s of series) {
                ctx.strokeStyle = s.color; ctx.lineWidth = 1.5;
                ctx.shadowColor = s.color; ctx.shadowBlur = 6;
                ctx.beginPath();
                for (let i = 0; i < n; i++) {
                  const x = xOf(i), y = yOf(s.data[i] || 0);
                  i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
                }
                ctx.stroke();
                ctx.shadowBlur = 0;
              }
              chartState.set(canvas, { labels, series, padL, padR, padT, padB, xOf, yOf, n, max });
            }
            document.querySelectorAll(".chart-wrap canvas").forEach(canvas => {
              const tip = canvas.parentElement.querySelector(".chart-tip");
              canvas.addEventListener("mousemove", ev => {
                const st = chartState.get(canvas);
                if (!st || st.n === 0) return;
                const rect = canvas.getBoundingClientRect();
                const x = ev.clientX - rect.left;
                let best = 0, bd = Infinity;
                for (let i = 0; i < st.n; i++) { const d = Math.abs(st.xOf(i) - x); if (d < bd) { bd = d; best = i; } }
                let html = "<b>" + st.labels[best] + "</b>";
                for (const s of st.series)
                  html += '<br><span style="color:' + s.color + '">' + s.name + ": " +
                    (Math.round((s.data[best] || 0) * 100) / 100) + "</span>";
                tip.innerHTML = html;
                tip.style.display = "block";
                tip.style.left = Math.min(rect.width - 130, Math.max(0, x + 12)) + "px";
                tip.style.top = "30px";
              });
              canvas.addEventListener("mouseleave", () => { tip.style.display = "none"; });
            });

            // ---- polling loops ----
            refreshPlayers(); pollChat(); pollLogs(); pollMetrics();
            setInterval(refreshPlayers, 3000);
            setInterval(pollChat, 2000);
            setInterval(pollLogs, 2000);
            setInterval(pollMetrics, 5000);
            </script>
            </body>
            </html>
            """;
        byte[] data = Encoding.UTF8.GetBytes(html);
        context.Response.ContentLength64 = data.Length;
        await context.Response.OutputStream.WriteAsync(data);
    }
}
