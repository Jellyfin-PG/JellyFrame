var STORE_KEY = 'requests';

function loadRequests() {
    var raw = jf.store.get(STORE_KEY);
    if (!raw) {
        return [];
    }
    try {
        return JSON.parse(raw);
    } catch (e) {
        return [];
    }
}

function saveRequests(list) {
    jf.store.set(STORE_KEY, JSON.stringify(list));
}

function nextId(list) {
    var max = 0;
    for (var i = 0; i < list.length; i++) {
        var n = parseInt(list[i].id, 10) || 0;
        if (n > max) {
            max = n;
        }
    }
    return String(max + 1);
}

jf.routes.get('/requests', function (req, res) {
    var list = loadRequests();
    return res.json({ count: list.length, requests: list });
});

jf.routes.post('/requests', function (req, res) {
    var body = req.body || {};

    var title    = body.title    ? String(body.title).trim()    : '';
    var type     = body.type     ? String(body.type).trim()     : '';
    var year     = body.year     ? String(body.year).trim()     : '';
    var note     = body.note     ? String(body.note).trim()     : '';
    var userId   = body.userId   ? String(body.userId).trim()   : '';
    var userName = body.userName ? String(body.userName).trim() : '';

    if (!title || !type) {
        return res.status(400).json({ error: 'title and type are required' });
    }

    var list  = loadRequests();
    var entry = {
        id:        nextId(list),
        title:     title,
        type:      type,
        year:      year,
        note:      note,
        userId:    userId,
        userName:  userName,
        status:    'pending',
        createdAt: new Date().toISOString()
    };
    list.push(entry);
    saveRequests(list);

    jf.log.info('[media-request] New request: ' + title + ' (' + type + ') from ' + (userName || userId || 'unknown'));

    var webhookUrl = (jf.vars['WEBHOOK_URL'] || '').trim();
    if (webhookUrl) {
        var payload = {
            content: 'New media request from ' + (userName || 'unknown') + ': ' + title + ' (' + type + (year ? ', ' + year : '') + ')',
            request: entry
        };
        var webhookSecret = (jf.vars['WEBHOOK_SECRET'] || '').trim();
        var opts = { timeout: 8000 };
        if (webhookSecret) {
            opts.secret = webhookSecret;
        }
        var result = jf.webhooks.send(webhookUrl, payload, opts);
        if (!result.ok) {
            jf.log.warn('[media-request] Webhook delivery failed: ' + result.status);
        }
    }

    return res.json({ ok: true, request: entry });
});

jf.routes.patch('/requests/:id', function (req, res) {
    var id     = req.pathParams['id'];
    var body   = req.body || {};
    var status = body.status ? String(body.status).trim() : '';

    var valid    = ['pending', 'approved', 'declined', 'available'];
    var isValid  = false;
    for (var i = 0; i < valid.length; i++) {
        if (valid[i] === status) {
            isValid = true;
            break;
        }
    }
    if (!isValid) {
        return res.status(400).json({ error: 'status must be pending, approved, declined, or available' });
    }

    var list  = loadRequests();
    var found = false;
    for (var j = 0; j < list.length; j++) {
        if (list[j].id === id) {
            list[j].status    = status;
            list[j].updatedAt = new Date().toISOString();
            found = true;
            break;
        }
    }

    if (!found) {
        return res.status(404).json({ error: 'request not found' });
    }
    saveRequests(list);
    return res.json({ ok: true });
});

jf.routes.delete('/requests/:id', function (req, res) {
    var id      = req.pathParams['id'];
    var list    = loadRequests();
    var newList = [];
    var found   = false;
    for (var i = 0; i < list.length; i++) {
        if (list[i].id === id) {
            found = true;
        } else {
            newList.push(list[i]);
        }
    }
    if (!found) {
        return res.status(404).json({ error: 'request not found' });
    }
    saveRequests(newList);
    return res.json({ ok: true });
});

jf.onStart(function () {
    jf.log.info('[media-request] started');
});

jf.onStop(function () {
    jf.log.info('[media-request] stopped');
});
