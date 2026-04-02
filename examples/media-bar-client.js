var CACHE_TTL_MS = 5 * 60 * 1000;
var MAX_ITEMS    = 20;

jf.onStart(function () {
    jf.log.info('media-bar started');
    buildCache();

    jf.scheduler.interval(CACHE_TTL_MS, function () {
        jf.log.debug('media-bar: refreshing item cache');
        buildCache();
    });

    jf.jellyfin.on('item.added', function () {
        buildCache();
    });
});

jf.onStop(function () {
    jf.scheduler.cancelAll();
    jf.log.info('media-bar stopped');
});

function getFirstUserId() {
    var users = jf.jellyfin.getUsers() || [];
    return users.length > 0 ? users[0].id : null;
}

function buildCache() {
    try {
        var userId = getFirstUserId();
        jf.log.debug('media-bar: buildCache userId=' + userId);
        var items = fetchItems(userId);
        jf.cache.set('mediaBarItems', items, CACHE_TTL_MS);
        jf.log.debug('media-bar: cached ' + items.length + ' item(s)');
    } catch (e) {
        jf.log.error('media-bar: buildCache error - ' + e.message);
    }
}

function fetchItems(userId) {
    var playlistId = (jf.vars['PLAYLIST_ID'] || '').trim();
    var itemIdsRaw = (jf.vars['ITEM_IDS']    || '').trim();
    var limit      = parseInt(jf.vars['ITEM_COUNT'] || '10', 10);
    limit = Math.min(Math.max(limit, 1), MAX_ITEMS);

    var rawItems = [];

    if (playlistId) {
        rawItems = fetchPlaylistItems(playlistId, userId) || [];
    } else if (itemIdsRaw) {
        rawItems = fetchByIds(itemIdsRaw.split(','), userId) || [];
    } else {
        rawItems = fetchLatest(limit, userId) || [];
    }

    if (rawItems.length === 0) { return []; }

    if (jf.vars['SHUFFLE'] === 'true') {
        rawItems = shuffle(rawItems);
    }

    rawItems = rawItems.slice(0, limit);

    var result = [];
    for (var i = 0; i < rawItems.length; i++) {
        var mapped = mapItem(rawItems[i]);
        if (mapped !== null) { result.push(mapped); }
    }
    return result;
}

function fetchPlaylistItems(playlistId, userId) {
    var playlist = jf.jellyfin.getItem(playlistId, userId);
    if (!playlist) {
        jf.log.warn('media-bar: playlist "' + playlistId + '" not found');
        return [];
    }
    return jf.jellyfin.getItems({
        parentId:  playlistId,
        recursive: 'false',
        userId:    userId
    }) || [];
}

function fetchByIds(ids, userId) {
    var results = [];
    for (var i = 0; i < ids.length; i++) {
        var id = ids[i].trim();
        if (!id) { continue; }
        var item = jf.jellyfin.getItem(id, userId);
        if (item) { results.push(item); }
    }
    return results;
}

function fetchLatest(limit, userId) {
    var half = Math.ceil(limit / 2);

    var movies = jf.jellyfin.getItems({
        type:      'Movie',
        recursive: 'true',
        sortBy:    'DateCreated',
        sortOrder: 'Descending',
        limit:     String(half),
        userId:    userId
    }) || [];

    var shows = jf.jellyfin.getItems({
        type:      'Series',
        recursive: 'true',
        sortBy:    'DateCreated',
        sortOrder: 'Descending',
        limit:     String(half),
        userId:    userId
    }) || [];

    return shuffle(movies.concat(shows));
}

function mapItem(item) {
    if (!item || !item.id) { return null; }

    var itemId = item.id;

    var backdropTag = (item.backdropImageTags && item.backdropImageTags.length > 0)
                    ? item.backdropImageTags[0] : null;
    var logoTag    = (item.imageTags && item.imageTags.Logo)    ? item.imageTags.Logo    : null;
    var primaryTag = (item.imageTags && item.imageTags.Primary) ? item.imageTags.Primary : null;

    var imageBase = '/Items/' + itemId + '/Images/';

    return {
        id:              itemId,
        name:            item.name            || '',
        type:            item.type            || '',
        overview:        item.overview        || '',
        year:            item.productionYear  || null,
        rating:          item.officialRating  || null,
        communityRating: item.communityRating || null,
        runTimeTicks:    item.runTimeTicks    || null,
        genres:          item.genres          || [],
        detailUrl:       '/web/index.html#/details?id=' + itemId,
        backdropUrl:     backdropTag
                           ? imageBase + 'Backdrop/0?tag=' + backdropTag + '&quality=90&maxWidth=1920'
                           : (primaryTag ? imageBase + 'Primary?tag=' + primaryTag + '&quality=90&maxWidth=1920' : null),
        logoUrl:         logoTag
                           ? imageBase + 'Logo?tag=' + logoTag + '&quality=90&maxWidth=400'
                           : null
    };
}

function attachFavorites(items, userId) {
    if (!userId || !items || items.length === 0) { return items; }
    var result = [];
    for (var i = 0; i < items.length; i++) {
        var item  = items[i];
        var fresh = jf.jellyfin.getItem(item.id, userId);
        result.push({
            id:              item.id,
            name:            item.name,
            type:            item.type,
            overview:        item.overview,
            year:            item.year,
            rating:          item.rating,
            communityRating: item.communityRating,
            runTimeTicks:    item.runTimeTicks,
            genres:          item.genres,
            isFavorite:      fresh ? fresh.isFavorite === true : false,
            detailUrl:       item.detailUrl,
            backdropUrl:     item.backdropUrl,
            logoUrl:         item.logoUrl
        });
    }
    return result;
}

function shuffle(arr) {
    var a = arr.slice();
    for (var i = a.length - 1; i > 0; i--) {
        var j = Math.floor(Math.random() * (i + 1));
        var t = a[i]; a[i] = a[j]; a[j] = t;
    }
    return a;
}

jf.routes.get('/items', function (req, res) {
    var userId = (req.query && req.query['userId']) || getFirstUserId();

    var items = jf.cache.get('mediaBarItems');
    if (!items) {
        buildCache();
        items = jf.cache.get('mediaBarItems') || [];
    }

    var enriched = attachFavorites(items, userId);
    return res.json({ count: enriched.length, items: enriched });
});

jf.routes.post('/favourite/:itemId', function (req, res) {
    var itemId = req.pathParams['itemId'];
    var body   = req.body || {};
    var state  = body.favourite !== false;
    var userId = body.userId ? String(body.userId) : null;

    if (!userId) {
        var users = jf.jellyfin.getUsers() || [];
        if (users.length === 0) {
            return res.status(500).json({ error: 'no users' });
        }
        userId = users[0].id;
    }

    jf.jellyfin.setFavorite(userId, itemId, state);
    return res.json({ ok: true, itemId: itemId, userId: userId, favourite: state });
});            '@media (max-width: 768px) {',
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
        if (cachedItems && (Date.now() - cacheTime < 5 * 60 * 1000)) {
            return Promise.resolve(cachedItems);
        }

        return fetchViaServerMod()
            .then(function (items) { return items.length > 0 ? items : fetchViaApiClient(); })
            .catch(fetchViaApiClient)
            .then(function (items) {
                cachedItems = items;
                cacheTime = Date.now();
                return items;
            });
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
            (function (url) {
                infoBtn.onclick = function (e) { e.stopPropagation(); window.location.hash = url; };
            })(item.detailUrl);
            btns.appendChild(infoBtn);

            var favBtn = document.createElement('button');
            favBtn.className = 'jfmb-btn jfmb-btn-fav' + (item.isFavorite ? ' active' : '');
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
                        if (!r.ok) console.error('[media-bar] Favourite toggle failed:', r.status);
                    }).catch(function (err) {
                        console.error('[media-bar] Favourite toggle error:', err);
                    });
                };
            })(favBtn, item);
            btns.appendChild(favBtn);

            overlay.appendChild(btns);
            slide.appendChild(overlay);

            slide.onclick = function (e) {
                if (e.target.closest('button')) return;
                window.location.hash = item.detailUrl;
            };

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

})();            '@media (max-width: 768px) {',
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
        if (cachedItems && (Date.now() - cacheTime < 5 * 60 * 1000)) {
            return Promise.resolve(cachedItems);
        }

        return fetchViaServerMod()
            .then(function (items) { return items.length > 0 ? items : fetchViaApiClient(); })
            .catch(fetchViaApiClient)
            .then(function (items) {
                cachedItems = items;
                cacheTime = Date.now();
                return items;
            });
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
            (function (url) {
                infoBtn.onclick = function (e) { e.stopPropagation(); window.location.hash = url; };
            })(item.detailUrl);
            btns.appendChild(infoBtn);

            var favBtn = document.createElement('button');
            favBtn.className = 'jfmb-btn jfmb-btn-fav' + (item.isFavorite ? ' active' : '');
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
                        if (!r.ok) console.error('[media-bar] Favourite toggle failed:', r.status);
                    }).catch(function (err) {
                        console.error('[media-bar] Favourite toggle error:', err);
                    });
                };
            })(favBtn, item);
            btns.appendChild(favBtn);

            overlay.appendChild(btns);
            slide.appendChild(overlay);

            slide.onclick = function (e) {
                if (e.target.closest('button')) return;
                window.location.hash = item.detailUrl;
            };

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

})();            '  width: 100%; padding: 100px 48px 36px;',
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
        return fetch(API_BASE + '/items')
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
        if (cachedItems && (Date.now() - cacheTime < 5 * 60 * 1000)) {
            return Promise.resolve(cachedItems);
        }

        return fetchViaServerMod()
            .then(function (items) { return items.length > 0 ? items : fetchViaApiClient(); })
            .catch(fetchViaApiClient)
            .then(function (items) {
                cachedItems = items;
                cacheTime = Date.now();
                return items;
            });
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
            (function (url) {
                infoBtn.onclick = function (e) { e.stopPropagation(); window.location.hash = url; };
            })(item.detailUrl);
            btns.appendChild(infoBtn);

            var favBtn = document.createElement('button');
            favBtn.className = 'jfmb-btn jfmb-btn-fav' + (item.isFavorite ? ' active' : '');
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
                        if (!r.ok) console.error('[media-bar] Favourite toggle failed:', r.status);
                    }).catch(function (err) {
                        console.error('[media-bar] Favourite toggle error:', err);
                    });
                };
            })(favBtn, item);
            btns.appendChild(favBtn);

            overlay.appendChild(btns);
            slide.appendChild(overlay);

            slide.onclick = function (e) {
                if (e.target.closest('button')) return;
                window.location.hash = item.detailUrl;
            };

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
