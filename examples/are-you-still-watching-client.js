(function () {
    'use strict';

    var IDLE_MS     = parseInt('{{IDLE_MINUTES}}', 10)   * 60 * 1000 || 10800000;
    var PROMPT_MS   = parseInt('{{PROMPT_SECONDS}}', 10) * 1000       || 60000;
    var BTN_COLOR   = '{{BUTTON_COLOR}}';
    var TEXT_COLOR  = '{{TEXT_COLOR}}';
    var OVERLAY_BG  = '{{OVERLAY_COLOR}}';
    var PROMPT_TEXT = '{{PROMPT_TEXT}}';
    var BTN_TEXT    = '{{BUTTON_TEXT}}';

    var MODAL_ID    = 'jf-sleep-modal';
    var STYLE_ID    = 'jf-sleep-style';

    var idleTimer     = null;
    var promptTimer   = null;
    var isPromptActive = false;
    var lastVideoRef  = null;

    function injectStyles() {
        if (document.getElementById(STYLE_ID)) {
            return;
        }
        var s = document.createElement('style');
        s.id = STYLE_ID;
        s.textContent = [
            '#' + MODAL_ID + ' {',
            '  position:fixed;top:0;left:0;width:100vw;height:100vh;',
            '  z-index:99999;',
            '  display:flex;flex-direction:column;align-items:center;justify-content:center;',
            '  font-family:inherit;',
            '  opacity:0;pointer-events:none;',
            '  transition:opacity .3s ease;',
            '}',
            '#' + MODAL_ID + '.active {',
            '  opacity:1;pointer-events:auto;',
            '}',
            '#' + MODAL_ID + ' .jfst-btn:hover {',
            '  filter:brightness(1.12);transform:scale(1.04);',
            '}'
        ].join('\n');
        document.head.appendChild(s);
    }

    function buildModal() {
        if (document.getElementById(MODAL_ID)) {
            return;
        }
        injectStyles();

        var modal = document.createElement('div');
        modal.id = MODAL_ID;
        modal.style.cssText = 'background:' + OVERLAY_BG + ';backdrop-filter:blur(10px);-webkit-backdrop-filter:blur(10px);';

        var title = document.createElement('div');
        title.style.cssText = 'font-size:2rem;font-weight:700;margin-bottom:2rem;color:' + TEXT_COLOR + ';text-shadow:0 4px 12px rgba(0,0,0,0.5);text-align:center;padding:0 24px;';
        title.textContent = PROMPT_TEXT || 'Are you still watching?';

        var countdown = document.createElement('div');
        countdown.id = MODAL_ID + '-countdown';
        countdown.style.cssText = 'font-size:1rem;opacity:.6;margin-bottom:2rem;color:' + TEXT_COLOR + ';';
        countdown.textContent = 'Pausing in ' + Math.round(PROMPT_MS / 1000) + 's';

        var btn = document.createElement('button');
        btn.className = 'jfst-btn';
        btn.style.cssText = [
            'background:' + BTN_COLOR + ';',
            'color:#fff;border:none;',
            'padding:16px 40px;font-size:1.1rem;border-radius:8px;',
            'cursor:pointer;font-weight:700;',
            'text-transform:uppercase;letter-spacing:1px;',
            'box-shadow:0 4px 16px rgba(0,0,0,0.4);',
            'transition:filter .2s,transform .2s;',
            'font-family:inherit;'
        ].join('');
        btn.textContent = BTN_TEXT || 'Continue Watching';
        btn.onclick = function () {
            dismissPrompt();
        };

        modal.appendChild(title);
        modal.appendChild(countdown);
        modal.appendChild(btn);
        document.body.appendChild(modal);
    }

    function startCountdown() {
        var el = document.getElementById(MODAL_ID + '-countdown');
        if (!el) {
            return;
        }
        var remaining = Math.round(PROMPT_MS / 1000);
        el.textContent = 'Pausing in ' + remaining + 's';

        var tick = setInterval(function () {
            remaining -= 1;
            if (!document.getElementById(MODAL_ID + '-countdown')) {
                clearInterval(tick);
                return;
            }
            el.textContent = remaining > 0 ? ('Pausing in ' + remaining + 's') : 'Exiting player...';
            if (remaining <= 0) {
                clearInterval(tick);
            }
        }, 1000);
    }

    function showPrompt() {
        var video = document.querySelector('video');
        if (!video || video.paused) {
            return;
        }

        isPromptActive = true;
        video.pause();

        buildModal();
        var modal = document.getElementById(MODAL_ID);
        if (modal) {
            modal.classList.add('active');
        }

        startCountdown();

        promptTimer = setTimeout(function () {
            exitPlayer();
        }, PROMPT_MS);
    }

    function dismissPrompt() {
        isPromptActive = false;

        if (promptTimer) {
            clearTimeout(promptTimer);
            promptTimer = null;
        }

        var modal = document.getElementById(MODAL_ID);
        if (modal) {
            modal.classList.remove('active');
        }

        var video = document.querySelector('video');
        if (video && video.paused) {
            video.play().catch(function () {});
        }

        resetIdleTimer();
    }

    function exitPlayer() {
        var backBtn = document.querySelector('.btnHeaderBack') ||
                      document.querySelector('.videoOsdBack');
        if (backBtn) {
            backBtn.click();
        } else {
            window.history.back();
        }
        dismissPrompt();
    }

    function resetIdleTimer() {
        if (idleTimer) {
            clearTimeout(idleTimer);
            idleTimer = null;
        }
        if (isPromptActive) {
            return;
        }
        var video = document.querySelector('video');
        if (video && !video.paused) {
            idleTimer = setTimeout(function () {
                showPrompt();
            }, IDLE_MS);
        }
    }

    function handleActivity() {
        if (isPromptActive) {
            return;
        }
        resetIdleTimer();
    }

    function initListeners() {
        var events = ['mousemove', 'mousedown', 'keydown', 'touchstart', 'scroll'];
        for (var i = 0; i < events.length; i++) {
            window.addEventListener(events[i], handleActivity, { passive: true });
        }
    }

    function checkPlayerState() {
        var video = document.querySelector('video');
        if (video === lastVideoRef) {
            return;
        }

        if (lastVideoRef) {
            lastVideoRef.removeEventListener('play',  resetIdleTimer);
            lastVideoRef.removeEventListener('pause', handleActivity);
        }

        lastVideoRef = video;

        if (video) {
            video.addEventListener('play',  resetIdleTimer);
            video.addEventListener('pause', handleActivity);
            resetIdleTimer();
        } else {
            if (idleTimer) {
                clearTimeout(idleTimer);
                idleTimer = null;
            }
            if (isPromptActive) {
                dismissPrompt();
            }
        }
    }

    function init() {
        initListeners();
        checkPlayerState();
        var observer = new MutationObserver(checkPlayerState);
        observer.observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'complete' || document.readyState === 'interactive') {
        init();
    } else {
        window.addEventListener('DOMContentLoaded', init);
    }

})();
