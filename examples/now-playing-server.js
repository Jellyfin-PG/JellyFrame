jf.onStart(function () {
    jf.log.info('now-playing started');

    jf.store.set('requestCount', '0');

    buildSessionCache();

    jf.scheduler.interval(15000, function () {
        buildSessionCache();
    });

    jf.jellyfin.on('playback.started', function (data) {
        buildSessionCache();
        jf.log.debug('Playback started: ' + data.itemName + ' — refreshed cache');
    });

    jf.jellyfin.on('playback.stopped', function (data) {
        buildSessionCache();
        jf.log.debug('Playback stopped — refreshed cache');
    });
});

jf.onStop(function () {
    jf.scheduler.cancelAll();
    jf.log.info('now-playing stopped');
});

function buildSessionCache() {
    var sessions = jf.jellyfin.getSessions() || [];
    var playing = [];

    for (var i = 0; i < sessions.length; i++) {
        var s = sessions[i];
        if (!s.nowPlayingItem) continue;

        playing.push({
            user:      s.userName   || 'Unknown',
            title:     s.nowPlayingItem.name        || 'Unknown',
            type:      s.nowPlayingItem.type        || 'Unknown',
            series:    s.nowPlayingItem.seriesName  || null,
            year:      s.nowPlayingItem.productionYear || null,
            paused:    s.playState ? !!s.playState.isPaused : false,
            client:    s.client     || 'Unknown',
            device:    s.deviceName || 'Unknown'
        });
    }

    jf.cache.set('nowPlaying', playing, 30000);
}

jf.routes.get('/sessions', function (req, res) {
    var n = parseInt(jf.store.get('requestCount') || '0', 10) + 1;
    jf.store.set('requestCount', String(n));

    var playing = jf.cache.get('nowPlaying');
    if (!playing) {
        buildSessionCache();
        playing = jf.cache.get('nowPlaying') || [];
    }

    var label = jf.vars['INSTANCE_LABEL'] || 'Jellyfin';

    return res.json({
        label:    label,
        count:    playing.length,
        sessions: playing
    });
});

jf.routes.get('/status', function (req, res) {
    return res.json({
        requests:       parseInt(jf.store.get('requestCount') || '0', 10),
        cacheEntries:   jf.cache.count,
        schedulerTasks: jf.scheduler.count
    });
});
