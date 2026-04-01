(function () {
    var POLL_INTERVAL = parseInt('{{POLL_INTERVAL}}', 10) || 15000;
    var SHOW_USER     = '{{SHOW_USER}}' === 'true';
    var API_BASE      = '/JellyFrame/mods/now-playing/api';

    var ticker = null;
    var pollTimer = null;

    function createTicker() {
        if (document.getElementById('jf-now-playing')) return;

        var el = document.createElement('div');
        el.id = 'jf-now-playing';
        el.style.cssText = [
            'display:none',
            'align-items:center',
            'gap:8px',
            'padding:0 14px',
            'font-size:12px',
            'font-weight:600',
            'letter-spacing:0.04em',
            'opacity:0.75',
            'white-space:nowrap',
            'overflow:hidden',
            'max-width:320px',
            'text-overflow:ellipsis',
            'cursor:default',
            'color:inherit'
        ].join(';');

        var dot = document.createElement('span');
        dot.id = 'jf-np-dot';
        dot.style.cssText = 'width:7px;height:7px;border-radius:50%;background:#00a4dc;flex-shrink:0;display:inline-block;';

        var text = document.createElement('span');
        text.id = 'jf-np-text';

        el.appendChild(dot);
        el.appendChild(text);
        ticker = el;

        inject();
    }

    function inject() {
        var header = document.querySelector('.headerRight, .viewMenuBar .flex-grow');
        if (header && ticker && !document.getElementById('jf-now-playing')) {
            header.insertBefore(ticker, header.firstChild);
        }
    }

    function update() {
        fetch(API_BASE + '/sessions')
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (!ticker) return;

                var sessions = data.sessions || [];
                if (sessions.length === 0) {
                    ticker.style.display = 'none';
                    return;
                }

                var s = sessions[0];
                var label = s.paused ? '⏸' : '▶';

                var title = s.series
                    ? s.series + ' — ' + s.title
                    : s.title + (s.year ? ' (' + s.year + ')' : '');

                var line = label + '  ' + title;
                if (SHOW_USER) line += '  ·  ' + s.user;

                document.getElementById('jf-np-text').textContent = line;
                document.getElementById('jf-np-dot').style.background = s.paused ? 'rgba(255,255,255,0.3)' : '#00a4dc';
                ticker.style.display = 'flex';
            })
            .catch(function () {
                if (ticker) ticker.style.display = 'none';
            });
    }

    function start() {
        if (pollTimer) return;
        createTicker();
        update();
        pollTimer = setInterval(update, POLL_INTERVAL);
    }

    function stop() {
        if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
        if (ticker) ticker.style.display = 'none';
    }

    var observer = new MutationObserver(function () {
        if (document.querySelector('.headerRight, .viewMenuBar') && !document.getElementById('jf-now-playing')) {
            inject();
        }
    });
    observer.observe(document.body, { childList: true, subtree: true });

    document.addEventListener('visibilitychange', function () {
        document.hidden ? stop() : start();
    });

    start();
})();
