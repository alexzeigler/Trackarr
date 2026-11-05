namespace Trackarr.MockServer;

public static class DashboardPage
{
    public static readonly string Html = """
                                         <!DOCTYPE html>
                                         <html lang="en" data-bs-theme="dark">
                                         <head>
                                         <meta charset="utf-8">
                                         <meta name="viewport" content="width=device-width, initial-scale=1.0">
                                         <title>MockServer</title>
                                         <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@tabler/core@1.4.0/dist/css/tabler.min.css">
                                         <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@tabler/icons-webfont@3.34.1/dist/tabler-icons.min.css">
                                         </head>
                                         <body>
                                         <div class="page">
                                           <div class="page-wrapper">

                                             <div class="page-header d-print-none">
                                               <div class="container-xl">
                                                 <div class="row g-2 align-items-center">
                                                   <div class="col">
                                                     <h2 class="page-title">MockServer</h2>
                                                     <div class="text-secondary" id="ports">Connecting...</div>
                                                   </div>
                                                   <div class="col-auto ms-auto">
                                                     <span class="badge bg-secondary-lt" id="live">offline</span>
                                                   </div>
                                                 </div>
                                               </div>
                                             </div>

                                             <div class="page-body">
                                               <div class="container-xl">

                                                 <div class="row row-cards mb-3">
                                                   <div class="col-lg-6">
                                                     <div class="card h-100">
                                                       <div class="card-header"><h3 class="card-title">API Key</h3></div>
                                                       <div class="card-body">
                                                         <p class="text-secondary mb-2">
                                                           Trackarr must send this key (X-Api-Key header or <code>apikey</code> query param).
                                                           Requests without it get <span class="badge bg-red-lt">401</span>.
                                                         </p>
                                                         <div class="input-group mb-2">
                                                           <input type="password" class="form-control font-monospace" id="apiKey" autocomplete="off" spellcheck="false">
                                                           <button class="btn" id="toggleKey" type="button" title="Show/hide"><i class="ti ti-eye"></i></button>
                                                           <button class="btn" id="copyKey" type="button" title="Copy"><i class="ti ti-copy"></i></button>
                                                         </div>
                                                         <div class="d-flex gap-2 align-items-center">
                                                           <button class="btn btn-primary btn-sm" id="saveKey">Save</button>
                                                           <button class="btn btn-outline-secondary btn-sm" id="randomKey">Generate random</button>
                                                           <span class="text-secondary small ms-2" id="keyMsg"></span>
                                                         </div>
                                                       </div>
                                                     </div>
                                                   </div>
                                                   <div class="col-lg-6">
                                                     <div class="card h-100">
                                                       <div class="card-header"><h3 class="card-title">Live Sources</h3></div>
                                                       <div class="card-body">
                                                         <p class="text-secondary mb-2">
                                                           Every GET is forwarded to the live server. Searches, queue removals and other writes
                                                           are logged here and never forwarded. Saved to user secrets; saving restarts the server.
                                                         </p>
                                                         <div class="row g-2 mb-2">
                                                           <div class="col-12 fw-bold small" id="radarrLabel">Radarr</div>
                                                           <div class="col-md-7"><input class="form-control font-monospace src" id="radarrUrl" placeholder="http://radarr:7878" spellcheck="false"></div>
                                                           <div class="col-md-5"><input type="password" class="form-control font-monospace src" id="radarrApiKey" placeholder="API key" autocomplete="off" spellcheck="false"></div>
                                                           <div class="col-12 fw-bold small mt-2" id="sonarrLabel">Sonarr</div>
                                                           <div class="col-md-7"><input class="form-control font-monospace src" id="sonarrUrl" placeholder="http://sonarr:8989" spellcheck="false"></div>
                                                           <div class="col-md-5"><input type="password" class="form-control font-monospace src" id="sonarrApiKey" placeholder="API key" autocomplete="off" spellcheck="false"></div>
                                                         </div>
                                                         <div class="d-flex gap-2 align-items-center">
                                                           <button class="btn btn-primary btn-sm" id="saveSources">Save &amp; restart</button>
                                                           <button class="btn btn-outline-secondary btn-sm" id="toggleSrcKeys" type="button"><i class="ti ti-eye"></i></button>
                                                           <span class="text-secondary small ms-2" id="srcMsg"></span>
                                                         </div>
                                                         <div class="text-secondary small mt-2 text-truncate" id="secretsPath"></div>
                                                       </div>
                                                     </div>
                                                   </div>
                                                 </div>

                                                 <div class="row row-cards mb-3" id="cards"></div>

                                                 <div class="card mb-3">
                                                   <div class="card-header">
                                                     <h3 class="card-title">Queue Removal Attempts</h3>
                                                     <div class="card-actions">
                                                       <button class="btn btn-outline-secondary btn-sm" id="clearRemovals">Restore all</button>
                                                     </div>
                                                   </div>
                                                   <div class="card-body py-2 text-secondary small">
                                                     Trackarr tried to remove these from the queue. The live item was left alone and is hidden from
                                                     the queue Trackarr sees. Restore puts it back.
                                                   </div>
                                                   <div class="table-responsive">
                                                     <table class="table table-vcenter card-table table-hover">
                                                       <thead>
                                                         <tr>
                                                           <th>Last attempt</th>
                                                           <th>Instance</th>
                                                           <th>Queue item</th>
                                                           <th>State at removal</th>
                                                           <th>Options</th>
                                                           <th>Tries</th>
                                                           <th>Live queue</th>
                                                           <th></th>
                                                         </tr>
                                                       </thead>
                                                       <tbody id="removals"></tbody>
                                                     </table>
                                                   </div>
                                                 </div>

                                                 <div class="row row-cards mb-3">
                                                   <div class="col-lg-6">
                                                     <div class="card h-100">
                                                       <div class="card-header"><h3 class="card-title">Searches</h3></div>
                                                       <div class="table-responsive" style="max-height: 420px">
                                                         <table class="table table-vcenter card-table table-sm">
                                                           <thead><tr><th>Time</th><th>Instance</th><th>Command</th><th>Targets</th><th>Status</th><th>Outcome</th></tr></thead>
                                                           <tbody id="commands"></tbody>
                                                         </table>
                                                       </div>
                                                     </div>
                                                   </div>
                                                   <div class="col-lg-6">
                                                     <div class="card h-100">
                                                       <div class="card-header"><h3 class="card-title">Other Intercepted Writes</h3></div>
                                                       <div class="table-responsive" style="max-height: 420px">
                                                         <table class="table table-vcenter card-table table-sm table-hover">
                                                           <thead><tr><th>Time</th><th>Instance</th><th>Method</th><th>Path</th></tr></thead>
                                                           <tbody id="writes"></tbody>
                                                         </table>
                                                       </div>
                                                     </div>
                                                   </div>
                                                 </div>

                                                 <div class="card">
                                                   <div class="card-header">
                                                     <h3 class="card-title">Incoming Requests</h3>
                                                     <div class="card-actions">
                                                       <button class="btn btn-outline-secondary btn-sm" id="exportJson">Export JSON</button>
                                                       <button class="btn btn-outline-secondary btn-sm" id="exportCsv">Export CSV</button>
                                                     </div>
                                                   </div>
                                                   <div class="table-responsive">
                                                     <table class="table table-vcenter card-table table-hover">
                                                       <thead>
                                                         <tr>
                                                           <th>Time</th>
                                                           <th>Method</th>
                                                           <th>Path</th>
                                                           <th>Instance</th>
                                                           <th>Handling</th>
                                                           <th>Source IP</th>
                                                           <th>Status</th>
                                                           <th>Duration</th>
                                                           <th>Response Preview</th>
                                                         </tr>
                                                       </thead>
                                                       <tbody id="tbody">
                                                         <tr><td colspan="9" class="text-center text-secondary py-4">Waiting for requests...</td></tr>
                                                       </tbody>
                                                     </table>
                                                   </div>
                                                 </div>

                                               </div>
                                             </div>

                                           </div>
                                         </div>

                                         <!-- Request Detail Modal -->
                                         <div class="modal modal-blur fade" id="requestModal" tabindex="-1" role="dialog">
                                           <div class="modal-dialog modal-xl modal-dialog-scrollable" role="document">
                                             <div class="modal-content">
                                               <div class="modal-header">
                                                 <h5 class="modal-title" id="reqModalTitle">Request</h5>
                                                 <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                                               </div>
                                               <div class="modal-body">
                                                 <div class="datagrid mb-4" id="reqOverview"></div>

                                                 <ul class="nav nav-tabs" data-bs-toggle="tabs">
                                                   <li class="nav-item">
                                                     <a href="#tab-reqheaders" class="nav-link active" data-bs-toggle="tab">Request Headers</a>
                                                   </li>
                                                   <li class="nav-item">
                                                     <a href="#tab-respheaders" class="nav-link" data-bs-toggle="tab">Response Headers</a>
                                                   </li>
                                                   <li class="nav-item">
                                                     <a href="#tab-reqbody" class="nav-link" data-bs-toggle="tab">Request Body</a>
                                                   </li>
                                                   <li class="nav-item">
                                                     <a href="#tab-respbody" class="nav-link" data-bs-toggle="tab">Response Body</a>
                                                   </li>
                                                 </ul>
                                                 <div class="tab-content pt-3">
                                                   <div class="tab-pane active show" id="tab-reqheaders">
                                                     <table class="table table-sm card-table">
                                                       <tbody id="reqHeadersTable"></tbody>
                                                     </table>
                                                   </div>
                                                   <div class="tab-pane" id="tab-respheaders">
                                                     <table class="table table-sm card-table">
                                                       <tbody id="respHeadersTable"></tbody>
                                                     </table>
                                                   </div>
                                                   <div class="tab-pane" id="tab-reqbody">
                                                     <pre class="p-2 mb-0" id="reqBodyPre"></pre>
                                                   </div>
                                                   <div class="tab-pane" id="tab-respbody">
                                                     <pre class="p-2 mb-0" id="respBodyPre"></pre>
                                                   </div>
                                                 </div>
                                               </div>
                                               <div class="modal-footer">
                                                 <button class="btn" data-bs-dismiss="modal">Close</button>
                                               </div>
                                             </div>
                                           </div>
                                         </div>

                                         <!-- JSON Detail Modal (removal snapshots, intercepted write bodies) -->
                                         <div class="modal modal-blur fade" id="jsonModal" tabindex="-1" role="dialog">
                                           <div class="modal-dialog modal-lg modal-dialog-scrollable" role="document">
                                             <div class="modal-content">
                                               <div class="modal-header">
                                                 <h5 class="modal-title" id="jsonModalTitle"></h5>
                                                 <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                                               </div>
                                               <div class="modal-body"><pre class="p-2 mb-0" id="jsonModalBody"></pre></div>
                                               <div class="modal-footer"><button class="btn" data-bs-dismiss="modal">Close</button></div>
                                             </div>
                                           </div>
                                         </div>

                                         <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.7/dist/js/bootstrap.bundle.min.js"></script>
                                         <script src="https://cdn.jsdelivr.net/npm/@tabler/core@1.4.0/dist/js/tabler.min.js"></script>
                                         <script>
                                         const CARDS = [
                                           ['totalRequests','Total Requests'],
                                           ['rejectedRequests','Rejected (401)'],
                                           ['searches','Searches'],
                                           ['searchesFound','Searches Found'],
                                           ['searchesMissed','Searches Missed'],
                                           ['removals','Removal Attempts'],
                                           ['writes','Other Writes'],
                                         ];

                                         let keyDirty = false;
                                         let lastWrites = [];
                                         let lastRemovals = [];

                                         function keyFor(r) { return r.timestamp + '|' + r.method + '|' + r.path + r.query; }

                                         function escapeHtml(s) {
                                           return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
                                         }

                                         function prettyPrint(text) {
                                           if (!text) return '(empty)';
                                           try { return JSON.stringify(JSON.parse(text), null, 2); }
                                           catch { return text; }
                                         }

                                         function timeOf(iso) {
                                           const d = new Date(iso);
                                           return d.toLocaleTimeString('en-US', {hour12: false});
                                         }

                                         function ago(iso) {
                                           if (!iso) return null;
                                           const s = Math.max(0, Math.round((Date.now() - new Date(iso)) / 1000));
                                           if (s < 60) return s + 's ago';
                                           if (s < 3600) return Math.floor(s / 60) + 'm ago';
                                           return Math.floor(s / 3600) + 'h ago';
                                         }

                                         function download(filename, mime, content) {
                                           const blob = new Blob([content], {type: mime});
                                           const url = URL.createObjectURL(blob);
                                           const a = document.createElement('a');
                                           a.href = url;
                                           a.download = filename;
                                           a.click();
                                           URL.revokeObjectURL(url);
                                         }

                                         function exportJson() {
                                           download('mockserver-requests.json', 'application/json',
                                             JSON.stringify(window.__lastReqs || [], null, 2));
                                         }

                                         function exportCsv() {
                                           const cols = ['timestamp', 'method', 'path', 'query', 'mode', 'handling', 'remoteIp', 'statusCode', 'durationMs', 'requestBody', 'responseBody'];
                                           const esc = v => '"' + String(v ?? '').replace(/"/g, '""') + '"';
                                           const lines = [cols.join(',')].concat(
                                             (window.__lastReqs || []).map(r => cols.map(c => esc(r[c])).join(','))
                                           );
                                           download('mockserver-requests.csv', 'text/csv', lines.join('\r\n'));
                                         }

                                         function methodBadgeClass(method) {
                                           switch ((method || '').toUpperCase()) {
                                             case 'GET': return 'badge bg-blue-lt';
                                             case 'POST': return 'badge bg-green-lt';
                                             case 'PUT': return 'badge bg-purple-lt';
                                             case 'DELETE': return 'badge bg-red-lt';
                                             default: return 'badge bg-secondary-lt';
                                           }
                                         }

                                         function statusBadgeClass(status) {
                                           if (status >= 500) return 'badge bg-red-lt';
                                           if (status >= 400) return 'badge bg-orange-lt';
                                           if (status >= 300) return 'badge bg-yellow-lt';
                                           return 'badge bg-green-lt';
                                         }

                                         function instanceBadgeClass(mode) {
                                           return mode === 'Sonarr' ? 'badge bg-green-lt' : 'badge bg-blue-lt';
                                         }

                                         function handlingBadge(h) {
                                           const cls = h === 'intercepted' ? 'bg-purple-lt' : h === 'rejected' ? 'bg-red-lt' : 'bg-secondary-lt';
                                           return `<span class="badge ${cls}">${escapeHtml(h)}</span>`;
                                         }

                                         function datagridItem(title, value) {
                                           return `<div class="datagrid-item">
                                             <div class="datagrid-title">${escapeHtml(title)}</div>
                                             <div class="datagrid-content">${value}</div>
                                           </div>`;
                                         }

                                         function renderOverview(r) {
                                           const reqLen = r.requestContentLength != null ? `${r.requestContentLength} bytes` : '—';
                                           return [
                                             datagridItem('Timestamp', escapeHtml(new Date(r.timestamp).toLocaleString())),
                                             datagridItem('Method', `<span class="${methodBadgeClass(r.method)}">${escapeHtml(r.method)}</span>`),
                                             datagridItem('Path', escapeHtml(r.path + (r.query || ''))),
                                             datagridItem('Instance', `<span class="${instanceBadgeClass(r.mode)}">${escapeHtml(r.mode)}</span>`),
                                             datagridItem('Handling', handlingBadge(r.handling)),
                                             datagridItem('Status', `<span class="${statusBadgeClass(r.statusCode)}">${r.statusCode}</span>`),
                                             datagridItem('Duration', `${r.durationMs} ms`),
                                             datagridItem('Source IP', escapeHtml(r.remoteIp)),
                                             datagridItem('Host', escapeHtml(r.host)),
                                             datagridItem('Scheme', escapeHtml(r.scheme)),
                                             datagridItem('Protocol', escapeHtml(r.protocol)),
                                             datagridItem('User Agent', escapeHtml(r.userAgent || '—')),
                                             datagridItem('Request Content-Type', escapeHtml(r.requestContentType || '—')),
                                             datagridItem('Request Content-Length', reqLen),
                                             datagridItem('Response Content-Type', escapeHtml(r.responseContentType || '—')),
                                             datagridItem('Response Content-Length', `${r.responseContentLength} bytes`),
                                           ].join('');
                                         }

                                         function headersRows(headers) {
                                           const entries = Object.entries(headers || {});
                                           if (entries.length === 0) return '<tr><td class="text-secondary">No headers</td></tr>';
                                           return entries.map(([k, v]) =>
                                             `<tr><th class="w-25 text-nowrap">${escapeHtml(k)}</th><td class="text-break">${escapeHtml(v)}</td></tr>`
                                           ).join('');
                                         }

                                         function openRequestModal(key) {
                                           const r = (window.__lastReqs || []).find(x => keyFor(x) === key);
                                           if (!r) return;

                                           document.getElementById('reqModalTitle').textContent = `${r.method} ${r.path}${r.query || ''}`;
                                           document.getElementById('reqOverview').innerHTML = renderOverview(r);
                                           document.getElementById('reqHeadersTable').innerHTML = headersRows(r.requestHeaders);
                                           document.getElementById('respHeadersTable').innerHTML = headersRows(r.responseHeaders);
                                           document.getElementById('reqBodyPre').textContent = prettyPrint(r.requestBody);
                                           document.getElementById('respBodyPre').textContent = prettyPrint(r.responseBody);

                                           bootstrap.Modal.getOrCreateInstance(document.getElementById('requestModal')).show();
                                         }
                                         window.openRequestModal = openRequestModal;

                                         function openJsonModal(title, text) {
                                           document.getElementById('jsonModalTitle').textContent = title;
                                           document.getElementById('jsonModalBody').textContent = prettyPrint(text);
                                           bootstrap.Modal.getOrCreateInstance(document.getElementById('jsonModal')).show();
                                         }

                                         window.openRemoval = i => {
                                           const r = lastRemovals[i];
                                           if (r) openJsonModal(`Queue item #${r.queueId} — live snapshot`, r.snapshot || '(item was not found in the live queue)');
                                         };
                                         window.openWrite = i => {
                                           const w = lastWrites[i];
                                           if (w) openJsonModal(`${w.method} ${w.path}`, w.body);
                                         };

                                         window.restoreRemoval = async (mode, id) => {
                                           await fetch(`/mock/removals/${mode}/${id}/restore`, {method: 'POST'});
                                           refresh();
                                         };

                                         function renderRows(reqs) {
                                           window.__lastReqs = reqs;
                                           const tbody = document.getElementById('tbody');
                                           if (reqs.length === 0) {
                                             tbody.innerHTML = '<tr><td colspan="9" class="text-center text-secondary py-4">No requests yet</td></tr>';
                                             return;
                                           }
                                           tbody.innerHTML = reqs.map(r => {
                                             const key = keyFor(r);
                                             const safeKey = escapeHtml(key).replace(/'/g, "\\'");
                                             const d = new Date(r.timestamp);
                                             const ts = d.toLocaleTimeString('en-US', {hour12: false}) +
                                                        '.' + String(d.getMilliseconds()).padStart(3, '0');
                                             const preview = r.responseBody ? escapeHtml(r.responseBody.slice(0, 60).replace(/\s+/g, ' ')) : '(empty)';

                                             return `<tr class="cursor-pointer" onclick="openRequestModal('${safeKey}')">
                                               <td class="text-secondary text-nowrap">${ts}</td>
                                               <td><span class="${methodBadgeClass(r.method)}">${r.method}</span></td>
                                               <td class="text-truncate">${escapeHtml(r.path)}${escapeHtml(r.query || '')}</td>
                                               <td><span class="${instanceBadgeClass(r.mode)}">${r.mode}</span></td>
                                               <td>${handlingBadge(r.handling)}</td>
                                               <td class="text-nowrap">${escapeHtml(r.remoteIp)}</td>
                                               <td><span class="${statusBadgeClass(r.statusCode)}">${r.statusCode}</span></td>
                                               <td class="text-nowrap">${r.durationMs} ms</td>
                                               <td class="text-secondary text-truncate">${preview}</td>
                                             </tr>`;
                                           }).join('');
                                         }

                                         function renderRemovals(list) {
                                           lastRemovals = list;
                                           const tbody = document.getElementById('removals');
                                           if (list.length === 0) {
                                             tbody.innerHTML = '<tr><td colspan="8" class="text-center text-secondary py-3">No removal attempts</td></tr>';
                                             return;
                                           }
                                           tbody.innerHTML = list.map((r, i) => {
                                             const opts = [r.blocklist ? 'blocklist' : null, r.removeFromClient ? 'remove from client' : 'keep in client']
                                               .filter(Boolean).map(o => `<span class="badge bg-secondary-lt me-1">${o}</span>`).join('');
                                             const seen = r.lastSeenLive ? `<span class="text-secondary">seen ${ago(r.lastSeenLive)}</span>` : '<span class="text-secondary">—</span>';
                                             return `<tr>
                                               <td class="text-secondary text-nowrap">${timeOf(r.lastAttempt)}</td>
                                               <td><span class="${instanceBadgeClass(r.mode)}">${r.mode}</span></td>
                                               <td>
                                                 <a href="#" onclick="openRemoval(${i}); return false;">${escapeHtml(r.title)}</a>
                                                 ${r.media ? `<div class="text-secondary small">${escapeHtml(r.media)} · #${r.queueId}</div>` : `<div class="text-secondary small">#${r.queueId}</div>`}
                                               </td>
                                               <td>
                                                 ${r.status ? `<span class="badge bg-orange-lt">${escapeHtml(r.status)}</span>` : ''}
                                                 ${r.errorMessage ? `<div class="text-secondary small text-truncate" style="max-width: 280px" title="${escapeHtml(r.errorMessage)}">${escapeHtml(r.errorMessage)}</div>` : ''}
                                               </td>
                                               <td>${opts}</td>
                                               <td>${r.attempts}</td>
                                               <td class="text-nowrap">${seen}</td>
                                               <td><button class="btn btn-sm btn-outline-secondary" onclick="restoreRemoval('${r.mode}', ${r.queueId})">Restore</button></td>
                                             </tr>`;
                                           }).join('');
                                         }

                                         function renderCommands(list) {
                                           const tbody = document.getElementById('commands');
                                           if (list.length === 0) {
                                             tbody.innerHTML = '<tr><td colspan="6" class="text-center text-secondary py-3">No searches yet</td></tr>';
                                             return;
                                           }
                                           tbody.innerHTML = list.map(c => {
                                             const outcome = c.outcome === 'found' ? '<span class="badge bg-green-lt">found</span>'
                                               : c.outcome === 'missed' ? '<span class="badge bg-orange-lt">missed</span>' : '—';
                                             const ids = c.targetIds.length > 6 ? c.targetIds.slice(0, 6).join(', ') + ` +${c.targetIds.length - 6}` : c.targetIds.join(', ');
                                             return `<tr>
                                               <td class="text-secondary text-nowrap">${timeOf(c.queued)}</td>
                                               <td><span class="${instanceBadgeClass(c.mode)}">${c.mode}</span></td>
                                               <td>${escapeHtml(c.name)}</td>
                                               <td class="text-secondary">${escapeHtml(ids)}</td>
                                               <td>${escapeHtml(c.status)}</td>
                                               <td>${outcome}</td>
                                             </tr>`;
                                           }).join('');
                                         }

                                         function renderWrites(list) {
                                           lastWrites = list;
                                           const tbody = document.getElementById('writes');
                                           if (list.length === 0) {
                                             tbody.innerHTML = '<tr><td colspan="4" class="text-center text-secondary py-3">None</td></tr>';
                                             return;
                                           }
                                           tbody.innerHTML = list.map((w, i) => `<tr class="cursor-pointer" onclick="openWrite(${i})">
                                               <td class="text-secondary text-nowrap">${timeOf(w.timestamp)}</td>
                                               <td><span class="${instanceBadgeClass(w.mode)}">${w.mode}</span></td>
                                               <td><span class="${methodBadgeClass(w.method)}">${w.method}</span></td>
                                               <td class="text-truncate">${escapeHtml(w.path)}</td>
                                             </tr>`).join('');
                                         }

                                         const SRC_FIELDS = ['radarrUrl', 'radarrApiKey', 'sonarrUrl', 'sonarrApiKey'];
                                         let srcDirty = false;
                                         let restarting = false;
                                         let startedAt = null;
                                         let restartFrom = null;

                                         function renderSources(s) {
                                           const label = (name, port, url) => `${name} <span class="text-secondary fw-normal">:${port}</span>` +
                                             (url ? '' : ' <span class="badge bg-red-lt ms-1">not configured</span>');
                                           document.getElementById('radarrLabel').innerHTML = label('Radarr', s.radarrPort, s.radarrSource);
                                           document.getElementById('sonarrLabel').innerHTML = label('Sonarr', s.sonarrPort, s.sonarrSource);
                                         }

                                         // Fills the form from current config (user secrets, if set). Skipped while the user is editing.
                                         async function loadSources() {
                                           if (srcDirty) return;
                                           const src = await fetch('/mock/sources').then(r => r.json());
                                           for (const f of SRC_FIELDS) document.getElementById(f).value = src[f] || '';
                                           document.getElementById('secretsPath').textContent = src.secretsPath ? 'Secrets file: ' + src.secretsPath : '';
                                         }

                                         async function saveSources() {
                                           const msg = document.getElementById('srcMsg');
                                           const body = Object.fromEntries(SRC_FIELDS.map(f => [f, document.getElementById(f).value]));
                                           const res = await fetch('/mock/sources', {
                                             method: 'POST',
                                             headers: {'Content-Type': 'application/json'},
                                             body: JSON.stringify(body)
                                           });
                                           const data = await res.json().catch(() => ({}));
                                           if (!res.ok) {
                                             msg.textContent = data.message || 'Save failed';
                                             msg.className = 'text-danger small ms-2';
                                             return;
                                           }
                                           srcDirty = false;
                                           restarting = true;
                                           restartFrom = startedAt;
                                           msg.textContent = 'Saved — restarting server...';
                                           msg.className = 'text-warning small ms-2';
                                         }

                                         async function refresh() {
                                           try {
                                             const [s, reqs, removals, commands, writes] = await Promise.all([
                                               fetch('/mock/status').then(r => r.json()),
                                               fetch('/mock/requests').then(r => r.json()),
                                               fetch('/mock/removals').then(r => r.json()),
                                               fetch('/mock/commands').then(r => r.json()),
                                               fetch('/mock/writes').then(r => r.json()),
                                             ]);

                                             document.getElementById('ports').textContent =
                                               `Radarr :${s.radarrPort}  ·  Sonarr :${s.sonarrPort}`;

                                             if (!keyDirty) document.getElementById('apiKey').value = s.apiKey;
                                             renderSources(s);

                                             document.getElementById('cards').innerHTML = CARDS.map(([k, l]) => `
                                               <div class="col-sm-6 col-lg">
                                                 <div class="card card-sm">
                                                   <div class="card-body">
                                                     <div class="subheader">${l}</div>
                                                     <div class="h1 mb-0">${s[k] ?? 0}</div>
                                                   </div>
                                                 </div>
                                               </div>`
                                             ).join('');

                                             renderRemovals(removals);
                                             renderCommands(commands);
                                             renderWrites(writes);
                                             renderRows(reqs);

                                             const live = document.getElementById('live');
                                             live.textContent = 'live · ' + new Date().toLocaleTimeString('en-US', {hour12: false});
                                             live.className = 'badge bg-green-lt';

                                             startedAt = s.startedAt;
                                             if (restarting && s.startedAt !== restartFrom) {
                                               restarting = false;
                                               const msg = document.getElementById('srcMsg');
                                               msg.textContent = 'Restarted with new sources.';
                                               msg.className = 'text-success small ms-2';
                                               loadSources();
                                             }
                                           } catch (e) {
                                             const live = document.getElementById('live');
                                             live.textContent = restarting ? 'restarting' : 'offline';
                                             live.className = restarting ? 'badge bg-yellow-lt' : 'badge bg-red-lt';
                                           }
                                         }

                                         async function saveKey(value) {
                                           const msg = document.getElementById('keyMsg');
                                           const res = await fetch('/mock/apikey', {
                                             method: 'POST',
                                             headers: {'Content-Type': 'application/json'},
                                             body: JSON.stringify({apiKey: value})
                                           });
                                           const data = await res.json().catch(() => ({}));
                                           if (!res.ok) {
                                             msg.textContent = data.message || 'Save failed';
                                             msg.className = 'text-danger small ms-2';
                                             return;
                                           }
                                           keyDirty = false;
                                           document.getElementById('apiKey').value = data.apiKey;
                                           msg.textContent = 'Saved — update the API key in Trackarr to match.';
                                           msg.className = 'text-success small ms-2';
                                         }

                                         const keyInput = document.getElementById('apiKey');
                                         keyInput.addEventListener('input', () => { keyDirty = true; });
                                         document.getElementById('saveKey').addEventListener('click', () => saveKey(keyInput.value));
                                         document.getElementById('randomKey').addEventListener('click', () => saveKey(''));
                                         document.getElementById('toggleKey').addEventListener('click', () => {
                                           keyInput.type = keyInput.type === 'password' ? 'text' : 'password';
                                         });
                                         document.getElementById('copyKey').addEventListener('click', async () => {
                                           await navigator.clipboard.writeText(keyInput.value);
                                           const msg = document.getElementById('keyMsg');
                                           msg.textContent = 'Copied';
                                           msg.className = 'text-secondary small ms-2';
                                         });
                                         document.getElementById('clearRemovals').addEventListener('click', async () => {
                                           await fetch('/mock/removals', {method: 'DELETE'});
                                           refresh();
                                         });
                                         document.querySelectorAll('.src').forEach(el => el.addEventListener('input', () => { srcDirty = true; }));
                                         document.getElementById('saveSources').addEventListener('click', saveSources);
                                         document.getElementById('toggleSrcKeys').addEventListener('click', () => {
                                           for (const id of ['radarrApiKey', 'sonarrApiKey']) {
                                             const el = document.getElementById(id);
                                             el.type = el.type === 'password' ? 'text' : 'password';
                                           }
                                         });
                                         document.getElementById('exportJson').addEventListener('click', exportJson);
                                         document.getElementById('exportCsv').addEventListener('click', exportCsv);

                                         loadSources().catch(() => {});
                                         refresh();
                                         setInterval(refresh, 1000);
                                         </script>
                                         </body>
                                         </html>
                                         """;
}
