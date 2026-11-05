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
        <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@tabler/icons-webfont@latest/dist/tabler-icons.min.css">
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

                <div class="row row-cards mb-3" id="cards"></div>

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
                          <th>Source IP</th>
                          <th>Status</th>
                          <th>Duration</th>
                          <th>Response Preview</th>
                        </tr>
                      </thead>
                      <tbody id="tbody">
                        <tr><td colspan="8" class="text-center text-secondary py-4">Waiting for requests...</td></tr>
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

        <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.7/dist/js/bootstrap.bundle.min.js"></script>
        <script src="https://cdn.jsdelivr.net/npm/@tabler/core@1.4.0/dist/js/tabler.min.js"></script>
        <script>
        const CARDS = [
          ['totalRequests','Total Requests'],
          ['movies','Movies'],
          ['series','Series'],
          ['episodes','Episodes'],
          ['movieQueueItems','Movie Queue'],
          ['seriesQueueItems','Series Queue'],
          ['missingMovies','Missing Movies'],
          ['missingEpisodes','Missing Episodes'],
          ['pendingSearches','Pending Searches'],
          ['profileChangedItems','Profile Changes'],
        ];

        function keyFor(r) { return r.timestamp + '|' + r.method + '|' + r.path + r.query; }

        function escapeHtml(s) {
          return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
        }

        function prettyPrint(text) {
          if (!text) return '(empty)';
          try { return JSON.stringify(JSON.parse(text), null, 2); }
          catch { return text; }
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
          const cols = ['timestamp', 'method', 'path', 'query', 'mode', 'remoteIp', 'statusCode', 'durationMs', 'requestBody', 'responseBody'];
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

        function renderRows(reqs) {
          window.__lastReqs = reqs;
          const tbody = document.getElementById('tbody');
          if (reqs.length === 0) {
            tbody.innerHTML = '<tr><td colspan="8" class="text-center text-secondary py-4">No requests yet</td></tr>';
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
              <td class="text-nowrap">${escapeHtml(r.remoteIp)}</td>
              <td><span class="${statusBadgeClass(r.statusCode)}">${r.statusCode}</span></td>
              <td class="text-nowrap">${r.durationMs} ms</td>
              <td class="text-secondary text-truncate">${preview}</td>
            </tr>`;
          }).join('');
        }

        async function refresh() {
          try {
            const [s, reqs] = await Promise.all([
              fetch('/mock/status').then(r => r.json()),
              fetch('/mock/requests').then(r => r.json())
            ]);

            document.getElementById('ports').textContent =
              `Radarr :${s.radarrPort}  ·  Sonarr :${s.sonarrPort}`;

            document.getElementById('cards').innerHTML = CARDS.map(([k, l]) => `
              <div class="col-sm-6 col-lg-3">
                <div class="card card-sm">
                  <div class="card-body">
                    <div class="subheader">${l}</div>
                    <div class="h1 mb-0">${s[k] ?? 0}</div>
                  </div>
                </div>
              </div>`
            ).join('');

            renderRows(reqs);

            const live = document.getElementById('live');
            live.textContent = 'live · ' + new Date().toLocaleTimeString('en-US', {hour12: false});
            live.className = 'badge bg-green-lt';
          } catch (e) {
            const live = document.getElementById('live');
            live.textContent = 'offline';
            live.className = 'badge bg-red-lt';
          }
        }

        document.getElementById('exportJson').addEventListener('click', exportJson);
        document.getElementById('exportCsv').addEventListener('click', exportCsv);

        refresh();
        setInterval(refresh, 1000);
        </script>
        </body>
        </html>
        """;
}
