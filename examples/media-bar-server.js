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
});
