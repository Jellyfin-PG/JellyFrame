var PLUGIN_ID = 'd4e5f6a7-b8c9-0123-defa-456789012345';

var CHIP_OFF = 'display:inline-flex;align-items:center;padding:4px 12px;border-radius:9999px;border:1px solid rgba(255,255,255,0.23);background:transparent;color:rgba(255,255,255,0.7);font-size:12px;font-weight:500;line-height:1.4;cursor:pointer;user-select:none;box-sizing:border-box;transition:all 150ms cubic-bezier(0.4,0,0.2,1);';
var CHIP_ON = 'display:inline-flex;align-items:center;padding:4px 12px;border-radius:9999px;border:1px solid #90caf9;background:rgba(144,202,249,0.16);color:#90caf9;font-size:12px;font-weight:600;line-height:1.4;cursor:pointer;user-select:none;box-sizing:border-box;transition:all 150ms cubic-bezier(0.4,0,0.2,1);box-shadow:0 0 0 1px #90caf9;';

function el(id) {
    return document.getElementById(id);
}

function escHtml(s) {
    return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function safeParseJson(str, fallback) {
    try {
        return JSON.parse(str || '{}') || fallback;
    } catch (e) {
        return fallback;
    }
}

/**
 * Resolve a localised string field.
 * Accepts either a plain string (legacy) or a locale-keyed object:
 *   { "en": "Hello", "fr": "Bonjour", "de": "Hallo" }
 */
function getLocalized(raw) {
    if (!raw) return '';
    if (typeof raw === 'string') return raw;
    if (typeof raw !== 'object') return '';
    var lang = (navigator.language || navigator.userLanguage || 'en').toLowerCase();
    var langBase = lang.split('-')[0];
    var keys = Object.keys(raw);
    function pick(target) {
        for (var i = 0; i < keys.length; i++) {
            if (keys[i].toLowerCase() === target) return raw[keys[i]];
        }
        return null;
    }
    return pick(lang) || pick(langBase) || pick('en') || raw[keys[0]] || '';
}

function getLocalizedTitle(item) {
    return getLocalized(item.title || item.name) || '';
}

function getLocalizedDescription(item) {
    return getLocalized(item.description) || '';
}

function formatDate(dateStr) {
    if (!dateStr) return '';
    try {
        var d = new Date(dateStr);
        if (isNaN(d.getTime())) return '';
        return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
    } catch (e) {
        return '';
    }
}

function sortItems(list, sortBy) {
    return list.slice().sort(function (a, b) {
        if (sortBy === 'name') {
            return getLocalizedTitle(a).localeCompare(getLocalizedTitle(b), undefined, { sensitivity: 'base' });
        }
        if (sortBy === 'name-desc') {
            return getLocalizedTitle(b).localeCompare(getLocalizedTitle(a), undefined, { sensitivity: 'base' });
        }
        if (sortBy === 'newest') {
            var dateA = new Date(a.createdAt || a.date || (a.files && a.files[0] && a.files[0].date) || 0).getTime();
            var dateB = new Date(b.createdAt || b.date || (b.files && b.files[0] && b.files[0].date) || 0).getTime();
            if (dateB !== dateA) return dateB - dateA;
            return getLocalizedTitle(a).localeCompare(getLocalizedTitle(b));
        }
        // Default: recently-updated
        var updateA = new Date(a.updatedAt || a.date || a.createdAt || (a.files && a.files[0] && a.files[0].date) || 0).getTime();
        var updateB = new Date(b.updatedAt || b.date || b.createdAt || (b.files && b.files[0] && b.files[0].date) || 0).getTime();
        if (updateB !== updateA) return updateB - updateA;
        return getLocalizedTitle(a).localeCompare(getLocalizedTitle(b));
    });
}

function parseCleanVersion(vStr) {
    if (!vStr || typeof vStr !== 'string') return null;
    var m = vStr.trim().match(/^v?(\d+(\.\d+)*)/i);
    if (!m) return null;
    var parts = m[1].split('.');
    return {
        major: parseInt(parts[0] || '0', 10),
        minor: parseInt(parts[1] || '0', 10),
        build: parseInt(parts[2] || '0', 10),
        rev: parseInt(parts[3] || '0', 10)
    };
}

function compareVersions(a, b) {
    if (!a && !b) return 0;
    if (!a) return -1;
    if (!b) return 1;
    if (a.major !== b.major) return a.major - b.major;
    if (a.minor !== b.minor) return a.minor - b.minor;
    if (a.build !== b.build) return a.build - b.build;
    return a.rev - b.rev;
}

function evaluateSingleCondition(cond, cur) {
    if (!cond) return true;
    cond = cond.trim();
    if (cond === '*' || cond.toLowerCase() === 'any') return true;

    if (cond.charAt(cond.length - 1) === '+') {
        var baseVer = parseCleanVersion(cond.slice(0, -1).trim());
        if (!baseVer) return true;
        return compareVersions(cur, baseVer) >= 0;
    }
    if (cond.charAt(0) === '^') {
        var baseVer = parseCleanVersion(cond.slice(1).trim());
        if (!baseVer) return true;
        if (compareVersions(cur, baseVer) < 0) return false;
        var upperVer = {
            major: baseVer.major > 0 ? baseVer.major + 1 : 0,
            minor: baseVer.major === 0 ? baseVer.minor + 1 : 0,
            build: 0,
            rev: 0
        };
        return compareVersions(cur, upperVer) < 0;
    }
    if (cond.charAt(0) === '~') {
        var baseVer = parseCleanVersion(cond.slice(1).trim());
        if (!baseVer) return true;
        if (compareVersions(cur, baseVer) < 0) return false;
        var upperVer = { major: baseVer.major, minor: baseVer.minor + 1, build: 0, rev: 0 };
        return compareVersions(cur, upperVer) < 0;
    }
    if (cond.indexOf('>=') === 0) {
        var v = parseCleanVersion(cond.slice(2));
        return v ? compareVersions(cur, v) >= 0 : true;
    }
    if (cond.indexOf('<=') === 0) {
        var v = parseCleanVersion(cond.slice(2));
        return v ? compareVersions(cur, v) <= 0 : true;
    }
    if (cond.indexOf('>') === 0) {
        var v = parseCleanVersion(cond.slice(1));
        return v ? compareVersions(cur, v) > 0 : true;
    }
    if (cond.indexOf('<') === 0) {
        var v = parseCleanVersion(cond.slice(1));
        return v ? compareVersions(cur, v) < 0 : true;
    }
    if (cond.indexOf('==') === 0) {
        var v = parseCleanVersion(cond.slice(2));
        return v ? compareVersions(cur, v) === 0 : true;
    }
    if (cond.indexOf('=') === 0) {
        var v = parseCleanVersion(cond.slice(1));
        return v ? compareVersions(cur, v) === 0 : true;
    }
    if (cond.indexOf('*') !== -1 || cond.toLowerCase().indexOf('x') !== -1) {
        var parts = cond.split('.');
        if (parts.length >= 1 && parts[0] !== '*' && parts[0].toLowerCase() !== 'x') {
            if (cur.major !== parseInt(parts[0], 10)) return false;
        }
        if (parts.length >= 2 && parts[1] !== '*' && parts[1].toLowerCase() !== 'x') {
            if (cur.minor !== parseInt(parts[1], 10)) return false;
        }
        if (parts.length >= 3 && parts[2] !== '*' && parts[2].toLowerCase() !== 'x') {
            if (cur.build !== parseInt(parts[2], 10)) return false;
        }
        return true;
    }
    var plainParts = cond.split('.');
    if (plainParts.length === 2 && !isNaN(parseInt(plainParts[0], 10)) && !isNaN(parseInt(plainParts[1], 10))) {
        if (cur.major !== parseInt(plainParts[0], 10)) return false;
        if (cur.minor !== parseInt(plainParts[1], 10)) return false;
        return true;
    }
    var exact = parseCleanVersion(cond);
    if (exact) return compareVersions(cur, exact) === 0;
    return true;
}

function isCompatibleSingleClause(clause, cur) {
    if (!clause) return true;
    var normalized = clause.replace(/,/g, ' ').trim();
    var rawTokens = normalized.split(/\s+/);
    var tokens = [];
    for (var i = 0; i < rawTokens.length; i++) {
        var p = rawTokens[i].trim();
        if (!p) continue;
        if ((p === '>=' || p === '<=' || p === '>' || p === '<' || p === '==' || p === '=') && i + 1 < rawTokens.length) {
            tokens.push(p + rawTokens[i + 1].trim());
            i++;
        } else {
            tokens.push(p);
        }
    }
    for (var j = 0; j < tokens.length; j++) {
        if (!evaluateSingleCondition(tokens[j], cur)) return false;
    }
    return true;
}

function isVersionCompatible(constraint, currentVer) {
    if (!currentVer) return true;
    if (!constraint || constraint === '*' || constraint.toLowerCase() === 'any') return true;
    var cur = typeof currentVer === 'string' ? parseCleanVersion(currentVer) : currentVer;
    if (!cur) return true;

    var trimmed = constraint.trim();
    if (trimmed.indexOf('||') !== -1) {
        var orParts = trimmed.split('||');
        for (var i = 0; i < orParts.length; i++) {
            if (isCompatibleSingleClause(orParts[i].trim(), cur)) return true;
        }
        return false;
    }
    return isCompatibleSingleClause(trimmed, cur);
}

function fetchServerVersion() {
    if (window.ApiClient) {
        if (typeof ApiClient.serverVersion === 'function') {
            var v = ApiClient.serverVersion();
            if (v) return Promise.resolve(v);
        }
        if (typeof ApiClient.getPublicSystemInfo === 'function') {
            return ApiClient.getPublicSystemInfo().then(function (info) {
                return (info && (info.Version || info.version)) || '';
            }).catch(function () { return ''; });
        }
    }
    return Promise.resolve('');
}
