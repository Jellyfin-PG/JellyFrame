// JellyFrame Feature Test - Server Mod
// Tests: log, vars, cache, store, userStore, jellyfin (read+write),
//        http, scheduler, bus, webhooks, routes

var RESULTS = {};

function pass(name) {
    RESULTS[name] = { status: 'pass', msg: 'OK' };
    jf.log.info('[test] PASS: ' + name);
}

function fail(name, msg) {
    RESULTS[name] = { status: 'fail', msg: msg || 'failed' };
    jf.log.error('[test] FAIL: ' + name + '  -  ' + (msg || 'failed'));
}

function skip(name, msg) {
    RESULTS[name] = { status: 'skip', msg: msg || 'skipped' };
    jf.log.info('[test] SKIP: ' + name + '  -  ' + (msg || 'skipped'));
}

// -- Log ----------------------------------------------------------------------
function testLog() {
    try {
        jf.log.debug('debug test');
        jf.log.info('info test');
        jf.log.warn('warn test');
        jf.log.error('error test  -  ignore');
        pass('log');
    } catch (e) {
        fail('log', e.message);
    }
}

// -- Vars ---------------------------------------------------------------------
function testVars() {
    try {
        var val = jf.vars['TEST_VAR'] || '';
        RESULTS['vars'] = {
            status: 'pass',
            msg: 'TEST_VAR = "' + val + '"'
        };
    } catch (e) {
        fail('vars', e.message);
    }
}

// -- Cache ---------------------------------------------------------------------
function testCache() {
    try {
        jf.cache.set('test_key', 'hello', 60000);
        var got = jf.cache.get('test_key');
        if (got !== 'hello') { fail('cache', 'get returned: ' + got); return; }
        var has = jf.cache.has('test_key');
        if (!has) { fail('cache', 'has() returned false after set'); return; }
        jf.cache.delete('test_key');
        var gone = jf.cache.get('test_key');
        if (gone !== null) { fail('cache', 'delete did not remove key'); return; }
        pass('cache');
    } catch (e) {
        fail('cache', e.message);
    }
}

// -- Store ---------------------------------------------------------------------
function testStore() {
    try {
        jf.store.set('test_key', 'stored_value');
        var got = jf.store.get('test_key');
        if (got !== 'stored_value') { fail('store', 'get returned: ' + got); return; }
        var keys = jf.store.keys();
        var found = false;
        for (var i = 0; i < keys.length; i++) {
            if (keys[i] === 'test_key') { found = true; break; }
        }
        if (!found) { fail('store', 'key not in keys()'); return; }
        jf.store.delete('test_key');
        var gone = jf.store.get('test_key');
        if (gone !== null && gone !== undefined && gone !== '') {
            fail('store', 'delete did not remove key: ' + gone);
            return;
        }
        pass('store');
    } catch (e) {
        fail('store', e.message);
    }
}

// -- UserStore -----------------------------------------------------------------
function testUserStore() {
    try {
        var uid = 'test_user_001';
        jf.userStore.set(uid, 'pref_theme', 'dark');
        var got = jf.userStore.get(uid, 'pref_theme');
        if (got !== 'dark') { fail('userStore', 'get returned: ' + got); return; }
        var users = jf.userStore.users();
        var found = false;
        for (var i = 0; i < users.length; i++) {
            if (users[i] === uid) { found = true; break; }
        }
        if (!found) { fail('userStore', 'user not in users()'); return; }
        jf.userStore.delete(uid, 'pref_theme');
        pass('userStore');
    } catch (e) {
        fail('userStore', e.message);
    }
}

// -- Jellyfin Read -------------------------------------------------------------
function testJellyfinRead() {
    try {
        var users = jf.jellyfin.getUsers();
        if (!users || users.length === 0) {
            fail('jellyfin.getUsers', 'no users returned');
            return;
        }
        pass('jellyfin.getUsers');

        var userId = users[0].id;
        var user = jf.jellyfin.getUser(userId);
        if (!user || !user.id) {
            fail('jellyfin.getUser', 'could not fetch user by id');
            return;
        }
        pass('jellyfin.getUser');

        var byName = jf.jellyfin.getUserByName(user.name);
        if (!byName || !byName.id) {
            fail('jellyfin.getUserByName', 'could not fetch user by name');
            return;
        }
        pass('jellyfin.getUserByName');

        var items = jf.jellyfin.getItems({
            type: 'Movie',
            recursive: 'true',
            limit: '3',
            userId: userId
        });
        if (!items) {
            fail('jellyfin.getItems', 'returned null');
            return;
        }
        pass('jellyfin.getItems');

        var latest = jf.jellyfin.getLatestItems(userId, 3);
        if (!latest) {
            fail('jellyfin.getLatestItems', 'returned null');
            return;
        }
        pass('jellyfin.getLatestItems');

        var libraries = jf.jellyfin.getUserLibraries(userId);
        if (!libraries) {
            fail('jellyfin.getUserLibraries', 'returned null');
            return;
        }
        pass('jellyfin.getUserLibraries');

        var sessions = jf.jellyfin.getSessions();
        if (!sessions) {
            fail('jellyfin.getSessions', 'returned null');
            return;
        }
        pass('jellyfin.getSessions');

        var searchResults = jf.jellyfin.search('a', 2);
        if (!searchResults) {
            fail('jellyfin.search', 'returned null');
            return;
        }
        pass('jellyfin.search');

        var encoder = jf.jellyfin.getEncoderInfo();
        if (!encoder) {
            fail('jellyfin.getEncoderInfo', 'returned null');
            return;
        }
        pass('jellyfin.getEncoderInfo');

        if (items && items.length > 0) {
            var item = jf.jellyfin.getItem(items[0].id, userId);
            if (!item || !item.id) {
                fail('jellyfin.getItem', 'could not fetch item by id');
            } else {
                pass('jellyfin.getItem');
            }
        } else {
            skip('jellyfin.getItem', 'no movies in library');
        }

    } catch (e) {
        fail('jellyfin.read', e.message);
    }
}

// -- Jellyfin Events -----------------------------------------------------------
function testJellyfinEvents() {
    try {
        jf.jellyfin.on('playback.started', function (data) {
            jf.log.info('[test] playback.started fired');
            jf.cache.set('event_playback_started', 'true', 300000);
        });
        RESULTS['jellyfin.events'] = {
            status: 'pass',
            msg: 'Handler registered for playback.started'
        };
    } catch (e) {
        fail('jellyfin.events', e.message);
    }
}

// -- HTTP ----------------------------------------------------------------------
function testHttp() {
    try {
        var result = jf.http.get('https://httpbin.org/get');
        if (!result) {
            fail('http.get', 'returned null');
            return;
        }
        if (!result.ok) {
            fail('http.get', 'status ' + result.status + ': ' + result.body);
            return;
        }
        var parsed = result.json();
        if (!parsed) {
            fail('http.get', 'json() parse failed');
            return;
        }
        RESULTS['http.get'] = { status: 'pass', msg: 'status ' + result.status };

        var postResult = jf.http.post('https://httpbin.org/post',
            JSON.stringify({ jf: 'test' }));
        if (!postResult || !postResult.ok) {
            fail('http.post', 'status ' + (postResult ? postResult.status : '?'));
            return;
        }
        RESULTS['http.post'] = { status: 'pass', msg: 'status ' + postResult.status };

    } catch (e) {
        fail('http', e.message);
    }
}

// -- Scheduler -----------------------------------------------------------------
function testScheduler() {
    try {
        var fired = false;
        var id = jf.scheduler.interval(500, function () {
            if (!fired) {
                fired = true;
                jf.cache.set('scheduler_fired', 'true', 60000);
                jf.log.info('[test] scheduler interval fired');
            }
        });
        if (!id) {
            fail('scheduler.interval', 'returned null id');
            return;
        }

        var count = jf.scheduler.count;
        if (count < 1) {
            fail('scheduler.count', 'expected >= 1, got ' + count);
            return;
        }

        jf.scheduler.cancel(id);
        RESULTS['scheduler'] = {
            status: 'pass',
            msg: 'interval registered (id=' + id + '), cancel called'
        };
    } catch (e) {
        fail('scheduler', e.message);
    }
}

// -- Cron ----------------------------------------------------------------------
function testCron() {
    try {
        var id = jf.scheduler.cron('* * * * *', function () {
            jf.log.info('[test] cron fired');
        });
        if (!id) {
            fail('scheduler.cron', 'returned null id');
            return;
        }
        jf.scheduler.cancel(id);
        pass('scheduler.cron');
    } catch (e) {
        fail('scheduler.cron', e.message);
    }
}

// -- Bus -----------------------------------------------------------------------
function testBus() {
    try {
        var received = false;
        var subId = jf.bus.on('test.ping', function (data, from) {
            received = true;
            jf.cache.set('bus_received', 'true', 60000);
            jf.log.info('[test] bus received test.ping from ' + from);
        });
        if (!subId) {
            fail('bus.on', 'returned null subscription id');
            return;
        }

        var count = jf.bus.emit('test.ping', { msg: 'hello' });
        RESULTS['bus'] = {
            status: 'pass',
            msg: 'subscribed (id=' + subId + '), emit delivered to ' + count + ' handler(s)'
        };

        jf.bus.off(subId);
    } catch (e) {
        fail('bus', e.message);
    }
}

// -- Webhooks ------------------------------------------------------------------
function testWebhooks() {
    try {
        jf.webhooks.register('test-hook', function (payload, headers) {
            jf.log.info('[test] webhook test-hook received');
            jf.cache.set('webhook_received', 'true', 60000);
        });

        var registered = jf.webhooks.list();
        var found = false;
        for (var i = 0; i < registered.length; i++) {
            if (registered[i] === 'test-hook') { found = true; break; }
        }
        if (!found) {
            fail('webhooks', 'registered hook not in list()');
            return;
        }

        RESULTS['webhooks'] = {
            status: 'pass',
            msg: 'registered test-hook, POST to /JellyFrame/mods/jf-test/webhooks/test-hook to trigger'
        };
    } catch (e) {
        fail('webhooks', e.message);
    }
}

// -- Perms ---------------------------------------------------------------------
function testPerms() {
    try {
        var granted = jf.perms.granted();
        if (!granted || !granted.length) {
            fail('perms', 'no permissions granted');
            return;
        }
        var hasRead = jf.perms.has('jellyfin.read');
        RESULTS['perms'] = {
            status: 'pass',
            msg: 'granted: [' + granted.join(', ') + '], jellyfin.read=' + hasRead
        };
    } catch (e) {
        fail('perms', e.message);
    }
}

// -- Routes --------------------------------------------------------------------
jf.routes.get('/results', function (req, res) {
    // Run all tests on each request so results are always fresh
    runAll();
    return res.json({
        mod:     'jf-test',
        results: RESULTS
    });
});

jf.routes.get('/ping', function (req, res) {
    return res.json({ pong: true, time: new Date().toString() });
});

jf.routes.post('/echo', function (req, res) {
    return res.json({
        method:     req.method,
        body:       req.body,
        pathParams: req.pathParams,
        query:      req.query
    });
});

jf.routes.get('/store-check', function (req, res) {
    return res.json({
        schedulerFired:  jf.cache.get('scheduler_fired'),
        busReceived:     jf.cache.get('bus_received'),
        webhookReceived: jf.cache.get('webhook_received'),
        eventFired:      jf.cache.get('event_playback_started')
    });
});

// -- Run all -------------------------------------------------------------------
function runAll() {
    RESULTS = {};
    testLog();
    testVars();
    testCache();
    testStore();
    testUserStore();
    testJellyfinRead();
    testJellyfinEvents();
    testHttp();
    testScheduler();
    testCron();
    testBus();
    testWebhooks();
    testPerms();
}

jf.onStart(function () {
    jf.log.info('[test] JellyFrame feature test mod started');
    runAll();
    jf.log.info('[test] initial test run complete');
});

jf.onStop(function () {
    jf.scheduler.cancelAll();
    jf.webhooks.unregister('test-hook');
    jf.jellyfin.off('playback.started');
    jf.bus.offAll();
    jf.log.info('[test] test mod stopped');
});
