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
            body{background:#12141a;color:#e6e6e6;font-family:"Segoe UI","Microsoft YaHei",sans-serif;margin:0;}
            a{color:#7ab8ff;text-decoration:none;}
            header{display:flex;align-items:center;gap:14px;padding:12px 24px;background:#171a22;border-bottom:1px solid #2a2e3a;position:sticky;top:0;z-index:10;}
            .brand{font-size:18px;font-weight:600;}
            .live{display:flex;align-items:center;gap:6px;font-size:12px;color:#4caf7d;}
            .live .dot{width:8px;height:8px;border-radius:50%;background:#4caf7d;animation:pulse 1.6s infinite;}
            @keyframes pulse{0%{box-shadow:0 0 0 0 rgba(76,175,125,.6);}70%{box-shadow:0 0 0 8px rgba(76,175,125,0);}100%{box-shadow:0 0 0 0 rgba(76,175,125,0);}}
            .spacer{flex:1;}
            .user{font-size:13px;color:#9aa0ab;}
            .logout{font-size:13px;padding:5px 12px;border:1px solid #2a2e3a;border-radius:4px;transition:background .15s;}
            .logout:hover{background:#1c1f29;}
            nav.tabs{display:flex;gap:4px;padding:10px 24px 0;background:#171a22;border-bottom:1px solid #2a2e3a;position:sticky;top:53px;z-index:10;}
            nav.tabs button{background:none;border:none;color:#9aa0ab;font-size:14px;padding:9px 16px;cursor:pointer;border-bottom:2px solid transparent;transition:color .15s,border-color .15s;font-family:inherit;}
            nav.tabs button:hover{color:#e6e6e6;}
            nav.tabs button.active{color:#7ab8ff;border-bottom-color:#7ab8ff;}
            main{padding:20px 24px 60px;max-width:1200px;margin:0 auto;}
            .panel{display:none;}
            .panel.active{display:block;animation:panelIn .25s ease;}
            @keyframes panelIn{from{opacity:0;transform:translateY(8px);}to{opacity:1;transform:none;}}
            h2{font-size:16px;margin:24px 0 10px;color:#c9cdd4;}
            .cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(170px,1fr));gap:12px;}
            .card{background:#1c1f29;border:1px solid #2a2e3a;border-radius:8px;padding:14px 16px;transition:transform .15s,box-shadow .15s;}
            .card:hover{transform:translateY(-2px);box-shadow:0 6px 18px rgba(0,0,0,.35);}
            .card .label{font-size:12px;color:#9aa0ab;margin-bottom:6px;}
            .card .value{font-size:24px;font-weight:600;font-variant-numeric:tabular-nums;}
            table{border-collapse:collapse;width:100%;}
            th,td{border-bottom:1px solid #2a2e3a;padding:7px 10px;text-align:left;font-size:13px;}
            th{color:#9aa0ab;font-weight:500;background:#171a22;}
            tbody tr{transition:background .15s;}
            tbody tr:hover{background:#1a1d26;}
            tr.row-new{animation:rowIn .4s ease;}
            @keyframes rowIn{from{opacity:0;transform:translateX(-10px);}to{opacity:1;transform:none;}}
            input{background:#12141a;border:1px solid #2a2e3a;color:#e6e6e6;padding:8px 10px;border-radius:6px;font-size:13px;font-family:inherit;transition:border-color .15s;}
            input:focus{outline:none;border-color:#2f6fed;}
            button.act{background:#2f6fed;color:#fff;border:none;padding:8px 16px;border-radius:6px;cursor:pointer;font-size:13px;transition:background .15s,transform .05s;font-family:inherit;}
            button.act:hover{background:#3d7dff;}
            button.act:active{transform:scale(.96);}
            button.danger{background:#a4352c;}
            button.danger:hover{background:#c24036;}
            button.kick{background:none;border:1px solid #a4352c;color:#f08080;padding:4px 10px;border-radius:4px;cursor:pointer;font-size:12px;transition:all .15s;font-family:inherit;}
            button.kick:hover{background:#a4352c;color:#fff;}
            button.kick:active{transform:scale(.94);}
            .bar{display:flex;gap:10px;margin-bottom:14px;}
            .bar input{flex:1;max-width:480px;}
            .stream{background:#14161d;border:1px solid #2a2e3a;border-radius:8px;height:520px;overflow-y:auto;padding:10px 14px;font-size:13px;line-height:1.7;}
            .stream .line{animation:lineIn .3s ease;word-break:break-all;}
            @keyframes lineIn{from{opacity:0;transform:translateY(6px);}to{opacity:1;transform:none;}}
            .stream .time{color:#6b7280;font-variant-numeric:tabular-nums;margin-right:8px;font-size:12px;}
            .badge{display:inline-block;padding:1px 7px;border-radius:8px;font-size:11px;margin-right:8px;}
            .badge-global{background:rgba(88,166,255,.15);color:#58a6ff;}
            .badge-channel{background:rgba(188,140,255,.15);color:#bc8cff;}
            .badge-map{background:rgba(76,175,125,.15);color:#4caf7d;}
            .badge-server{background:rgba(227,179,65,.15);color:#e3b341;}
            .chat-name{color:#c9cdd4;margin-right:6px;}
            .lv-Trace,.lv-Debug{color:#8a8f98;}
            .lv-Information{color:#58a6ff;}
            .lv-Warning{color:#e3b341;}
            .lv-Error,.lv-Critical{color:#f85149;}
            .log-cat{color:#6b7280;margin-right:8px;font-size:12px;}
            .log-exc{color:#f85149;white-space:pre-wrap;font-size:12px;opacity:.85;}
            .filters{display:flex;gap:8px;margin-bottom:10px;}
            .filters button{background:#1c1f29;border:1px solid #2a2e3a;color:#9aa0ab;padding:5px 14px;border-radius:14px;cursor:pointer;font-size:12px;transition:all .15s;font-family:inherit;}
            .filters button.active{background:#2f6fed;border-color:#2f6fed;color:#fff;}
            .back-bottom{position:fixed;right:32px;bottom:32px;background:#2f6fed;color:#fff;border:none;padding:9px 18px;border-radius:20px;cursor:pointer;font-size:13px;box-shadow:0 4px 14px rgba(0,0,0,.4);display:none;animation:panelIn .2s ease;font-family:inherit;z-index:20;}
            #toasts{position:fixed;top:70px;right:24px;z-index:100;display:flex;flex-direction:column;gap:8px;}
            .toast{padding:10px 18px;border-radius:8px;font-size:13px;color:#fff;box-shadow:0 6px 18px rgba(0,0,0,.4);animation:toastIn .3s ease;max-width:360px;}
            .toast.ok{background:#1d5c33;border:1px solid #2f8a4d;}
            .toast.err{background:#6b2320;border:1px solid #a4352c;}
            .toast.out{animation:toastOut .3s ease forwards;}
            @keyframes toastIn{from{opacity:0;transform:translateX(40px);}to{opacity:1;transform:none;}}
            @keyframes toastOut{to{opacity:0;transform:translateX(40px);}}
            .chart-wrap{background:#1c1f29;border:1px solid #2a2e3a;border-radius:8px;padding:14px 16px;margin-bottom:16px;position:relative;}
            .chart-wrap h3{margin:0 0 8px;font-size:13px;color:#9aa0ab;font-weight:500;}
            .chart-wrap canvas{width:100%;height:220px;display:block;}
            .chart-tip{position:absolute;pointer-events:none;background:#0d0f14;border:1px solid #2a2e3a;border-radius:6px;padding:6px 10px;font-size:12px;display:none;z-index:5;white-space:nowrap;}
            .legend{display:flex;gap:14px;font-size:12px;color:#9aa0ab;margin-top:6px;}
            .legend span::before{content:"";display:inline-block;width:10px;height:3px;border-radius:2px;background:var(--c);margin-right:5px;vertical-align:middle;}
            .empty{color:#6b7280;padding:18px;text-align:center;}
            </style>
            </head>
            <body>
            <header>
              <div class="brand">MiaoNet 管理后台</div>
              <div class="live"><span class="dot"></span>实时</div>
              <div class="spacer"></div>
              <span class="user">{{{HtmlEncode(session.NickName)}}}（{{{HtmlEncode(session.UserName)}}}）</span>
              <a class="logout" href="/admin/logout">退出登录</a>
            </header>
            <nav class="tabs">
              <button data-tab="dashboard" class="active">仪表盘</button>
              <button data-tab="players">玩家</button>
              <button data-tab="chat">聊天</button>
              <button data-tab="logs">日志</button>
              <button data-tab="metrics">指标</button>
            </nav>
            <main>
              <section id="panel-dashboard" class="panel active">
                <div class="cards">
                  <div class="card"><div class="label">在线玩家</div><div class="value" id="d-players">0</div></div>
                  <div class="card"><div class="label">频道数</div><div class="value" id="d-channels">0</div></div>
                  <div class="card"><div class="label">累计会话</div><div class="value" id="d-sessions">0</div></div>
                  <div class="card"><div class="label">累计消息</div><div class="value" id="d-chat">0</div></div>
                  <div class="card"><div class="label">运行时间</div><div class="value" id="d-uptime" style="font-size:18px;">-</div></div>
                  <div class="card"><div class="label">托管内存</div><div class="value" id="d-mem" style="font-size:18px;">-</div></div>
                </div>
                <h2>频道</h2>
                <table><thead><tr><th>ID</th><th>名称</th><th>玩家数</th></tr></thead><tbody id="d-channels-body"></tbody></table>
              </section>
              <section id="panel-players" class="panel">
                <div class="bar">
                  <input id="announce-input" placeholder="广播公告内容..." maxlength="200">
                  <button class="act" id="announce-send">发送公告</button>
                </div>
                <table>
                  <thead><tr><th>连接 ID</th><th>名称</th><th>AuthID</th><th>频道</th><th>位置</th><th>操作</th></tr></thead>
                  <tbody id="players-body"></tbody>
                </table>
              </section>
              <section id="panel-chat" class="panel">
                <div class="stream" id="chat-stream"><div class="empty">暂无聊天消息</div></div>
                <button class="back-bottom" id="chat-bottom">回到底部</button>
              </section>
              <section id="panel-logs" class="panel">
                <div class="filters" id="log-filters">
                  <button data-lv="0" class="active">全部</button>
                  <button data-lv="2">信息+</button>
                  <button data-lv="3">警告+</button>
                  <button data-lv="4">错误</button>
                </div>
                <div class="stream" id="log-stream"></div>
                <button class="back-bottom" id="log-bottom">回到底部</button>
              </section>
              <section id="panel-metrics" class="panel">
                <div class="chart-wrap"><h3>在线玩家</h3><canvas id="chart-players"></canvas><div class="chart-tip"></div><div class="legend"><span style="--c:#58a6ff">在线玩家</span><span style="--c:#bc8cff">频道数</span></div></div>
                <div class="chart-wrap"><h3>聊天消息（条 / 5 秒）</h3><canvas id="chart-chat"></canvas><div class="chart-tip"></div><div class="legend"><span style="--c:#4caf7d">消息数</span></div></div>
                <div class="chart-wrap"><h3>TCP 包速率（包 / 秒）</h3><canvas id="chart-packets"></canvas><div class="chart-tip"></div><div class="legend"><span style="--c:#58a6ff">上行</span><span style="--c:#e3b341">下行</span></div></div>
                <div class="chart-wrap"><h3>TCP 字节速率（KB / 秒）</h3><canvas id="chart-bytes"></canvas><div class="chart-tip"></div><div class="legend"><span style="--c:#58a6ff">上行</span><span style="--c:#e3b341">下行</span></div></div>
              </section>
            </main>
            <div id="toasts"></div>
            <script>
            "use strict";
            const $ = s => document.querySelector(s);

            function toast(msg, ok) {
              const t = document.createElement("div");
              t.className = "toast " + (ok ? "ok" : "err");
              t.textContent = msg;
              $("#toasts").appendChild(t);
              setTimeout(() => { t.classList.add("out"); setTimeout(() => t.remove(), 320); }, 3200);
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

            // ---- tabs ----
            document.querySelectorAll("nav.tabs button").forEach(b => {
              b.addEventListener("click", () => {
                document.querySelectorAll("nav.tabs button").forEach(x => x.classList.toggle("active", x === b));
                document.querySelectorAll(".panel").forEach(p => p.classList.toggle("active", p.id === "panel-" + b.dataset.tab));
              });
            });

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
                // dashboard channels table
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
              } catch (e) { /* 401 handled in api */ }
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
              } catch (e) { }
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
                for (const e of data.entries) {
                  logEntries.push(e);
                  if ((lvOrder[e.level] ?? 2) >= logMinLevel) logStream.append(logNode(e));
                }
                if (logEntries.length > 1000) logEntries.splice(0, logEntries.length - 1000);
                logAfter = data.latest;
              } catch (e) { }
            }

            // ---- metrics & charts ----
            async function pollMetrics() {
              try {
                const data = await apiJson("/admin/api/metrics");
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
                  { name: "在线玩家", color: "#58a6ff", data: s.onlinePlayers },
                  { name: "频道数", color: "#bc8cff", data: s.channels }
                ]);
                drawChart($("#chart-chat"), labels, [
                  { name: "消息数", color: "#4caf7d", data: s.chatMessagesPerInterval }
                ]);
                drawChart($("#chart-packets"), labels, [
                  { name: "上行", color: "#58a6ff", data: s.upPacketsPerSecond },
                  { name: "下行", color: "#e3b341", data: s.downPacketsPerSecond }
                ]);
                drawChart($("#chart-bytes"), labels, [
                  { name: "上行", color: "#58a6ff", data: s.upBytesPerSecond.map(v => v / 1024) },
                  { name: "下行", color: "#e3b341", data: s.downBytesPerSecond.map(v => v / 1024) }
                ]);
              } catch (e) { }
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
              ctx.font = "10px sans-serif"; ctx.fillStyle = "#6b7280"; ctx.strokeStyle = "#23262f"; ctx.lineWidth = 1;
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
              // series
              for (const s of series) {
                ctx.strokeStyle = s.color; ctx.lineWidth = 1.6; ctx.beginPath();
                for (let i = 0; i < n; i++) {
                  const x = xOf(i), y = yOf(s.data[i] || 0);
                  i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
                }
                ctx.stroke();
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
