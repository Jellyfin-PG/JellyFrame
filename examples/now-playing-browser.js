(function () {
    'use strict';

    var INTERVAL_MS = parseInt('{{SLIDE_INTERVAL}}', 10) || 8000;
    var API_BASE    = '/JellyFrame/mods/media-bar/api';
    var STYLE_ID    = 'jf-media-bar-style';
    var BAR_ID      = 'jf-media-bar';

    var currentIndex = 0;
    var timer        = null;
    var paused       = false;
    var isFetching   = false;
    var lastPath     = '';


    function injectCSS() {
        if (document.getElementById(STYLE_ID)) return;
        var s = document.createElement('style');
        s.id = STYLE_ID;
        s.textContent = [
            '#' + BAR_ID + ' {',
            '  position: relative; margin: 20px 3.3%; height: 450px;',
            '  border-radius: 14px; overflow: hidden; background: #000;',
            '  box-shadow: 0 10px 30px rgba(0,0,0,0.4); z-index: 100;',
            '}',
            '.jfmb-slide {',
            '  position: absolute; inset: 0;',
            '  background-size: cover; background-position: center 20%;',
            '  opacity: 0; transition: opacity 1s ease-in-out;',
            '  z-index: 1; cursor: pointer;',
            '  display: flex; align-items: flex-end;',
            '}',
            '.jfmb-slide.active { opacity: 1; z-index: 2; }',
            '.jfmb-overlay {',
            '  width: 100%; padding: 100px 48px 36px;',
            '  background: linear-gradient(to top, rgba(0,0,0,.92) 0%, rgba(0,0,0,.45) 55%, transparent 100%);',
            '  color: #fff; pointer-events: none;',
            '}',
            '.jfmb-logo { max-height: 80px; max-width: 320px; object-fit: contain; display: block; margin-bottom: 14px; filter: brightness(1.2); }',
            '.jfmb-title { font-size: 2.4em; font-weight: 700; margin: 0 0 10px; text-shadow: 1px 2px 6px rgba(0,0,0,.8); line-height: 1.1; }',
            '.jfmb-meta { font-size: 1.05em; color: rgba(255,255,255,.75); display: flex; align-items: center; gap: 10px; flex-wrap: wrap; margin-bottom: 16px; }',
            '.jfmb-rating { color: #facc15; font-weight: 600; }',
            '.jfmb-sep { font-size: 6px; opacity: .5; line-height: 1; }',
            '.jfmb-overview { font-size: .95em; color: rgba(255,255,255,.7); max-width: 620px; line-height: 1.5; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; margin-bottom: 20px; }',
            '.jfmb-buttons { display: flex; gap: 12px; pointer-events: auto; }',
            '.jfmb-btn { display: inline-flex; align-items: center; justify-content: center; gap: 6px; height: 42px; padding: 0 20px; box-sizing: border-box; border: none; border-radius: 6px; font-size: .95em; font-weight: 700; cursor: pointer; transition: opacity .2s, transform .15s; }',
            '.jfmb-btn:hover { opacity: .85; transform: translateY(-1px); }',
            '.jfmb-btn-play { background: #fff; color: #000; }',
            '.jfmb-btn-info { background: rgba(255,255,255,.18); color: #fff; backdrop-filter: blur(4px); }',
            '.jfmb-btn-fav  { background: rgba(255,255,255,.12); color: #fff; backdrop-filter: blur(4px); padding: 0 14px; min-width: 42px; font-size: 1.1em; }',
            '.jfmb-btn-fav.active { color: #f87171; }',
            '.jfmb-arrow { position: absolute; top: 50%; transform: translateY(-50%); z-index: 10; cursor: pointer; background: rgba(0,0,0,.4); border: none; color: #fff; width: 44px; height: 44px; border-radius: 50%; font-size: 1.6em; font-weight: 300; display: flex; align-items: center; justify-content: center; transition: background .2s; backdrop-filter: blur(4px); }',
            '.jfmb-arrow:hover { background: rgba(0,0,0,.7); }',
            '.jfmb-arrow-left  { left:  16px; }',
            '.jfmb-arrow-right { right: 16px; }',
            '.jfmb-pause { position: absolute; top: 14px; right: 14px; z-index: 10; cursor: pointer; background: rgba(0,0,0,.35); border: none; color: #fff; width: 36px; height: 36px; border-radius: 50%; font-size: .85em; display: flex; align-items: center; justify-content: center; transition: background .2s; opacity: .6; backdrop-filter: blur(4px); }',
            '.jfmb-pause:hover { opacity: 1; background: rgba(0,0,0,.6); }',
            '.jfmb-dots { position: absolute; bottom: 16px; right: 20px; z-index: 10; display: flex; gap: 7px; align-items: center; }',
            '.jfmb-dot { width: 7px; height: 7px; border-radius: 50%; background: rgba(255,255,255,.4); cursor: pointer; transition: background .3s, transform .3s; }',
            '.jfmb-dot.active { background: #fff; transform: scale(1.5); }',
            '@media (max-width: 768px) {',
            '  #' + BAR_ID + ' { margin: 10px 2%; height: 300px; }',
            '  .jfmb-overlay { padding: 60px 20px 20px; }',
            '  .jfmb-title { font-size: 1.6em; }',
            '  .jfmb-overview { display: none; }',
            '  .jfmb-logo { max-height: 54px; }',
            '}'
        ].join('\n');
        document.head.appendChild(s);
    }

    function fetchViaServerMod() {
        var userId = (typeof ApiClient !== 'undefined') ? ApiClient.getCurrentUserId() : null;
        var url = API_BASE + '/items' + (userId ? '?userId=' + encodeURIComponent(userId) : '');
        return fetch(url)
            .then(function (r) {
                if (!r.ok) throw new Error('server mod ' + r.status);
                return r.json();
            })
            .then(function (data) { return data.items || []; });
    }

    function fetchViaApiClient() {
        if (typeof ApiClient === 'undefined') return Promise.resolve([]);
        var userId = ApiClient.getCurrentUserId();
        if (!userId) return Promise.resolve([]);

        return ApiClient.getJSON(ApiClient.getUrl('Users/' + userId + '/Items', {
            IncludeItemTypes: 'Movie,Series',
            Limit: 10,
            SortBy: 'Random',
            Filters: 'IsUnplayed',
            Fields: 'CommunityRating,ProductionYear,Overview,Genres,OfficialRating,RunTimeTicks',
            Recursive: true,
            ImageTypes: 'Backdrop'
        })).then(function (res) {
            return (res.Items || []).map(function (item) {
                var bdTag   = item.BackdropImageTags && item.BackdropImageTags[0];
                var logoTag = item.ImageTags && item.ImageTags.Logo;
                if (!bdTag) return null;
                return {
                    id:              item.Id,
                    serverId:        item.ServerId,
                    type:            item.Type || 'Movie',
                    name:            item.Name   || '',
                    overview:        item.Overview || '',
                    year:            item.ProductionYear || null,
                    rating:          item.OfficialRating || null,
                    communityRating: item.CommunityRating || null,
                    runTimeTicks:    item.RunTimeTicks || null,
                    genres:          item.Genres || [],
                    isFavorite:      !!(item.UserData && item.UserData.IsFavorite),
                    backdropUrl:     ApiClient.getImageUrl(item.Id, { type: 'Backdrop', maxWidth: 1920, tag: bdTag }),
                    logoUrl:         logoTag ? ApiClient.getImageUrl(item.Id, { type: 'Logo', maxWidth: 400, tag: logoTag }) : null,
                    detailUrl:       '#!/details?id=' + item.Id + (item.ServerId ? '&serverId=' + item.ServerId : '')
                };
            }).filter(Boolean);
        }).catch(function () { return []; });
    }

    function fetchItems() {
        return fetchViaServerMod()
            .then(function (items) { return items.length > 0 ? items : fetchViaApiClient(); })
            .catch(fetchViaApiClient);
    }

    function formatRuntime(ticks) {
        if (!ticks) return '';
        var m = Math.floor(ticks / 600000000);
        return m >= 60 ? Math.floor(m / 60) + 'h ' + (m % 60) + 'm' : m + 'm';
    }

    function buildBar(items) {
        currentIndex = 0;

        var bar      = document.createElement('div');
        bar.id       = BAR_ID;
        var slideEls = [];
        var dotEls   = [];

        items.forEach(function (item, i) {
            var slide = document.createElement('div');
            slide.className = 'jfmb-slide' + (i === 0 ? ' active' : '');
            slide.style.backgroundImage = "url('" + item.backdropUrl + "')";

            var overlay = document.createElement('div');
            overlay.className = 'jfmb-overlay';

            if (item.logoUrl) {
                var logo = document.createElement('img');
                logo.className = 'jfmb-logo';
                logo.src = item.logoUrl;
                logo.alt = item.name;
                overlay.appendChild(logo);
            } else {
                var titleEl = document.createElement('div');
                titleEl.className = 'jfmb-title';
                titleEl.textContent = item.name;
                overlay.appendChild(titleEl);
            }

            var meta = document.createElement('div');
            meta.className = 'jfmb-meta';
            var parts = [];
            if (item.communityRating) parts.push('<span class="jfmb-rating">&#9733; ' + item.communityRating.toFixed(1) + '</span>');
            if (item.year)            parts.push('<span>' + item.year + '</span>');
            if (item.rating)          parts.push('<span>' + item.rating + '</span>');
            if (item.runTimeTicks)    parts.push('<span>' + formatRuntime(item.runTimeTicks) + '</span>');
            if (item.genres && item.genres.length) parts.push('<span>' + item.genres.slice(0, 3).join(' . ') + '</span>');
            meta.innerHTML = parts.join('<span class="jfmb-sep"> * </span>');
            overlay.appendChild(meta);

            if (item.overview) {
                var ov = document.createElement('div');
                ov.className = 'jfmb-overview';
                ov.textContent = item.overview;
                overlay.appendChild(ov);
            }

            var btns = document.createElement('div');
            btns.className = 'jfmb-buttons';

            var playBtn = document.createElement('button');
            playBtn.className = 'jfmb-btn jfmb-btn-play';
            playBtn.innerHTML = '&#9654; Play Now';
            (function (itm) {
                playBtn.onclick = function (e) {
                    e.stopPropagation();
                    if (typeof ApiClient === 'undefined') return;

                    ApiClient.getJSON(ApiClient.getUrl('Sessions')).then(function (sessions) {
                        var deviceId  = typeof ApiClient.deviceId === 'function' ? ApiClient.deviceId() : null;
                        var sessionId = null;

                        for (var i = 0; i < sessions.length; i++) {
                            if (deviceId && sessions[i].DeviceId === deviceId) {
                                sessionId = sessions[i].Id;
                                break;
                            }
                        }

                        if (!sessionId) {
                            for (var j = 0; j < sessions.length; j++) {
                                if (sessions[j].Client && sessions[j].Client.indexOf('Web') !== -1) {
                                    sessionId = sessions[j].Id;
                                    break;
                                }
                            }
                        }

                        if (!sessionId && sessions.length > 0) sessionId = sessions[0].Id;

                        if (!sessionId) {
                            console.error('[media-bar] Could not determine active Session ID.');
                            return;
                        }

                        var playUrl = ApiClient.getUrl('Sessions/' + sessionId + '/Playing') + '?playCommand=PlayNow&itemIds=' + itm.id;
                        var headers = { 'Accept': 'application/json' };

                        if (typeof ApiClient.getAuthorizationHeader === 'function') {
                            headers['Authorization'] = ApiClient.getAuthorizationHeader();
                        } else if (typeof ApiClient.accessToken === 'function') {
                            headers['Authorization'] = 'MediaBrowser Token="' + ApiClient.accessToken() + '"';
                        }

                        fetch(playUrl, { method: 'POST', headers: headers })
                            .then(function (res) {
                                if (!res.ok) console.error('[media-bar] Play command failed:', res.statusText);
                            })
                            .catch(function (err) {
                                console.error('[media-bar] Error sending play command:', err);
                            });

                    }).catch(function (err) {
                        console.error('[media-bar] Error fetching sessions:', err);
                    });
                };
            })(item);
            btns.appendChild(playBtn);

            var infoBtn = document.createElement('button');
            infoBtn.className = 'jfmb-btn jfmb-btn-info';
            infoBtn.textContent = 'More Info';
            (function (itm) {
                infoBtn.onclick = function (e) {
                    e.stopPropagation();
                    var serverId = itm.serverId || (typeof ApiClient !== 'undefined' ? ApiClient.serverId() : '');
                    var hash = '/details?id=' + itm.id + (serverId ? '&serverId=' + serverId : '');
                    window.location.hash = hash;
                };
            })(item);
            btns.appendChild(infoBtn);

            var favBtn = document.createElement('button');
            favBtn.className = 'jfmb-btn jfmb-btn-fav' + (item.isFavorite ? ' active' : '');
            favBtn.setAttribute('data-jfmb-item', item.id);
            favBtn.innerHTML = item.isFavorite ? '&#9829;&#xFE0E;' : '&#9825;&#xFE0E;';
            (function (btn, itm) {
                btn.onclick = function (e) {
                    e.stopPropagation();

                    var userId = (typeof ApiClient !== 'undefined') ? ApiClient.getCurrentUserId() : null;
                    if (!userId) {
                        console.warn('[media-bar] Cannot toggle favourite -- no user ID available');
                        return;
                    }

                    itm.isFavorite = !itm.isFavorite;
                    btn.classList.toggle('active', itm.isFavorite);
                    btn.innerHTML = itm.isFavorite ? '&#9829;&#xFE0E;' : '&#9825;&#xFE0E;';

                    fetch(API_BASE + '/favourite/' + itm.id, {
                        method:  'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body:    JSON.stringify({ favourite: itm.isFavorite, userId: userId })
                    }).then(function (r) {
                        if (!r.ok) {
                            console.error('[media-bar] Favourite toggle failed:', r.status);
                            itm.isFavorite = !itm.isFavorite;
                            btn.classList.toggle('active', itm.isFavorite);
                            btn.innerHTML = itm.isFavorite ? '&#9829;&#xFE0E;' : '&#9825;&#xFE0E;';
                        }

                    }).catch(function (err) {
                        console.error('[media-bar] Favourite toggle error:', err);
                        itm.isFavorite = !itm.isFavorite;
                        btn.classList.toggle('active', itm.isFavorite);
                        btn.innerHTML = itm.isFavorite ? '&#9829;&#xFE0E;' : '&#9825;&#xFE0E;';
                    });
                };
            })(favBtn, item);
            btns.appendChild(favBtn);

            overlay.appendChild(btns);
            slide.appendChild(overlay);

            (function (itm) {
                slide.onclick = function (e) {
                    if (e.target.closest('button')) return;
                    var serverId = itm.serverId || (typeof ApiClient !== 'undefined' ? ApiClient.serverId() : '');
                    var hash = '/details?id=' + itm.id + (serverId ? '&serverId=' + serverId : '');
                    window.location.hash = hash;
                };
            })(item);

            bar.appendChild(slide);
            slideEls.push(slide);
        });

        function goTo(index) {
            slideEls[currentIndex].classList.remove('active');
            dotEls[currentIndex].classList.remove('active');
            currentIndex = ((index % items.length) + items.length) % items.length;
            slideEls[currentIndex].classList.add('active');
            dotEls[currentIndex].classList.add('active');
        }

        function resetTimer() {
            clearInterval(timer);
            if (!paused) {
                timer = setInterval(function () {
                    if (!document.getElementById(BAR_ID)) { clearInterval(timer); return; }
                    goTo(currentIndex + 1);
                }, INTERVAL_MS);
            }
        }

        var leftBtn = document.createElement('button');
        leftBtn.className = 'jfmb-arrow jfmb-arrow-left';
        leftBtn.innerHTML = '&#8249;';
        leftBtn.onclick = function () { goTo(currentIndex - 1); resetTimer(); };
        bar.appendChild(leftBtn);

        var rightBtn = document.createElement('button');
        rightBtn.className = 'jfmb-arrow jfmb-arrow-right';
        rightBtn.innerHTML = '&#8250;';
        rightBtn.onclick = function () { goTo(currentIndex + 1); resetTimer(); };
        bar.appendChild(rightBtn);

        var pauseBtn = document.createElement('button');
        pauseBtn.className = 'jfmb-pause';
        pauseBtn.title = 'Pause / Resume';
        pauseBtn.innerHTML = '&#9646;&#9646;';
        pauseBtn.onclick = function () {
            paused = !paused;
            pauseBtn.innerHTML = paused ? '&#9654;' : '&#9646;&#9646;';
            paused ? clearInterval(timer) : resetTimer();
        };
        bar.appendChild(pauseBtn);

        var dotsWrap = document.createElement('div');
        dotsWrap.className = 'jfmb-dots';
        items.forEach(function (_, i) {
            var dot = document.createElement('div');
            dot.className = 'jfmb-dot' + (i === 0 ? ' active' : '');
            (function (idx) { dot.onclick = function () { goTo(idx); resetTimer(); }; })(i);
            dotsWrap.appendChild(dot);
            dotEls.push(dot);
        });
        bar.appendChild(dotsWrap);

        resetTimer();

        // Poll the server every 30s to refresh isFavorite state from any source
        var pollTimer = setInterval(function () {
            if (!document.getElementById(BAR_ID)) {
                clearInterval(pollTimer);
                return;
            }
            var userId = (typeof ApiClient !== 'undefined') ? ApiClient.getCurrentUserId() : null;
            var url = API_BASE + '/items' + (userId ? '?userId=' + encodeURIComponent(userId) : '');
            fetch(url)
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (data) {
                    if (!data || !data.items) { return; }
                    var bar = document.getElementById(BAR_ID);
                    if (!bar) { return; }
                    for (var i = 0; i < data.items.length; i++) {
                        var itm = data.items[i];
                        var btn = bar.querySelector('[data-jfmb-item="' + itm.id + '"]');
                        if (!btn) { continue; }
                        var isFav = itm.isFavorite === true;
                        btn.classList.toggle('active', isFav);
                        btn.innerHTML = isFav ? '&#9829;&#xFE0E;' : '&#9825;&#xFE0E;';
                    }
                })
                .catch(function () {});
        }, 5000);

        return bar;
    }

    function getActiveHomePage() {
        var activePage = document.querySelector('.page.is-active');
        if (activePage && (activePage.classList.contains('homePage') || activePage.getAttribute('data-type') === 'home' || activePage.id === 'indexPage')) {
            return activePage;
        }

        var visibleHome = document.querySelector('.homePage:not(.hide)');
        if (visibleHome && visibleHome.offsetWidth > 0) return visibleHome;

        return null;
    }

    function findTarget() {
        var page = getActiveHomePage();
        if (!page) return null;

        var selectors = [
            '.homeSectionsContainer',
            '.sections',
            '.padded-left',
            '.emby-scroller',
            '.itemsContainer',
            '.verticalSection'
        ];

        for (var i = 0; i < selectors.length; i++) {
            var el = page.querySelector(selectors[i]);
            if (el && el.parentNode) {
                return { parent: el.parentNode, element: el, page: page };
            }
        }

        return { parent: page, element: page.firstChild, page: page };
    }

    function isHome() {
        var hash = window.location.hash;
        var onHomeURL = (hash === '' || hash === '#' || hash === '#!' || hash.indexOf('home') !== -1);
        return onHomeURL && !!getActiveHomePage();
    }

    function tryInit() {
        if (isFetching || document.getElementById(BAR_ID)) return;
        if (!isHome()) return;

        var targetInfo = findTarget();
        if (!targetInfo) return;

        isFetching = true;
        injectCSS();

        fetchItems().then(function (items) {
            isFetching = false;
            if (!items || items.length === 0) return;
            if (!isHome()) return;

            if (document.getElementById(BAR_ID)) return;

            var currentTarget = findTarget();
            if (!currentTarget) return;

            var bar = buildBar(items);

            if (currentTarget.parent && currentTarget.element) {
                currentTarget.parent.insertBefore(bar, currentTarget.element);
            } else {
                currentTarget.page.prepend(bar);
            }

        }).catch(function (err) {
            console.error('[media-bar]', err);
            isFetching = false;
        });
    }

    function checkState() {
        var path = window.location.hash || window.location.pathname;
        if (path !== lastPath) {
            lastPath = path;
        }

        if (!isHome()) {
            var el = document.getElementById(BAR_ID);
            if (el) { el.remove(); clearInterval(timer); timer = null; }
        } else if (!isFetching && !document.getElementById(BAR_ID) && findTarget()) {
            tryInit();
        }
    }

    var observer = new MutationObserver(checkState);

    observer.observe(document.body, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ['class']
    });

    window.addEventListener('hashchange', checkState);
    window.addEventListener('popstate', checkState);
    document.addEventListener('viewshow', checkState);

    setInterval(checkState, 1500);

    checkState();

})();
