(function () {
    'use strict';

    var API     = '/JellyFrame/mods/jf-test/api';
    var PANEL_ID = 'jf-test-panel';
    var STYLE_ID = 'jf-test-style';
    var POLL_MS  = 8000;
    var pollTimer = null;

    function injectCSS() {
        if (document.getElementById(STYLE_ID)) { return; }
        var s = document.createElement('style');
        s.id = STYLE_ID;
        s.textContent = [
            '#' + PANEL_ID + ' {',
            '  position:fixed; bottom:20px; right:20px; z-index:99999;',
            '  width:380px; max-height:520px; overflow-y:auto;',
            '  background:#1a1a2e; border:1px solid rgba(255,255,255,0.12);',
            '  border-radius:10px; box-shadow:0 8px 32px rgba(0,0,0,0.6);',
            '  font-family:monospace; font-size:12px; color:#e0e0e0;',
            '}',
            '#' + PANEL_ID + ' .jft-head {',
            '  display:flex; align-items:center; justify-content:space-between;',
            '  padding:10px 14px; border-bottom:1px solid rgba(255,255,255,0.08);',
            '  font-size:13px; font-weight:700; letter-spacing:.03em;',
            '  background:rgba(0,164,220,0.15); border-radius:10px 10px 0 0;',
            '}',
            '#' + PANEL_ID + ' .jft-close {',
            '  background:none; border:none; color:#fff; cursor:pointer;',
            '  font-size:16px; opacity:.5; line-height:1; padding:0 4px;',
            '}',
            '#' + PANEL_ID + ' .jft-close:hover { opacity:1; }',
            '#' + PANEL_ID + ' .jft-body { padding:10px 14px; }',
            '#' + PANEL_ID + ' .jft-row {',
            '  display:flex; align-items:flex-start; gap:8px;',
            '  padding:5px 0; border-bottom:1px solid rgba(255,255,255,0.04);',
            '}',
            '#' + PANEL_ID + ' .jft-row:last-child { border-bottom:none; }',
            '#' + PANEL_ID + ' .jft-badge {',
            '  flex-shrink:0; font-size:10px; font-weight:700;',
            '  border-radius:3px; padding:2px 5px; text-transform:uppercase;',
            '  line-height:1.4;',
            '}',
            '#' + PANEL_ID + ' .jft-pass { background:#14532d; color:#4ade80; }',
            '#' + PANEL_ID + ' .jft-fail { background:#7f1d1d; color:#f87171; }',
            '#' + PANEL_ID + ' .jft-skip { background:#292524; color:#a8a29e; }',
            '#' + PANEL_ID + ' .jft-name { font-weight:600; color:#a5f3fc; }',
            '#' + PANEL_ID + ' .jft-msg  { opacity:.55; font-size:11px; margin-top:1px; }',
            '#' + PANEL_ID + ' .jft-info { flex:1; min-width:0; }',
            '#' + PANEL_ID + ' .jft-summary {',
            '  display:flex; gap:16px; padding:8px 0 4px;',
            '  font-size:11px; font-weight:600;',
            '}',
            '#' + PANEL_ID + ' .jft-ping {',
            '  padding:6px 10px; margin-top:6px;',
            '  background:rgba(255,255,255,0.04);',
            '  border-radius:5px; font-size:11px; opacity:.6;',
            '}',
            '#' + PANEL_ID + ' .jft-actions {',
            '  display:flex; gap:8px; padding:8px 0 2px;',
            '}',
            '#' + PANEL_ID + ' .jft-btn {',
            '  background:rgba(0,164,220,0.2); border:1px solid rgba(0,164,220,0.4);',
            '  color:#7dd3fc; border-radius:5px; cursor:pointer; font-size:11px;',
            '  padding:4px 10px; font-family:monospace;',
            '}',
            '#' + PANEL_ID + ' .jft-btn:hover { background:rgba(0,164,220,0.35); }'
        ].join('\n');
        document.head.appendChild(s);
    }

    function buildPanel() {
        var panel = document.createElement('div');
        panel.id = PANEL_ID;

        var head = document.createElement('div');
        head.className = 'jft-head';
        head.innerHTML = '<span>JellyFrame Feature Tests</span>';

        var closeBtn = document.createElement('button');
        closeBtn.className = 'jft-close';
        closeBtn.textContent = '\u00d7';
        closeBtn.onclick = function () {
            clearInterval(pollTimer);
            panel.remove();
        };
        head.appendChild(closeBtn);
        panel.appendChild(head);

        var body = document.createElement('div');
        body.className = 'jft-body';
        body.id = PANEL_ID + '-body';

        var actions = document.createElement('div');
        actions.className = 'jft-actions';

        var refreshBtn = document.createElement('button');
        refreshBtn.className = 'jft-btn';
        refreshBtn.textContent = 'Re-run Tests';
        refreshBtn.onclick = function () { loadResults(body); };
        actions.appendChild(refreshBtn);

        var pingBtn = document.createElement('button');
        pingBtn.className = 'jft-btn';
        pingBtn.textContent = 'Ping';
        pingBtn.onclick = function () { doPing(body); };
        actions.appendChild(pingBtn);

        var echoBtn = document.createElement('button');
        echoBtn.className = 'jft-btn';
        echoBtn.textContent = 'Echo POST';
        echoBtn.onclick = function () { doEcho(body); };
        actions.appendChild(echoBtn);

        body.appendChild(actions);
        body.innerHTML += '<div style="opacity:.4;font-size:11px;padding:4px 0 2px;">Loading...</div>';

        panel.appendChild(body);
        document.body.appendChild(panel);
        return body;
    }

    function renderResults(body, data) {
        var results = data.results || {};
        var keys = Object.keys(results);

        var pass = 0;
        var fail = 0;
        var skip = 0;
        keys.forEach(function (k) {
            var s = results[k].status;
            if (s === 'pass') { pass++; }
            else if (s === 'fail') { fail++; }
            else { skip++; }
        });

        var html = '';

        // Summary line
        html += '<div class="jft-summary">';
        html += '<span style="color:#4ade80">' + pass + ' pass</span>';
        if (fail > 0) {
            html += '<span style="color:#f87171">' + fail + ' fail</span>';
        }
        if (skip > 0) {
            html += '<span style="color:#a8a29e">' + skip + ' skip</span>';
        }
        html += '</div>';

        // Action buttons -- keep them
        var actionsEl = body.querySelector('.jft-actions');

        // Results rows
        keys.forEach(function (k) {
            var r = results[k];
            var badgeClass = r.status === 'pass' ? 'jft-pass' : (r.status === 'fail' ? 'jft-fail' : 'jft-skip');
            html += '<div class="jft-row">';
            html += '<span class="jft-badge ' + badgeClass + '">' + r.status + '</span>';
            html += '<div class="jft-info">';
            html += '<div class="jft-name">' + escHtml(k) + '</div>';
            if (r.msg) {
                html += '<div class="jft-msg">' + escHtml(r.msg) + '</div>';
            }
            html += '</div></div>';
        });

        html += '<div class="jft-ping" id="jft-ping-line">Last updated: ' + new Date().toLocaleTimeString() + '</div>';

        // Rebuild body keeping actions
        body.innerHTML = '';
        if (actionsEl) {
            body.appendChild(actionsEl);
        } else {
            var newActions = buildActions(body);
            body.appendChild(newActions);
        }

        var content = document.createElement('div');
        content.innerHTML = html;
        body.appendChild(content);
    }

    function buildActions(body) {
        var actions = document.createElement('div');
        actions.className = 'jft-actions';

        var refreshBtn = document.createElement('button');
        refreshBtn.className = 'jft-btn';
        refreshBtn.textContent = 'Re-run Tests';
        refreshBtn.onclick = function () { loadResults(body); };
        actions.appendChild(refreshBtn);

        var pingBtn = document.createElement('button');
        pingBtn.className = 'jft-btn';
        pingBtn.textContent = 'Ping';
        pingBtn.onclick = function () { doPing(body); };
        actions.appendChild(pingBtn);

        var echoBtn = document.createElement('button');
        echoBtn.className = 'jft-btn';
        echoBtn.textContent = 'Echo POST';
        echoBtn.onclick = function () { doEcho(body); };
        actions.appendChild(echoBtn);

        return actions;
    }

    function loadResults(body) {
        fetch(API + '/results')
            .then(function (r) { return r.json(); })
            .then(function (data) { renderResults(body, data); })
            .catch(function (err) {
                var pingLine = body.querySelector('#jft-ping-line');
                if (pingLine) {
                    pingLine.textContent = 'Error: ' + err.message;
                }
            });
    }

    function doPing(body) {
        fetch(API + '/ping')
            .then(function (r) { return r.json(); })
            .then(function (data) {
                var pingLine = document.getElementById('jft-ping-line');
                if (pingLine) {
                    pingLine.textContent = 'Ping OK: ' + data.time;
                }
            })
            .catch(function (err) {
                var pingLine = document.getElementById('jft-ping-line');
                if (pingLine) {
                    pingLine.textContent = 'Ping error: ' + err.message;
                }
            });
    }

    function doEcho(body) {
        fetch(API + '/echo', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ hello: 'world', ts: Date.now() })
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                var pingLine = document.getElementById('jft-ping-line');
                if (pingLine) {
                    pingLine.textContent = 'Echo: ' + JSON.stringify(data.body);
                }
            })
            .catch(function (err) {
                var pingLine = document.getElementById('jft-ping-line');
                if (pingLine) {
                    pingLine.textContent = 'Echo error: ' + err.message;
                }
            });
    }

    function escHtml(str) {
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function init() {
        if (document.getElementById(PANEL_ID)) { return; }
        injectCSS();
        var body = buildPanel();
        loadResults(body);
        pollTimer = setInterval(function () {
            if (!document.getElementById(PANEL_ID)) {
                clearInterval(pollTimer);
                return;
            }
            var pingLine = document.getElementById('jft-ping-line');
            if (pingLine) {
                pingLine.textContent = 'Polling...';
            }
            loadResults(document.getElementById(PANEL_ID + '-body'));
        }, POLL_MS);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        setTimeout(init, 1000);
    }

})();
