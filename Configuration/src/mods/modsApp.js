(function () {
    var state = {
        mods: [],
        enabledIds: [],
        pendingIds: null,
        modVars: {},
        pendingVars: null,
        config: {},
        serverVersion: '',
        activeTags: [],
        typeFilter: 'all',
        sortBy: 'recently-updated',
        query: ''
    };

    var modalContext = null;

    function isModSupported(mod, serverVersion) {
        if (!serverVersion) return true;
        if (mod.jellyfin && !isVersionCompatible(mod.jellyfin, serverVersion)) {
            return false;
        }
        if (Array.isArray(mod.files) && mod.files.length > 0) {
            var hasComp = mod.files.some(function (f) {
                return isVersionCompatible(f.jellyfin || mod.jellyfin, serverVersion);
            });
            if (!hasComp) return false;
        }
        return true;
    }

    function getModType(m) {
        var hasCss = !!m.cssUrl || (m.files && m.files.some(function (f) { return f.type === 'css'; }));
        var hasJs = !!m.jsUrl || (m.files && m.files.some(function (f) { return f.type === 'js'; }));
        var hasServer = !!m.serverJs || (m.files && m.files.some(function (f) { return f.type === 'server' || f.type === 'serverjs'; }));
        var count = [hasCss, hasJs, hasServer].filter(Boolean).length;
        if (count > 1) return 'mixed';
        if (hasCss) return 'css';
        if (hasJs) return 'js';
        if (hasServer) return 'server';
        return ((m.type || 'js') || 'js').toLowerCase();
    }

    var OFFICIAL_MODS_URL = 'https://cdn.jsdelivr.net/gh/Jellyfin-PG/JellyFrame-Resources@main/mods.v2.json';

    function loadConfig() {
        switchTab('marketplace');
        Dashboard.showLoadingMsg();
        fetchServerVersion().then(function (ver) {
            state.serverVersion = ver || '';
            if (state.mods && state.mods.length > 0) render();
        });
        ApiClient.getPluginConfiguration(PLUGIN_ID).then(function (cfg) {
            state.config = cfg;
            state.enabledIds = cfg.EnabledMods || [];
            state.pendingIds = null;
            state.modVars = safeParseJson(cfg.ModVars, {});
            state.pendingVars = null;
            el('modsUrl').value = cfg.ModsUrl || OFFICIAL_MODS_URL;
            updateModSourceStatus();
            el('chkDebug').checked = cfg.DebugLogging || false;

            var cached = safeParseJson(cfg.CachedMods, []);
            if (Array.isArray(cached) && cached.length > 0) {
                state.mods = cached;
                buildTypeChips(cached);
                buildTagChips(cached);
                render();
            }

            Dashboard.hideLoadingMsg();
            updateActiveBar();
            updatePending();

            var url = el('modsUrl').value.trim();
            if (url) loadMods(url);
        });
    }

    function updateModSourceStatus() {
        var url = (el('modsUrl').value || '').trim();
        var statusEl = document.getElementById('mm-sourceStatus');
        if (!statusEl) { return; }
        if (!url) { statusEl.style.display = 'none'; return; }
        statusEl.style.display = 'block';
        if (url === OFFICIAL_MODS_URL) {
            statusEl.innerHTML = '<span style="color:#66bb6a;font-weight:600;">Official JellyFrame repository</span>';
        } else {
            statusEl.innerHTML = '<span style="color:#ffa726;font-weight:600;">Unofficial source</span><span style="opacity:.6;"> - only load repositories you trust</span>';
        }
    }

    function loadMods(url) {
        if (state.mods.length === 0) {
            el('modGrid').innerHTML = '<div class="stateMsg">Loading mods\u2026</div>';
        }
        el('mm-tagFilters').innerHTML = '';
        el('typeFilters').innerHTML = '';
        fetch(url)
            .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
            .then(function (mods) {
                state.mods = mods;
                buildTypeChips(mods);
                buildTagChips(mods);
                render();
                checkWhatsNew(mods);
            })
            .catch(function (err) {
                if (state.mods.length === 0) {
                    el('modGrid').innerHTML = '<div class="stateMsg">Could not load mods: ' + err.message + '</div>';
                } else {
                    console.warn('[JellyFrame] Could not refresh mods from URL:', err.message, '\u2014 showing cached mods');
                }
            });
    }

    function buildTypeChips(mods) {
        var types = ['all'], seen = {};
        mods.forEach(function (m) {
            var t = getModType(m);
            if (!seen[t]) { seen[t] = true; types.push(t); }
        });
        var c = el('typeFilters');
        if (!c) return;
        c.innerHTML = '';

        var group = document.createElement('div');
        group.className = 'mui-segmented-group';
        group.setAttribute('role', 'radiogroup');
        group.setAttribute('aria-label', 'Filter mods by type');

        var typeLabels = {
            'all': 'All',
            'js': 'Client JS',
            'css': 'CSS',
            'server': 'Server',
            'mixed': 'Mixed'
        };

        types.forEach(function (type) {
            var b = document.createElement('button');
            b.type = 'button';
            var isActive = (state.typeFilter === type);
            b.className = 'mui-segmented-btn' + (isActive ? ' active' : '');
            b.setAttribute('role', 'radio');
            b.setAttribute('aria-checked', isActive ? 'true' : 'false');
            b.textContent = typeLabels[type] || type.toUpperCase();

            b.addEventListener('click', function () {
                state.typeFilter = type;
                group.querySelectorAll('.mui-segmented-btn').forEach(function (btn) {
                    var act = (btn === b);
                    btn.className = 'mui-segmented-btn' + (act ? ' active' : '');
                    btn.setAttribute('aria-checked', act ? 'true' : 'false');
                });
                render();
            });
            group.appendChild(b);
        });

        c.appendChild(group);
    }

    function buildTagChips(mods) {
        var set = {};
        mods.forEach(function (m) { (m.tags || []).forEach(function (t) { set[t] = true; }); });
        state.activeTags = [];
        var c = el('mm-tagFilters');
        if (!c) return;
        c.innerHTML = '';
        var tags = Object.keys(set).sort();
        var tagSec = el('tagFiltersSection');
        if (tagSec) {
            tagSec.style.display = tags.length > 0 ? '' : 'none';
        }
        tags.forEach(function (tag) {
            var b = document.createElement('span');
            b.setAttribute('style', CHIP_OFF);
            b.textContent = tag;
            b.addEventListener('click', function () {
                var i = state.activeTags.indexOf(tag);
                if (i === -1) { state.activeTags.push(tag); b.setAttribute('style', CHIP_ON); }
                else { state.activeTags.splice(i, 1); b.setAttribute('style', CHIP_OFF); }
                render();
            });
            c.appendChild(b);
        });
    }

    function render() {
        var q = state.query.toLowerCase();
        var ids = currentIds();
        var list = state.mods.filter(function (m) {
            if (!isModSupported(m, state.serverVersion)) return false;
            var mq = !q ||
                getLocalizedTitle(m).toLowerCase().includes(q) ||
                (m.author || '').toLowerCase().includes(q) ||
                getLocalizedDescription(m).toLowerCase().includes(q);
            var mt = state.activeTags.length === 0 ||
                state.activeTags.every(function (t) { return (m.tags || []).includes(t); });
            var mtype = state.typeFilter === 'all' || getModType(m) === state.typeFilter;
            return mq && mt && mtype;
        });

        list = sortItems(list, state.sortBy || 'recently-updated');

        var grid = el('modGrid');
        if (list.length === 0) {
            grid.innerHTML = '<div class="stateMsg">No mods match your search or current server version (' + escHtml(state.serverVersion || 'all') + ').</div>';
            return;
        }
        grid.innerHTML = '';
        list.forEach(function (m) { grid.appendChild(buildCard(m, ids)); });
    }

    function buildCard(mod, ids) {
        var isOn = ids.indexOf(mod.id) !== -1;
        var type = ((mod.type || "js") || 'js').toLowerCase();
        var hasVars = ((mod.vars || []) || []).length > 0;
        var hasCss = !!(mod.cssUrl || "") || (mod.files && mod.files.some(function (f) { return f.type === 'css'; }));
        var hasJs = !!(mod.jsUrl || "") || (mod.files && mod.files.some(function (f) { return f.type === 'js'; }));
        var hasSrv = !!(mod.serverJs || "") || (mod.files && mod.files.some(function (f) { return f.type === 'server' || f.type === 'serverjs'; }));

        var card = document.createElement('div');
        card.className = 'card mui-card overflowBackdropCard' + (isOn ? ' enabledCard' : '') + (mod.editorsChoice ? ' editorsChoiceCard' : '');

        var imgContainer = document.createElement('div');
        imgContainer.className = 'cardImageContainer coveredImage';
        imgContainer.style.cursor = 'zoom-in';
        imgContainer.style.pointerEvents = 'auto';
        var _shotCount = (mod.previewUrl ? 1 : 0)
            + (mod.screenshots ? mod.screenshots.length : 0);
        imgContainer.title = _shotCount > 1 ? 'Click to view gallery (' + _shotCount + ' images)' : 'Click to preview';
        imgContainer.addEventListener('click', function (e) {
            e.stopPropagation();
            openPreviewModal(mod);
        });
        if ((mod.previewUrl || "")) {
            var img = document.createElement('img');
            img.src = (mod.previewUrl || "");
            img.alt = (mod.name || "");
            img.onerror = function () {
                img.remove();
                var fallback = document.createElement('div');
                fallback.className = 'cardImageFallback';
                fallback.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;min-height:160px;display:flex;align-items:center;justify-content:center;opacity:.25;pointer-events:none;color:#90caf9;';
                fallback.innerHTML = '<svg width="44" height="44" viewBox="0 0 24 24" fill="currentColor"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>';
                imgContainer.appendChild(fallback);
            };
            imgContainer.appendChild(img);
        } else {
            var fallback = document.createElement('div');
            fallback.className = 'cardImageFallback';
            fallback.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;min-height:160px;display:flex;align-items:center;justify-content:center;opacity:.25;pointer-events:none;color:#90caf9;';
            fallback.innerHTML = '<svg width="44" height="44" viewBox="0 0 24 24" fill="currentColor"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>';
            imgContainer.appendChild(fallback);
        }

        var badgeParts = [];
        if (hasCss) badgeParts.push('CSS');
        if (hasJs) badgeParts.push('JS');
        if (hasSrv) badgeParts.push('SRV');
        if (badgeParts.length === 0) badgeParts.push(type.toUpperCase());
        var frontendCount = (hasCss ? 1 : 0) + (hasJs ? 1 : 0);
        var badgeClass = hasSrv && frontendCount > 0 ? 'mixed' : hasSrv ? 'srv' : (hasCss && hasJs ? 'both' : hasCss ? 'css' : 'js');
        var badge = document.createElement('div');
        badge.className = 'typeBadge typeBadge-' + badgeClass;
        badge.textContent = badgeParts.join('+');
        imgContainer.appendChild(badge);

        if (isOn) {
            var chk = document.createElement('div');
            chk.className = 'enabledCheck';
            chk.textContent = '\u2713';
            chk.style.pointerEvents = 'none';
            imgContainer.appendChild(chk);
        }

        if (hasVars) {
            var varsBadge = document.createElement('div');
            varsBadge.style.cssText = 'position:absolute;bottom:8px;left:8px;background:rgba(0,0,0,.75);border:1px solid rgba(255,255,255,.25);color:rgba(255,255,255,.85);border-radius:4px;font-size:10px;font-weight:600;padding:2px 7px;z-index:1;letter-spacing:.04em;pointer-events:none;';
            varsBadge.textContent = 'CONFIGURABLE';
            imgContainer.appendChild(varsBadge);
        }

        if (_shotCount > 1) {
            var shotsBadge = document.createElement('div');
            shotsBadge.style.cssText = 'position:absolute;bottom:8px;right:8px;background:rgba(0,0,0,.75);border:1px solid rgba(255,255,255,.25);color:rgba(255,255,255,.85);border-radius:4px;font-size:10px;font-weight:600;padding:2px 7px;z-index:1;letter-spacing:.04em;pointer-events:none;display:inline-flex;align-items:center;gap:3px;';
            shotsBadge.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M12 12m-3.2 0a3.2 3.2 0 1 0 6.4 0a3.2 3.2 0 1 0 -6.4 0M9 2L7.17 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2h-3.17L15 2H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z"/></svg>' + _shotCount;
            imgContainer.appendChild(shotsBadge);
        }

        if (mod.editorsChoice) {
            var ecBadge = document.createElement('div');
            ecBadge.style.cssText = 'position:absolute;top:8px;right:8px;z-index:3;background:linear-gradient(135deg,#ffa726,#f57c00);border:1px solid rgba(255,255,255,0.4);border-radius:4px;padding:3px 8px;font-size:10px;font-weight:800;letter-spacing:.06em;text-transform:uppercase;color:#000;box-shadow:0 2px 6px rgba(0,0,0,0.4);pointer-events:none;display:inline-flex;align-items:center;gap:3px;';
            ecBadge.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z"/></svg>Editor\'s Choice';
            imgContainer.appendChild(ecBadge);
        }

        card.appendChild(imgContainer);

        var footer = document.createElement('div');
        footer.className = 'cardFooter';
        footer.style.cssText = 'padding:14px 14px 12px;display:flex;flex-direction:column;flex:1;';

        var headerRow = document.createElement('div');
        headerRow.style.cssText = 'display:flex;align-items:flex-start;justify-content:space-between;gap:8px;margin-bottom:2px;';

        var nameEl = document.createElement('div');
        nameEl.className = 'cardText';
        nameEl.style.cssText = 'font-size:0.95rem;font-weight:600;color:rgba(255,255,255,0.87);line-height:1.4;flex:1;';
        nameEl.textContent = getLocalizedTitle(mod);
        headerRow.appendChild(nameEl);

        var jfConstraint = mod.jellyfin || (mod.files && mod.files[0] && mod.files[0].jellyfin) || '';
        if (jfConstraint) {
            var jfPill = document.createElement('span');
            jfPill.style.cssText = 'display:inline-flex;align-items:center;padding:1px 6px;border-radius:4px;font-size:10px;font-weight:600;background:rgba(144,202,249,0.12);border:1px solid rgba(144,202,249,0.3);color:#90caf9;white-space:nowrap;flex-shrink:0;';
            jfPill.textContent = 'JF ' + jfConstraint;
            headerRow.appendChild(jfPill);
        }
        footer.appendChild(headerRow);

        var metaParts = [];
        if (mod.author) metaParts.push('by ' + mod.author);
        if (mod.version) metaParts.push('v' + mod.version);
        var formattedUpdated = formatDate(mod.updatedAt || mod.date || (mod.files && mod.files[0] && mod.files[0].date));
        if (formattedUpdated) metaParts.push(formattedUpdated);

        if (metaParts.length > 0) {
            var authEl = document.createElement('div');
            authEl.className = 'cardText cardText-secondary';
            authEl.style.cssText = 'font-size:0.75rem;color:rgba(255,255,255,0.6);margin-bottom:6px;';
            authEl.textContent = metaParts.join(' \u2022 ');
            footer.appendChild(authEl);
        }

        var _modDesc = getLocalizedDescription(mod);
        if (_modDesc) {
            var descEl = document.createElement('div');
            descEl.className = 'cardText cardText-secondary';
            descEl.style.cssText = 'font-size:0.8125rem;color:rgba(255,255,255,0.7);line-height:1.5;margin-top:4px;white-space:normal;word-break:break-word;';
            var TRUNCATE_LEN = 100;
            if (_modDesc.length <= TRUNCATE_LEN) {
                descEl.textContent = _modDesc;
            } else {
                var shortText = document.createTextNode(_modDesc.slice(0, TRUNCATE_LEN) + '\u2026 ');
                var readMoreBtn = document.createElement('button');
                readMoreBtn.type = 'button';
                readMoreBtn.textContent = 'more';
                readMoreBtn.style.cssText = 'background:none;border:none;padding:0;color:#90caf9;cursor:pointer;font-size:inherit;font-family:inherit;pointer-events:auto;text-decoration:underline;font-weight:500;';
                readMoreBtn.addEventListener('click', function (e) {
                    e.stopPropagation();
                    openPreviewModal(mod);
                });
                descEl.appendChild(shortText);
                descEl.appendChild(readMoreBtn);
            }
            footer.appendChild(descEl);
        }

        if (((mod.tags || []) || []).length > 0) {
            var tagRow = document.createElement('div');
            tagRow.style.cssText = 'display:flex;flex-wrap:wrap;gap:5px;margin-top:8px;margin-bottom:4px;';
            (mod.tags || []).slice(0, 4).forEach(function (tag) {
                var t = document.createElement('span');
                t.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;background:rgba(255,255,255,0.06);border:1px solid rgba(255,255,255,0.14);color:rgba(255,255,255,0.7);line-height:1.5;';
                t.textContent = tag;
                tagRow.appendChild(t);
            });
            footer.appendChild(tagRow);
        }

        if ((mod.permissions || []).length > 0) {
            var permRow = document.createElement('div');
            permRow.style.cssText = 'display:flex;flex-wrap:wrap;gap:5px;margin-top:8px;';
            var permColors = {
                'http': '#ffa726',
                'jellyfin.read': '#29b6f6',
                'jellyfin.write': '#f44336',
                'jellyfin.delete': '#d32f2f',
                'jellyfin.tasks': '#fb8c00',
                'jellyfin.refresh': '#ab47bc',
                'jellyfin.livetv': '#26a69a',
                'jellyfin.admin': '#e53935',
                'store': '#66bb6a',
                'shared-store': '#43a047',
                'scheduler': '#7e57c2',
                'webhooks': '#00acc1',
                'rpc': '#8d6e63',
                'bus': '#78909c',
                'filesystem': '#ffb74d',
                'os': '#9575cd',
                'db': '#4fc3f7',
                'db.shared': '#0288d1'
            };
            (mod.permissions || []).forEach(function (p) {
                var badge = document.createElement('span');
                var col = permColors[p] || '#90a4ae';
                badge.style.cssText = 'display:inline-flex;align-items:center;gap:4px;padding:2px 7px;border-radius:4px;font-size:10px;font-weight:700;letter-spacing:0.03em;background:' + col + '22;border:1px solid ' + col + '66;color:' + col + ';line-height:1.5;';
                badge.innerHTML = '<svg width="10" height="10" viewBox="0 0 24 24" fill="currentColor"><path d="M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z"/></svg>' + escHtml(p);
                badge.title = 'Permission required: ' + p;
                permRow.appendChild(badge);
            });
            footer.appendChild(permRow);
        }

        if ((mod.requires || []).length > 0) {
            var reqEl = document.createElement('div');
            reqEl.style.cssText = 'margin-top:8px;font-size:0.75rem;opacity:0.7;';
            var missingDeps = (mod.requires || []).filter(function (dep) {
                return !currentIds().includes(dep);
            });
            if (missingDeps.length > 0) {
                reqEl.style.color = '#ffa726';
                reqEl.textContent = 'Requires: ' + missingDeps.join(', ') + ' (will be auto-enabled)';
            } else {
                reqEl.textContent = '\u21b3 Requires: ' + (mod.requires || []).join(', ');
            }
            footer.appendChild(reqEl);
        }

        var btnRow = document.createElement('div');
        btnRow.style.cssText = 'display:flex;gap:8px;margin-top:auto;padding-top:12px;padding-bottom:2px;';

        var toggleBtn = document.createElement('button');
        toggleBtn.type = 'button';
        toggleBtn.style.cssText = 'flex:1;padding:6px 12px;font-size:0.75rem;font-weight:600;letter-spacing:0.02857em;text-transform:uppercase;border-radius:6px;border:none;cursor:pointer;pointer-events:auto;transition:all 150ms;'
            + (isOn ? 'background:#90caf9;color:rgba(0,0,0,0.87);box-shadow:0 1px 3px rgba(0,0,0,0.3);' : 'background:rgba(255,255,255,0.08);color:rgba(255,255,255,0.87);border:1px solid rgba(255,255,255,0.16);');
        toggleBtn.textContent = isOn ? '\u2713 Enabled' : 'Enable';
        toggleBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            toggleMod(mod);
        });
        btnRow.appendChild(toggleBtn);

        var infoBtn = document.createElement('button');
        infoBtn.type = 'button';
        infoBtn.style.cssText = 'padding:6px 10px;font-size:0.85rem;border:1px solid rgba(255,255,255,0.2);border-radius:6px;background:rgba(255,255,255,0.06);color:rgba(255,255,255,0.87);cursor:pointer;pointer-events:auto;display:inline-flex;align-items:center;justify-content:center;transition:all 150ms;';
        infoBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></svg>';
        infoBtn.title = 'View details & preview';
        infoBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            openPreviewModal(mod);
        });
        btnRow.appendChild(infoBtn);

        if (isOn && hasVars) {
            var cfgBtn = document.createElement('button');
            cfgBtn.type = 'button';
            cfgBtn.style.cssText = 'padding:6px 10px;font-size:0.85rem;border:1px solid rgba(255,255,255,0.2);border-radius:6px;background:rgba(255,255,255,0.06);color:rgba(255,255,255,0.87);cursor:pointer;pointer-events:auto;display:inline-flex;align-items:center;justify-content:center;transition:all 150ms;';
            cfgBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>';
            cfgBtn.title = 'Configure variables';
            cfgBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                openVarsModal(mod);
            });
            btnRow.appendChild(cfgBtn);
        }

        if (isOn) {
            (function (m) {
                var editBtn = document.createElement('button');
                editBtn.type = 'button';
                editBtn.style.cssText = 'padding:6px 10px;font-size:0.85rem;border:1px solid rgba(255,255,255,0.2);border-radius:6px;background:rgba(255,255,255,0.06);color:rgba(255,255,255,0.87);cursor:pointer;pointer-events:auto;display:inline-flex;align-items:center;justify-content:center;transition:all 150ms;';
                editBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z"/></svg>';
                editBtn.title = 'Edit cached files';
                editBtn.addEventListener('click', function (e) {
                    e.stopPropagation();
                    openCacheEditor(m.id, m.name || m.id, 'mod');
                });
                btnRow.appendChild(editBtn);
            })(mod);
        }

        if ((mod.sourceUrl || "")) {
            var srcBtn = document.createElement('a');
            srcBtn.style.cssText = 'padding:6px 10px;font-size:0.85rem;border:1px solid rgba(255,255,255,0.2);border-radius:6px;background:rgba(255,255,255,0.06);color:rgba(255,255,255,0.87);cursor:pointer;text-decoration:none;display:inline-flex;align-items:center;pointer-events:auto;transition:all 150ms;';
            srcBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg>';
            srcBtn.title = 'View source';
            srcBtn.href = (mod.sourceUrl || "");
            srcBtn.target = '_blank';
            srcBtn.rel = 'noopener noreferrer';
            srcBtn.addEventListener('click', function (e) { e.stopPropagation(); });
            btnRow.appendChild(srcBtn);
        }

        footer.appendChild(btnRow);
        card.appendChild(footer);
        return card;
    }

    function openVarsModal(mod) {
        var vars = (mod.vars || []) || [];
        var saved = (state.pendingVars || state.modVars)[mod.id] || {};
        modalContext = mod;

        var existing = document.getElementById('mmModalOverlay');
        if (existing) existing.remove();

        var overlay = document.createElement('div');
        overlay.id = 'mmModalOverlay';
        overlay.style.cssText = [
            'position:fixed',
            'inset:0',
            'background:rgba(0,0,0,0.75)',
            'backdrop-filter:blur(6px)',
            '-webkit-backdrop-filter:blur(6px)',
            'z-index:100000',
            'display:flex',
            'align-items:center',
            'justify-content:center',
            'padding:20px',
            'box-sizing:border-box'
        ].join(';');

        var dialog = document.createElement('div');
        dialog.style.cssText = [
            'background:#1e1e1e',
            'border:1px solid rgba(255,255,255,0.12)',
            'border-radius:16px',
            'padding:28px',
            'width:100%',
            'max-width:480px',
            'box-shadow:0px 11px 15px -7px rgba(0,0,0,0.2),0px 24px 38px 3px rgba(0,0,0,0.14),0px 9px 46px 8px rgba(0,0,0,0.12)',
            'position:relative',
            'overflow-y:auto',
            'max-height:90vh',
            'box-sizing:border-box'
        ].join(';');

        var title = document.createElement('div');
        title.style.cssText = 'font-size:1.25rem;font-weight:600;margin-bottom:6px;color:rgba(255,255,255,0.87);';
        title.textContent = (mod.name || "");
        dialog.appendChild(title);

        var subtitle = document.createElement('div');
        subtitle.style.cssText = 'font-size:0.8125rem;color:rgba(255,255,255,0.6);margin-bottom:24px;line-height:1.43;';
        subtitle.textContent = 'Configure variables. Leave blank to use the default.';
        dialog.appendChild(subtitle);

        var varContainer = document.createElement('div');
        varContainer.id = 'mmModalVars';
        vars.forEach(function (v) {
            var row = document.createElement('div');
            row.style.marginBottom = '20px';

            var lbl = document.createElement('div');
            lbl.style.cssText = 'font-size:0.875rem;font-weight:500;margin-bottom:4px;color:rgba(255,255,255,0.87);';
            lbl.textContent = (v.name || v.key || "") || v.key;
            row.appendChild(lbl);

            if ((v.description || "")) {
                var desc = document.createElement('div');
                desc.style.cssText = 'font-size:0.75rem;color:rgba(255,255,255,0.55);margin-bottom:8px;line-height:1.4;';
                desc.textContent = (v.description || "");
                row.appendChild(desc);
            }

            var inputType = ((v.type || "text") || 'text').toLowerCase();
            var input;

            if (inputType === 'boolean') {
                var trueVal = (v.trueValue !== undefined ? v.trueValue : "true") !== undefined ? (v.trueValue !== undefined ? v.trueValue : "true") : 'true';
                var falseVal = (v.falseValue !== undefined ? v.falseValue : "false") !== undefined ? (v.falseValue !== undefined ? v.falseValue : "false") : 'false';
                var savedBool = saved[v.key] !== undefined ? saved[v.key] : ((v.default || "") === 'true' || (v.default || "") === trueVal ? trueVal : falseVal);
                input = document.createElement('select');
                [['true', trueVal], ['false', falseVal]].forEach(function (pair) {
                    var o = document.createElement('option');
                    o.value = pair[1]; o.textContent = pair[0];
                    input.appendChild(o);
                });
                input.value = savedBool;
                input.name = 'mm-var-' + v.key;
                input.id = 'mm-var-' + v.key;
                input.style.cssText = 'width:100%;padding:10px 14px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.23);border-radius:8px;color:#fff;font-size:0.875rem;font-family:inherit;box-sizing:border-box;appearance:none;-webkit-appearance:none;cursor:pointer;';
                input.dataset.varKey = v.key;
                row.appendChild(input);
            } else if (inputType === 'color') {
                var colorWidget = buildColorInput(
                    saved[v.key] !== undefined ? saved[v.key] : null,
                    (v.default || "") || '#000000',
                    (v.allowGradient !== false) !== false
                );
                colorWidget.querySelector('input[type=hidden]').dataset.varKey = v.key;
                row.appendChild(colorWidget);
            } else {
                input = document.createElement('input');
                input.type = inputType === 'number' ? 'number' : 'text';
                input.value = saved[v.key] !== undefined ? saved[v.key] : ((v.default || "") || '');
                input.placeholder = (v.default || "") || '';
                input.name = 'mm-var-' + v.key;
                input.id = 'mm-var-' + v.key;
                input.style.cssText = 'width:100%;padding:10px 14px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.23);border-radius:8px;color:inherit;font-size:0.875rem;font-family:inherit;box-sizing:border-box;';
                input.dataset.varKey = v.key;
                row.appendChild(input);
            }
            varContainer.appendChild(row);
        });
        dialog.appendChild(varContainer);

        var actions = document.createElement('div');
        actions.style.cssText = 'display:flex;gap:12px;margin-top:24px;justify-content:flex-end;border-top:1px solid rgba(255,255,255,0.08);padding-top:16px;';

        var cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.textContent = 'Cancel';
        cancelBtn.style.cssText = 'padding:7px 18px;border-radius:8px;border:1px solid rgba(255,255,255,0.23);background:transparent;color:rgba(255,255,255,0.87);cursor:pointer;font-size:0.875rem;font-weight:600;text-transform:uppercase;letter-spacing:0.02857em;transition:all 150ms;';
        cancelBtn.addEventListener('click', closeModal);

        var saveBtn = document.createElement('button');
        saveBtn.type = 'button';
        saveBtn.textContent = 'Save';
        saveBtn.style.cssText = 'padding:8px 22px;border-radius:8px;border:none;background:#90caf9;color:rgba(0,0,0,0.87);cursor:pointer;font-size:0.875rem;font-weight:600;text-transform:uppercase;letter-spacing:0.02857em;box-shadow:0px 3px 1px -2px rgba(0,0,0,0.2),0px 2px 2px 0px rgba(0,0,0,0.14),0px 1px 5px 0px rgba(0,0,0,0.12);transition:all 150ms;';
        saveBtn.addEventListener('click', saveModalVars);

        actions.appendChild(cancelBtn);
        actions.appendChild(saveBtn);
        dialog.appendChild(actions);

        overlay.appendChild(dialog);
        overlay.addEventListener('click', function (e) { if (e.target === overlay) closeModal(); });
        document.body.appendChild(overlay);
    }

    function closeModal() {
        var overlay = document.getElementById('mmModalOverlay');
        if (overlay) overlay.remove();
        modalContext = null;
    }

    function openPreviewModal(item) {
        var existing = document.getElementById('smPreviewOverlay');
        if (existing) existing.remove();

        var shots = [];
        if (item.previewUrl) shots.push(item.previewUrl);
        if (item.screenshots && item.screenshots.length > 0) {
            for (var si = 0; si < item.screenshots.length; si++) {
                shots.push(item.screenshots[si]);
            }
        }
        var isGallery = shots.length > 1;
        var idx = 0;
        var desc = getLocalizedDescription(item);
        var isOn = currentIds().indexOf(item.id) !== -1;
        var hasVars = ((item.vars || []) || []).length > 0;
        var jfConstraint = item.jellyfin || (item.files && item.files[0] && item.files[0].jellyfin) || 'Any';
        var modType = getModType(item);

        var overlay = document.createElement('div');
        overlay.id = 'smPreviewOverlay';
        overlay.style.cssText = [
            'position:fixed', 'inset:0', 'background:rgba(0,0,0,0.85)',
            'backdrop-filter:blur(6px)', '-webkit-backdrop-filter:blur(6px)',
            'z-index:100001', 'display:flex', 'align-items:center',
            'justify-content:center', 'padding:20px', 'cursor:pointer'
        ].join(';');

        var dialog = document.createElement('div');
        dialog.style.cssText = [
            'background:#1e1e1e',
            'border:1px solid rgba(255,255,255,0.12)',
            'border-radius:16px',
            'width:100%',
            'max-width:720px',
            'max-height:90vh',
            'display:flex',
            'flex-direction:column',
            'overflow:hidden',
            'box-shadow:0px 11px 15px -7px rgba(0,0,0,0.4),0px 24px 38px 3px rgba(0,0,0,0.28),0px 9px 46px 8px rgba(0,0,0,0.24)',
            'cursor:default',
            'position:relative'
        ].join(';');

        var closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.textContent = '\u2715';
        closeBtn.style.cssText = [
            'position:absolute', 'top:12px', 'right:12px',
            'z-index:10', 'background:rgba(0,0,0,0.65)',
            'border:1px solid rgba(255,255,255,0.2)', 'border-radius:50%',
            'width:32px', 'height:32px',
            'color:rgba(255,255,255,0.87)', 'cursor:pointer',
            'font-size:0.9rem', 'line-height:1',
            'display:flex', 'align-items:center', 'justify-content:center',
            'transition:background-color 150ms'
        ].join(';');
        closeBtn.addEventListener('click', function (e) { e.stopPropagation(); overlay.remove(); });
        dialog.appendChild(closeBtn);

        var imgArea = document.createElement('div');
        imgArea.style.cssText = [
            'position:relative',
            'background:#121212',
            'flex-shrink:0',
            'overflow:hidden',
            'border-radius:16px 16px 0 0',
            'max-height:48vh',
            'min-height:200px',
            'display:flex',
            'align-items:center',
            'justify-content:center'
        ].join(';');

        var imgEl;
        if (shots.length > 0) {
            imgEl = document.createElement('img');
            imgEl.style.cssText = 'display:block;width:100%;max-height:48vh;object-fit:contain;pointer-events:none;';
            imgEl.alt = item.name || '';
            imgEl.src = shots[0];
        } else {
            imgEl = document.createElement('div');
            imgEl.style.cssText = 'width:100%;height:200px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;opacity:.4;';
            var noIcon = document.createElement('div');
            noIcon.style.cssText = 'color:#90caf9;display:flex;align-items:center;justify-content:center;';
            noIcon.innerHTML = '<svg width="48" height="48" viewBox="0 0 24 24" fill="currentColor"><path d="M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z"/></svg>';
            var noText = document.createElement('div');
            noText.style.cssText = 'font-size:.82em;letter-spacing:.05em;';
            noText.textContent = 'No preview available';
            imgEl.appendChild(noIcon);
            imgEl.appendChild(noText);
        }
        imgArea.appendChild(imgEl);

        var dotContainer = null;
        if (isGallery) {
            var ARROW_BASE = [
                'position:absolute', 'top:50%', 'transform:translateY(-50%)',
                'background:rgba(0,0,0,0.65)', 'border:1px solid rgba(255,255,255,0.2)', 'color:#fff',
                'font-size:1.6em', 'line-height:1', 'width:40px', 'height:60px',
                'border-radius:6px', 'cursor:pointer', 'z-index:2',
                'display:flex', 'align-items:center', 'justify-content:center',
                'transition:opacity .15s'
            ].join(';');

            var prevBtn = document.createElement('button');
            prevBtn.type = 'button';
            prevBtn.style.cssText = ARROW_BASE + ';left:8px;';
            prevBtn.innerHTML = '&#8249;';
            prevBtn.title = 'Previous (left arrow key)';

            var nextBtn = document.createElement('button');
            nextBtn.type = 'button';
            nextBtn.style.cssText = ARROW_BASE + ';right:8px;';
            nextBtn.innerHTML = '&#8250;';
            nextBtn.title = 'Next (right arrow key)';

            dotContainer = document.createElement('div');
            dotContainer.style.cssText = 'display:flex;gap:6px;justify-content:center;padding:10px 0 4px;flex-shrink:0;background:#1a1d21;';

            function renderDots() {
                dotContainer.innerHTML = '';
                for (var di = 0; di < shots.length; di++) {
                    (function (i) {
                        var dot = document.createElement('button');
                        dot.type = 'button';
                        var active = i === idx;
                        dot.style.cssText = [
                            'width:' + (active ? '20px' : '8px'),
                            'height:8px', 'border-radius:4px', 'border:none',
                            'background:' + (active ? '#90caf9' : 'rgba(255,255,255,0.3)'),
                            'cursor:pointer', 'padding:0', 'transition:all .2s'
                        ].join(';');
                        dot.addEventListener('click', function (e) {
                            e.stopPropagation();
                            idx = i;
                            updateGallery();
                        });
                        dotContainer.appendChild(dot);
                    })(di);
                }
            }

            function updateGallery() {
                imgEl.src = shots[idx];
                prevBtn.style.opacity = idx === 0 ? '0.25' : '0.8';
                nextBtn.style.opacity = idx === shots.length - 1 ? '0.25' : '0.8';
                renderDots();
            }

            prevBtn.addEventListener('click', function (e) { e.stopPropagation(); if (idx > 0) { idx--; updateGallery(); } });
            nextBtn.addEventListener('click', function (e) { e.stopPropagation(); if (idx < shots.length - 1) { idx++; updateGallery(); } });
            [prevBtn, nextBtn].forEach(function (btn) {
                btn.addEventListener('mouseenter', function () { this.style.opacity = '1'; });
                btn.addEventListener('mouseleave', function () { updateGallery(); });
            });

            document.addEventListener('keydown', function onGalleryKey(e) {
                if (!document.getElementById('smPreviewOverlay')) { document.removeEventListener('keydown', onGalleryKey); return; }
                if (e.key === 'ArrowLeft' && idx > 0) { idx--; updateGallery(); }
                if (e.key === 'ArrowRight' && idx < shots.length - 1) { idx++; updateGallery(); }
            });

            imgArea.appendChild(prevBtn);
            imgArea.appendChild(nextBtn);
            updateGallery();
        }

        dialog.appendChild(imgArea);
        if (dotContainer) dialog.appendChild(dotContainer);

        var infoPanel = document.createElement('div');
        infoPanel.style.cssText = [
            'overflow-y:auto',
            'padding:20px 24px 20px',
            'flex:1',
            'min-height:0'
        ].join(';');

        // Header Title
        var titleEl = document.createElement('div');
        titleEl.style.cssText = 'font-size:1.35rem;font-weight:700;color:rgba(255,255,255,0.92);margin-bottom:6px;line-height:1.25;letter-spacing:-0.01em;';
        titleEl.textContent = getLocalizedTitle(item);
        infoPanel.appendChild(titleEl);

        // Badges row
        var badgeRow = document.createElement('div');
        badgeRow.style.cssText = 'display:flex;flex-wrap:wrap;align-items:center;gap:6px;margin-bottom:14px;';

        if (item.version) {
            var verPill = document.createElement('span');
            verPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:600;background:rgba(255,255,255,0.08);border:1px solid rgba(255,255,255,0.16);color:rgba(255,255,255,0.87);';
            verPill.textContent = 'v' + item.version;
            badgeRow.appendChild(verPill);
        }

        if (jfConstraint) {
            var jfModalPill = document.createElement('span');
            jfModalPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:600;background:rgba(144,202,249,0.14);border:1px solid rgba(144,202,249,0.35);color:#90caf9;';
            jfModalPill.textContent = 'Jellyfin ' + jfConstraint;
            badgeRow.appendChild(jfModalPill);
        }

        var typePill = document.createElement('span');
        typePill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:600;background:rgba(255,255,255,0.06);border:1px solid rgba(255,255,255,0.14);color:rgba(255,255,255,0.8);text-transform:uppercase;';
        typePill.textContent = modType + ' Mod';
        badgeRow.appendChild(typePill);

        if (item.author) {
            var authorPill = document.createElement('span');
            authorPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:500;background:rgba(255,255,255,0.05);color:rgba(255,255,255,0.7);';
            authorPill.textContent = 'by ' + item.author;
            badgeRow.appendChild(authorPill);
        }

        var createdDateStr = formatDate(item.createdAt);
        if (createdDateStr) {
            var createdPill = document.createElement('span');
            createdPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;background:rgba(255,255,255,0.04);color:rgba(255,255,255,0.55);';
            createdPill.textContent = 'Created ' + createdDateStr;
            badgeRow.appendChild(createdPill);
        }

        var updatedDateStr = formatDate(item.updatedAt || item.date || (item.files && item.files[0] && item.files[0].date));
        if (updatedDateStr && updatedDateStr !== createdDateStr) {
            var updatedPill = document.createElement('span');
            updatedPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;background:rgba(255,255,255,0.04);color:rgba(255,255,255,0.55);';
            updatedPill.textContent = 'Updated ' + updatedDateStr;
            badgeRow.appendChild(updatedPill);
        }

        if ((item.tags || []).length > 0) {
            (item.tags || []).forEach(function (tag) {
                var tagPill = document.createElement('span');
                tagPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;background:rgba(255,255,255,0.05);border:1px solid rgba(255,255,255,0.1);color:rgba(255,255,255,0.65);';
                tagPill.textContent = tag;
                badgeRow.appendChild(tagPill);
            });
        }

        infoPanel.appendChild(badgeRow);

        if (desc) {
            var descEl = document.createElement('div');
            descEl.style.cssText = 'font-size:0.875rem;line-height:1.65;color:rgba(255,255,255,0.78);white-space:pre-wrap;word-break:break-word;margin-bottom:18px;';
            descEl.textContent = desc;
            infoPanel.appendChild(descEl);
        }

        // Files & Components section
        if (Array.isArray(item.files) && item.files.length > 0) {
            var filesHeader = document.createElement('div');
            filesHeader.style.cssText = 'font-size:0.8125rem;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#90caf9;margin:18px 0 8px;display:flex;align-items:center;gap:6px;';
            filesHeader.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z"/></svg>Mod Files &amp; Components (' + item.files.length + ')';
            infoPanel.appendChild(filesHeader);

            var filesList = document.createElement('div');
            filesList.style.cssText = 'display:flex;flex-direction:column;gap:6px;margin-bottom:18px;';
            item.files.forEach(function (f) {
                var fCard = document.createElement('div');
                fCard.style.cssText = 'padding:8px 12px;border-radius:8px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.08);display:flex;flex-direction:column;gap:4px;';

                var fRow = document.createElement('div');
                fRow.style.cssText = 'display:flex;align-items:center;gap:8px;flex-wrap:wrap;';

                var fType = document.createElement('span');
                fType.style.cssText = 'padding:1px 6px;border-radius:3px;font-size:10px;font-weight:700;letter-spacing:0.03em;background:rgba(144,202,249,0.15);color:#90caf9;border:1px solid rgba(144,202,249,0.3);';
                fType.textContent = (f.type || 'JS').toUpperCase();
                fRow.appendChild(fType);

                if (f.version) {
                    var fVer = document.createElement('span');
                    fVer.style.cssText = 'font-size:11px;font-weight:600;color:rgba(255,255,255,0.85);';
                    fVer.textContent = 'v' + f.version;
                    fRow.appendChild(fVer);
                }

                if (f.jellyfin) {
                    var fJf = document.createElement('span');
                    fJf.style.cssText = 'font-size:11px;color:rgba(255,255,255,0.6);';
                    fJf.textContent = 'JF ' + f.jellyfin;
                    fRow.appendChild(fJf);
                }

                if (f.date) {
                    var fDate = document.createElement('span');
                    fDate.style.cssText = 'font-size:11px;color:rgba(255,255,255,0.45);margin-left:auto;';
                    fDate.textContent = formatDate(f.date);
                    fRow.appendChild(fDate);
                }

                fCard.appendChild(fRow);

                if (f.changelog) {
                    var fNote = document.createElement('div');
                    fNote.style.cssText = 'font-size:11px;color:rgba(255,255,255,0.65);line-height:1.4;';
                    fNote.textContent = typeof f.changelog === 'string' ? f.changelog : JSON.stringify(f.changelog);
                    fCard.appendChild(fNote);
                }

                if (f.url) {
                    var fUrl = document.createElement('div');
                    fUrl.style.cssText = 'font-size:10px;color:rgba(255,255,255,0.4);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-family:monospace;';
                    fUrl.textContent = f.url;
                    fCard.appendChild(fUrl);
                }

                filesList.appendChild(fCard);
            });
            infoPanel.appendChild(filesList);
        }

        // Permissions & Capabilities section
        if (Array.isArray(item.permissions) && item.permissions.length > 0) {
            var permHeader = document.createElement('div');
            permHeader.style.cssText = 'font-size:0.8125rem;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#ffa726;margin:18px 0 8px;display:flex;align-items:center;gap:6px;';
            permHeader.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z"/></svg>Permissions &amp; Capabilities (' + item.permissions.length + ')';
            infoPanel.appendChild(permHeader);

            var permGrid = document.createElement('div');
            permGrid.style.cssText = 'display:flex;flex-wrap:wrap;gap:6px;margin-bottom:18px;';
            item.permissions.forEach(function (p) {
                var pBadge = document.createElement('span');
                pBadge.style.cssText = 'display:inline-flex;align-items:center;gap:5px;padding:3px 8px;border-radius:6px;font-size:11px;font-weight:600;background:rgba(255,167,38,0.12);border:1px solid rgba(255,167,38,0.35);color:#ffa726;';
                pBadge.innerHTML = '<svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><path d="M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z"/></svg>' + escHtml(p);
                permGrid.appendChild(pBadge);
            });
            infoPanel.appendChild(permGrid);
        }

        // Prerequisites & Requirements
        if ((item.requires && item.requires.length > 0) || (item.preconnect && item.preconnect.length > 0)) {
            var reqHeader = document.createElement('div');
            reqHeader.style.cssText = 'font-size:0.8125rem;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#90caf9;margin:18px 0 8px;display:flex;align-items:center;gap:6px;';
            reqHeader.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z"/></svg>Prerequisites &amp; External Integrations';
            infoPanel.appendChild(reqHeader);

            var reqBox = document.createElement('div');
            reqBox.style.cssText = 'padding:10px 14px;border-radius:8px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.08);margin-bottom:18px;font-size:12px;color:rgba(255,255,255,0.75);display:flex;flex-direction:column;gap:6px;';

            if (item.requires && item.requires.length > 0) {
                var reqLine = document.createElement('div');
                reqLine.innerHTML = '<strong>Required mods:</strong> ' + item.requires.map(function (r) { return '<code style="background:rgba(255,255,255,0.08);padding:1px 4px;border-radius:3px;">' + escHtml(r) + '</code>'; }).join(', ');
                reqBox.appendChild(reqLine);
            }

            if (item.preconnect && item.preconnect.length > 0) {
                var pcLine = document.createElement('div');
                pcLine.innerHTML = '<strong>Preconnect domains:</strong> ' + item.preconnect.map(function (p) { return '<code style="background:rgba(255,255,255,0.08);padding:1px 4px;border-radius:3px;">' + escHtml(p) + '</code>'; }).join(', ');
                reqBox.appendChild(pcLine);
            }

            infoPanel.appendChild(reqBox);
        }

        // Configurable variables section
        if (Array.isArray(item.vars) && item.vars.length > 0) {
            var varsHeader = document.createElement('div');
            varsHeader.style.cssText = 'font-size:0.8125rem;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#90caf9;margin:18px 0 8px;display:flex;align-items:center;gap:6px;';
            varsHeader.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>Configurable Variables (' + item.vars.length + ')';
            infoPanel.appendChild(varsHeader);

            var varsList = document.createElement('div');
            varsList.style.cssText = 'display:flex;flex-direction:column;gap:6px;margin-bottom:18px;';
            item.vars.forEach(function (v) {
                var vCard = document.createElement('div');
                vCard.style.cssText = 'padding:8px 12px;border-radius:8px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.08);display:flex;flex-direction:column;gap:3px;';

                var vRow = document.createElement('div');
                vRow.style.cssText = 'display:flex;align-items:center;gap:8px;';

                var vName = document.createElement('span');
                vName.style.cssText = 'font-size:12px;font-weight:600;color:rgba(255,255,255,0.9);font-family:monospace;';
                vName.textContent = v.name;
                vRow.appendChild(vName);

                var vType = document.createElement('span');
                vType.style.cssText = 'padding:1px 5px;border-radius:3px;font-size:10px;background:rgba(255,255,255,0.06);color:rgba(255,255,255,0.6);';
                vType.textContent = v.type || 'text';
                vRow.appendChild(vType);

                if (v.default !== undefined) {
                    var vDef = document.createElement('span');
                    vDef.style.cssText = 'font-size:11px;color:rgba(255,255,255,0.45);margin-left:auto;';
                    vDef.textContent = 'default: ' + v.default;
                    vRow.appendChild(vDef);
                }

                vCard.appendChild(vRow);

                var vDesc = getLocalized(v.label || v.description);
                if (vDesc) {
                    var vDescEl = document.createElement('div');
                    vDescEl.style.cssText = 'font-size:11px;color:rgba(255,255,255,0.65);';
                    vDescEl.textContent = vDesc;
                    vCard.appendChild(vDescEl);
                }

                varsList.appendChild(vCard);
            });
            infoPanel.appendChild(varsList);
        }

        // Changelog section
        if (item.changelog && typeof item.changelog === 'object' && Object.keys(item.changelog).length > 0) {
            var clHeader = document.createElement('div');
            clHeader.style.cssText = 'font-size:0.8125rem;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#90caf9;margin:18px 0 8px;display:flex;align-items:center;gap:6px;';
            clHeader.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-5 14H7v-2h7v2zm3-4H7v-2h10v2zm0-4H7V7h10v2z"/></svg>Version History';
            infoPanel.appendChild(clHeader);

            var clList = document.createElement('div');
            clList.style.cssText = 'display:flex;flex-direction:column;gap:6px;margin-bottom:12px;';
            Object.keys(item.changelog).forEach(function (verKey) {
                var clCard = document.createElement('div');
                clCard.style.cssText = 'padding:8px 12px;border-radius:8px;background:rgba(255,255,255,0.03);border:1px solid rgba(255,255,255,0.07);display:flex;flex-direction:column;gap:2px;';

                var clVer = document.createElement('span');
                clVer.style.cssText = 'font-size:11px;font-weight:700;color:#90caf9;';
                clVer.textContent = 'v' + verKey;
                clCard.appendChild(clVer);

                var clNote = document.createElement('div');
                clNote.style.cssText = 'font-size:11px;color:rgba(255,255,255,0.7);line-height:1.4;';
                clNote.textContent = item.changelog[verKey];
                clCard.appendChild(clNote);

                clList.appendChild(clCard);
            });
            infoPanel.appendChild(clList);
        }

        dialog.appendChild(infoPanel);

        // Actions Footer
        var footerArea = document.createElement('div');
        footerArea.style.cssText = 'padding:14px 24px;border-top:1px solid rgba(255,255,255,0.1);background:#1a1a1a;display:flex;align-items:center;justify-content:flex-end;gap:10px;flex-shrink:0;';

        if (item.sourceUrl) {
            var modalSrcBtn = document.createElement('a');
            modalSrcBtn.href = item.sourceUrl;
            modalSrcBtn.target = '_blank';
            modalSrcBtn.rel = 'noopener noreferrer';
            modalSrcBtn.style.cssText = 'padding:8px 14px;border-radius:8px;border:1px solid rgba(255,255,255,0.2);background:transparent;color:rgba(255,255,255,0.87);font-size:0.8125rem;font-weight:500;text-decoration:none;display:inline-flex;align-items:center;gap:6px;transition:background 150ms;margin-right:auto;';
            modalSrcBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg>Source';
            footerArea.appendChild(modalSrcBtn);
        }

        if (hasVars) {
            var modalCfgBtn = document.createElement('button');
            modalCfgBtn.type = 'button';
            modalCfgBtn.style.cssText = 'padding:8px 14px;border-radius:8px;border:1px solid rgba(255,255,255,0.2);background:rgba(255,255,255,0.06);color:rgba(255,255,255,0.87);font-size:0.8125rem;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:6px;transition:all 150ms;';
            modalCfgBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>Configure';
            modalCfgBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                overlay.remove();
                openVarsModal(item);
            });
            footerArea.appendChild(modalCfgBtn);
        }

        var modalToggleBtn = document.createElement('button');
        modalToggleBtn.type = 'button';
        modalToggleBtn.style.cssText = 'padding:8px 18px;border-radius:8px;border:none;font-size:0.8125rem;font-weight:700;letter-spacing:0.02857em;text-transform:uppercase;cursor:pointer;transition:all 150ms;'
            + (isOn
                ? 'background:rgba(255,255,255,0.12);color:#fff;border:1px solid rgba(255,255,255,0.2);'
                : 'background:#90caf9;color:rgba(0,0,0,0.87);box-shadow:0px 2px 4px rgba(0,0,0,0.25);');
        modalToggleBtn.textContent = isOn ? '\u2713 Mod Enabled' : 'Enable Mod';
        modalToggleBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            overlay.remove();
            toggleMod(item);
        });
        footerArea.appendChild(modalToggleBtn);

        dialog.appendChild(footerArea);
        overlay.appendChild(dialog);

        overlay.addEventListener('click', function () { overlay.remove(); });
        dialog.addEventListener('click', function (e) { e.stopPropagation(); });
        document.addEventListener('keydown', function onKey(e) {
            if (e.key === 'Escape') { overlay.remove(); document.removeEventListener('keydown', onKey); }
        });

        document.body.appendChild(overlay);
    }

    function saveModalVars() {
        if (!modalContext) return closeModal();
        var mod = modalContext;
        var container = document.getElementById('mmModalVars');
        var values = {};
        container.querySelectorAll('input[type=hidden][data-var-key]').forEach(function (input) {
            values[input.dataset.varKey] = input.value;
        });
        container.querySelectorAll('[data-var-key]').forEach(function (input) {
            if (input.type === 'hidden') return;
            if (!values.hasOwnProperty(input.dataset.varKey)) {
                values[input.dataset.varKey] = input.type === 'checkbox'
                    ? (input.checked ? 'true' : 'false')
                    : input.value;
            }
        });

        var pv = JSON.parse(JSON.stringify(state.pendingVars || state.modVars));
        pv[mod.id] = values;
        state.pendingVars = pv;

        el('mm-btnSave').disabled = false;
        updatePending();
        closeModal();
        render();
    }

    function toggleMod(mod) {
        var ids = currentIds().slice();
        var vars = (mod.vars || []) || [];
        var i = ids.indexOf(mod.id);

        if (i !== -1) {
            ids.splice(i, 1);
            state.pendingIds = ids;
            el('mm-btnSave').disabled = false;
            updatePending();
            updateActiveBar();
            render();
        } else {
            var missing = (mod.requires || []).filter(function (dep) {
                return !ids.includes(dep);
            });

            if (missing.length > 0) {
                var notFound = missing.filter(function (dep) {
                    return !state.mods.some(function (m) { return m.id === dep; });
                });
                if (notFound.length > 0) {
                    alert('Cannot enable "' + (mod.name || mod.id) + '".\n\nThe following required mods are not available in the current repository:\n  ' + notFound.join('\n  '));
                    return;
                }
                for (var d = 0; d < missing.length; d++) {
                    var depId = missing[d];
                    if (!ids.includes(depId)) {
                        ids.push(depId);
                    }
                }
            }

            ids.push(mod.id);
            state.pendingIds = ids;
            el('mm-btnSave').disabled = false;
            updatePending();
            updateActiveBar();
            render();
            if (vars.length > 0) {
                openVarsModal(mod);
            }
        }
    }

    function currentIds() {
        return state.pendingIds !== null ? state.pendingIds : state.enabledIds;
    }

    function currentVars() {
        return state.pendingVars !== null ? state.pendingVars : state.modVars;
    }

    function updateActiveBar() {
        var n = currentIds().length;
        el('mm-activeBar').style.display = n > 0 ? 'flex' : 'none';
        el('activeCount').textContent = n;
    }

    function updatePending() {
        var p = el('mm-pendingLabel');
        var ids = currentIds();
        if (state.pendingIds !== null || state.pendingVars !== null) {
            p.textContent = ids.length + ' mod(s) pending \u2014 unsaved';
            p.style.color = '#90caf9';
        } else {
            p.textContent = ids.length > 0 ? ids.length + ' mod(s) active' : 'No pending changes';
            p.style.color = '';
        }
    }

    function doSave() {
        if (state.mods.length === 0) {
            Dashboard.alert('Please click Load Mods before saving.');
            return;
        }
        var ids = currentIds();
        var vars = currentVars();
        var cfg = Object.assign({}, state.config, {
            ModsUrl: el('modsUrl').value.trim(),
            EnabledMods: ids,
            CachedMods: JSON.stringify(state.mods),
            ModVars: JSON.stringify(vars)
        });
        el('mm-btnSave').disabled = true;
        ApiClient.updatePluginConfiguration(PLUGIN_ID, cfg).then(function (result) {
            Dashboard.processPluginConfigurationUpdateResult(result);
            state.config = cfg;
            state.enabledIds = ids;
            state.modVars = vars;
            state.pendingIds = null;
            state.pendingVars = null;
            updatePending();
            updateActiveBar();
            render();
            var doReload = function () { setTimeout(function () { window.top.location.reload(true); }, 300); };
            if (window.caches) {
                caches.keys().then(function (keys) {
                    return Promise.all(
                        keys.filter(function (k) { return k.startsWith('modmanager-'); })
                            .map(function (k) { return caches.delete(k); })
                    );
                }).then(doReload).catch(doReload);
            } else {
                doReload();
            }
        }).catch(function (err) {
            el('mm-btnSave').disabled = false;
            Dashboard.alert('Save failed: ' + err.message);
        });
    }

    var mmPageEl = document.querySelector('.jfModsConfigPage');
    if (mmPageEl) {
        mmPageEl.addEventListener('viewshow', loadConfig);
    }

    el('mm-btnLoad').addEventListener('click', function () {
        var url = el('modsUrl').value.trim();
        if (url) loadMods(url);
    });
    el('modsUrl').addEventListener('input', updateModSourceStatus);
    el('mm-searchInput').addEventListener('input', function () { state.query = this.value; render(); });
    var sortSelect = el('mm-sortSelect');
    if (sortSelect) {
        sortSelect.addEventListener('change', function () { state.sortBy = this.value; render(); });
    }
    el('mm-btnSave').addEventListener('click', doSave);
    el('btnDisableAll').addEventListener('click', function () {
        state.pendingIds = [];
        el('mm-btnSave').disabled = false;
        updatePending();
        updateActiveBar();
        render();
    });
    el('chkDebug').addEventListener('change', function () {
        var cfg = Object.assign({}, state.config, { DebugLogging: this.checked });
        ApiClient.updatePluginConfiguration(PLUGIN_ID, cfg).then(function () {
            state.config = cfg;
        });
    });

    var TAB_PANELS = { marketplace: 'tabMarketplace', health: 'tabHealth', settings: 'mm-tabSettings' };
    function switchTab(name) {
        Object.keys(TAB_PANELS).forEach(function (k) {
            var panel = document.getElementById(TAB_PANELS[k]);
            if (panel) panel.style.display = k === name ? '' : 'none';
        });
        var mmRoot = document.querySelector('.jfModsConfigPage') || document;
        mmRoot.querySelectorAll('.mmTab').forEach(function (btn) {
            var tabKey = btn.getAttribute('data-tab') || btn.dataset.tab;
            var active = tabKey === name;
            btn.classList.toggle('mmTabActive', active);
            btn.classList.toggle('mui-tab-active', active);
            btn.setAttribute('aria-selected', active ? 'true' : 'false');
            btn.setAttribute('tabindex', active ? '0' : '-1');
        });
        if (name === 'health') loadHealth();
        if (name === 'settings') {
            var vEl = document.getElementById('pluginVersionLabel');
            if (vEl) vEl.textContent = 'Plugin ID: ' + PLUGIN_ID;
            loadCacheInfo();
        }
    }

    document.querySelectorAll('.mmTab').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var tabKey = this.getAttribute('data-tab') || this.dataset.tab;
            if (tabKey) switchTab(tabKey);
        });
    });
    var selectedCacheModIds = [];

    function formatCacheDate(d) {
        if (!d || isNaN(d.getTime()) || d.getTime() === 0) return '\u2014';
        var now = new Date();
        var diffMs = now - d;
        var diffMin = Math.floor(diffMs / 60000);
        var diffHr = Math.floor(diffMin / 60);
        var diffDay = Math.floor(diffHr / 24);
        var rel = '';
        if (diffMin < 1) rel = 'just now';
        else if (diffMin < 60) rel = diffMin + 'm ago';
        else if (diffHr < 24) rel = diffHr + 'h ago';
        else if (diffDay < 30) rel = diffDay + 'd ago';
        else rel = d.toLocaleDateString();
        return rel + ' (' + d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) + ')';
    }

    function loadCacheInfo() {
        var infoBar = document.getElementById('cacheInfoBar');
        var modList = document.getElementById('cacheModList');
        var purgeSel = document.getElementById('btnPurgeSelected');
        infoBar.textContent = 'Loading\u2026';
        modList.innerHTML = '';
        selectedCacheModIds = [];
        if (purgeSel) purgeSel.disabled = true;

        ApiClient.ajax({ url: ApiClient.getUrl('JellyFrame/api/mods/cache'), type: 'GET', dataType: 'json' })
            .then(function (data) {
                var kb = (data.totalBytes / 1024).toFixed(1);
                infoBar.textContent = data.fileCount + ' file(s) \u00b7 ' + kb + ' KB  \u2014  ' + data.cacheDir;

                var grouped = {};
                (data.entries || []).forEach(function (e) {
                    var key = e.modId || 'unknown';
                    if (!grouped[key]) grouped[key] = [];
                    grouped[key].push(e);
                });

                if (Object.keys(grouped).length === 0) {
                    modList.innerHTML = '<div style="font-size:0.8125rem;color:rgba(255,255,255,0.5);padding:8px 0;">Cache is empty.</div>';
                    return;
                }

                Object.keys(grouped).sort().forEach(function (modId) {
                    var files = grouped[modId];
                    var modKb = (files.reduce(function (s, f) { return s + f.sizeBytes; }, 0) / 1024).toFixed(1);
                    var latestTimestamp = Math.max.apply(null, files.map(function (f) {
                        return f.modified ? new Date(f.modified).getTime() : 0;
                    }));
                    var latestDate = latestTimestamp > 0 ? new Date(latestTimestamp) : null;

                    var row = document.createElement('div');
                    row.style.cssText = 'display:flex;align-items:center;gap:12px;padding:10px 14px;border-radius:8px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.1);cursor:pointer;user-select:none;transition:background-color 150ms;';

                    var chk = document.createElement('input');
                    chk.type = 'checkbox';
                    chk.style.cssText = 'flex-shrink:0;accent-color:#90caf9;cursor:pointer;width:16px;height:16px;';
                    chk.addEventListener('change', function () {
                        if (chk.checked) {
                            if (!selectedCacheModIds.includes(modId)) selectedCacheModIds.push(modId);
                        } else {
                            selectedCacheModIds = selectedCacheModIds.filter(function (x) { return x !== modId; });
                        }
                        if (purgeSel) purgeSel.disabled = selectedCacheModIds.length === 0;
                    });

                    var label = document.createElement('div');
                    label.style.cssText = 'flex:1;min-width:0;display:flex;flex-direction:column;gap:3px;';

                    var distinctTypes = [];
                    files.forEach(function (f) {
                        var t = (f.type || 'js').toUpperCase();
                        if (!distinctTypes.includes(t)) distinctTypes.push(t);
                    });
                    var badgesHtml = distinctTypes.map(function (t) {
                        return '<span style="display:inline-block;padding:1px 6px;border-radius:4px;background:rgba(144,202,249,0.12);color:#90caf9;font-size:0.7rem;font-weight:600;">' + escHtml(t) + '</span>';
                    }).join(' ');

                    var topRow = '<div style="display:flex;align-items:center;flex-wrap:wrap;gap:6px;">'
                        + '<span style="font-weight:600;font-size:0.875rem;color:rgba(255,255,255,0.87);">' + escHtml(modId) + '</span>'
                        + '<span style="color:rgba(255,255,255,0.5);font-size:0.75rem;">v' + escHtml(files[0].version || '1.0.0') + '</span>'
                        + badgesHtml
                        + '</div>';

                    var bottomRow = '<div style="font-size:0.75rem;color:rgba(255,255,255,0.5);display:flex;align-items:center;gap:12px;flex-wrap:wrap;">'
                        + '<span>Cached: ' + escHtml(formatCacheDate(latestDate)) + '</span>'
                        + '<span>' + files.length + ' file' + (files.length > 1 ? 's' : '') + '</span>'
                        + '</div>';

                    label.innerHTML = topRow + bottomRow;

                    var size = document.createElement('div');
                    size.style.cssText = 'font-size:0.8125rem;font-weight:500;color:rgba(255,255,255,0.7);flex-shrink:0;font-variant-numeric:tabular-nums;';
                    size.textContent = modKb + ' KB';

                    row.appendChild(chk);
                    row.appendChild(label);
                    row.appendChild(size);
                    row.addEventListener('click', function (e) {
                        if (e.target !== chk) chk.checked = !chk.checked;
                        chk.dispatchEvent(new Event('change'));
                    });
                    modList.appendChild(row);
                });
            })
            .catch(function () {
                infoBar.textContent = 'Failed to load cache info.';
            });
    }

    function doPurge(modIds) {
        var resultEl = document.getElementById('cacheActionResult');
        var body = modIds && modIds.length > 0 ? JSON.stringify({ modIds: modIds }) : '{}';
        ApiClient.ajax({
            url: ApiClient.getUrl('JellyFrame/api/mods/cache/purge'),
            type: 'POST',
            data: body,
            contentType: 'application/json',
            dataType: 'json'
        }).then(function (res) {
            if (resultEl) {
                resultEl.style.display = '';
                resultEl.textContent = '\u2713 Purged ' + res.deleted + ' file(s). Cache will rebuild on next load.';
                setTimeout(function () { resultEl.style.display = 'none'; }, 4000);
            }
            selectedCacheModIds = [];
            loadCacheInfo();
        }).catch(function () {
            if (resultEl) {
                resultEl.style.display = '';
                resultEl.style.color = '#e06060';
                resultEl.textContent = '\u2717 Purge failed. Check that you are logged in as admin.';
            }
        });
    }

    document.getElementById('btnPurgeAll').addEventListener('click', function () {
        if (confirm('Purge the entire cache? All mods will re-download on next page load.'))
            doPurge([]);
    });
    document.getElementById('btnPurgeSelected').addEventListener('click', function () {
        if (selectedCacheModIds.length === 0) return;
        if (confirm('Purge cache for: ' + selectedCacheModIds.join(', ') + '?'))
            doPurge(selectedCacheModIds);
    });
    document.getElementById('btnRefreshCacheInfo').addEventListener('click', loadCacheInfo);

    function loadHealth() {
        var grid = document.getElementById('healthGrid');
        var summary = document.getElementById('pluginHealthSummary');
        grid.innerHTML = '<div class="stateMsg" style="opacity:.5;font-size:.9em">Loading health data\u2026</div>';

        ApiClient.ajax({ url: ApiClient.getUrl('JellyFrame/api/health'), type: 'GET', dataType: 'json' })
            .then(function (data) {
                summary.innerHTML = '';
                var chips = [
                    ['Loaded server mods', data.plugin.loadedServerMods],
                    ['Enabled mods', data.plugin.enabledMods],
                    ['Cached mods', data.plugin.totalCachedMods],
                    ['Hot-reload watcher', data.plugin.watcherActive ? 'active' : 'off']
                ];
                chips.forEach(function (c) {
                    var chip = document.createElement('div');
                    chip.style.cssText = 'padding:10px 18px;border-radius:12px;background:#1e1e1e;border:1px solid rgba(255,255,255,0.12);font-size:0.875rem;display:flex;flex-direction:column;align-items:center;gap:4px;min-width:110px;box-shadow:0px 2px 1px -1px rgba(0,0,0,0.2),0px 1px 1px 0px rgba(0,0,0,0.14),0px 1px 3px 0px rgba(0,0,0,0.12);';
                    chip.innerHTML = '<span style="font-size:1.35rem;font-weight:700;line-height:1.2;color:#90caf9;">' + c[1] + '</span><span style="opacity:0.6;font-size:0.75rem;text-align:center;text-transform:uppercase;letter-spacing:0.02em;">' + c[0] + '</span>';
                    summary.appendChild(chip);
                });

                grid.innerHTML = '';
                if (!data.mods || data.mods.length === 0) {
                    grid.innerHTML = '<div class="stateMsg" style="opacity:.5;font-size:.9em">No server mods loaded.</div>';
                    return;
                }
                data.mods.forEach(function (mod) {
                    var card = document.createElement('div');
                    card.style.cssText = 'padding:16px 20px;border-radius:12px;background:#1e1e1e;border:1px solid rgba(255,255,255,0.12);box-shadow:0px 2px 1px -1px rgba(0,0,0,0.2),0px 1px 1px 0px rgba(0,0,0,0.14),0px 1px 3px 0px rgba(0,0,0,0.12);';

                    var statusDot = mod.loaded
                        ? '<span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:#66bb6a;margin-right:8px;vertical-align:middle;"></span>'
                        : '<span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:#f44336;margin-right:8px;vertical-align:middle;"></span>';

                    var loadedAt = mod.loadedAt ? new Date(mod.loadedAt).toLocaleTimeString() : '\u2014';

                    var metrics = [
                        ['Routes', mod.routeCount],
                        ['Scheduler', mod.schedulerTasks],
                        ['Cache entries', mod.cacheEntries],
                        ['Store keys', mod.storeKeys],
                        ['User store', mod.userStoreUsers + ' user(s)'],
                        ['Bus subscriptions', mod.busSubscriptions != null ? mod.busSubscriptions : '\u2014'],
                        ['KV keys', mod.kvKeys != null ? mod.kvKeys : '\u2014'],
                        ['DB tables', (mod.dbTables && mod.dbTables.length) ? mod.dbTables.join(', ') : '\u2014'],
                        ['Shared DB tables', (mod.sharedDbTables && mod.sharedDbTables.length) ? mod.sharedDbTables.join(', ') : '\u2014'],
                        ['Webhooks', (mod.registeredWebhooks || []).join(', ') || '\u2014'],
                        ['RPC methods', (mod.rpcMethods || []).join(', ') || '\u2014'],
                        ['Permissions', (mod.permissions || []).join(', ') || 'none'],
                        ['Requires', (mod.requires || []).join(', ') || 'none']
                    ];

                    var metricHtml = metrics.map(function (m) {
                        return '<div style="display:flex;justify-content:space-between;padding:6px 0;border-bottom:1px solid rgba(255,255,255,0.06);font-size:0.8125rem;">'
                            + '<span style="opacity:0.6;">' + m[0] + '</span>'
                            + '<span style="font-variant-numeric:tabular-nums;color:rgba(255,255,255,0.87);">' + m[1] + '</span>'
                            + '</div>';
                    }).join('');

                    var crashHtml = '';
                    if (mod.crashCount || mod.restartOnCrash) {
                        var crashColor = mod.crashCount ? '#f44336' : 'rgba(255,255,255,0.4)';
                        var lastCrash = mod.lastCrashAt ? new Date(mod.lastCrashAt).toLocaleString() : 'never';
                        var nextRestart = mod.nextRestartAt ? new Date(mod.nextRestartAt).toLocaleString() : '\u2014';
                        crashHtml = '<div style="margin-top:12px;padding:10px 14px;border-radius:8px;background:rgba(244,67,54,0.08);border:1px solid rgba(244,67,54,0.25);font-size:0.8125rem;">'
                            + '<div style="font-weight:600;color:' + crashColor + ';margin-bottom:6px;">Crash / Restart</div>'
                            + '<div style="display:flex;justify-content:space-between;padding:3px 0;"><span style="opacity:.6;">Crashes</span><span>' + (mod.crashCount || 0) + '</span></div>'
                            + '<div style="display:flex;justify-content:space-between;padding:3px 0;"><span style="opacity:.6;">Last crash</span><span>' + escHtml(lastCrash) + '</span></div>'
                            + (mod.lastError ? '<div style="padding:6px 0;opacity:.7;word-break:break-word;color:#f44336;">' + escHtml(mod.lastError) + '</div>' : '')
                            + '<div style="display:flex;justify-content:space-between;padding:3px 0;"><span style="opacity:.6;">Restart on crash</span><span>' + (mod.restartOnCrash ? 'yes' : 'no') + '</span></div>'
                            + (mod.restartOnCrash && mod.nextRestartAt ? '<div style="display:flex;justify-content:space-between;padding:3px 0;"><span style="opacity:.6;">Next restart</span><span>' + escHtml(nextRestart) + '</span></div>' : '')
                            + '</div>';
                    }

                    card.innerHTML = '<div style="display:flex;justify-content:space-between;align-items:baseline;margin-bottom:12px;">'
                        + '<span style="font-weight:600;font-size:1.0rem;color:rgba(255,255,255,0.87);">' + statusDot + escHtml(mod.name || mod.id) + '</span>'
                        + '<span style="font-size:0.75rem;opacity:0.5;">loaded ' + loadedAt + '</span>'
                        + '</div>'
                        + metricHtml
                        + crashHtml;

                    grid.appendChild(card);
                });
            })
            .catch(function () {
                grid.innerHTML = '<div class="stateMsg" style="color:#e06060;font-size:.9em">Failed to load health data. Are you logged in as admin?</div>';
            });
    }

    document.getElementById('btnRefreshHealth').addEventListener('click', loadHealth);

    var _pendingUpdateIds = [];
    var _pendingChangelogs = [];

    function checkWhatsNew(freshMods) {
        var cached = [];
        try { cached = JSON.parse(state.config.CachedMods || '[]'); } catch (e) { }

        var cachedById = {};
        cached.forEach(function (m) { cachedById[m.id] = m.version; });

        var updatedLabels = [];
        var updatedIds = [];
        var changelogs = [];
        freshMods.forEach(function (m) {
            var prev = cachedById[m.id];
            if (prev && prev !== m.version) {
                var title = getLocalizedTitle(m) || m.id;
                updatedLabels.push(title + ' (' + prev + ' \u2192 ' + m.version + ')');
                updatedIds.push(m.id);
                if (m.changelog && m.changelog.length > 0) {
                    changelogs.push({ id: m.id, name: title, prevVersion: prev, newVersion: m.version, entries: m.changelog });
                }
            }
        });

        var bar = document.getElementById('whatsNewBar');
        var text = document.getElementById('whatsNewText');
        if (updatedLabels.length > 0) {
            _pendingUpdateIds = updatedIds;
            _pendingChangelogs = changelogs;
            text.textContent = 'Updated: ' + updatedLabels.join(' \u00b7 ');
            bar.style.display = 'flex';
            var clBtn = document.getElementById('btnWhatsNewChangelog');
            if (clBtn) clBtn.style.display = changelogs.length > 0 ? '' : 'none';
        } else {
            if (bar) bar.style.display = 'none';
        }
    }

    document.getElementById('btnWhatsNewChangelog').addEventListener('click', function () {
        if (_pendingChangelogs && _pendingChangelogs.length > 0) {
            openChangelogModal(_pendingChangelogs, 'mod');
        }
    });

    document.getElementById('btnWhatsNewUpdate').addEventListener('click', function () {
        if (!_pendingUpdateIds.length) return;
        var btn = document.getElementById('btnWhatsNewUpdate');
        btn.disabled = true;
        btn.textContent = 'Updating\u2026';

        var modsUrl = el('modsUrl').value.trim();
        var manifestPromise = modsUrl
            ? fetch(modsUrl).then(function (r) { return r.ok ? r.json() : null; }).catch(function () { return null; })
            : Promise.resolve(null);

        manifestPromise.then(function (freshMods) {
            var configToSave = Object.assign({}, state.config);
            if (freshMods && freshMods.length > 0) {
                var existing = [];
                try { existing = JSON.parse(state.config.CachedMods || '[]'); } catch (e) { existing = []; }
                var freshById = {};
                freshMods.forEach(function (m) { freshById[m.id] = m; });
                var merged = existing.map(function (m) {
                    return freshById[m.id] ? freshById[m.id] : m;
                });
                freshMods.forEach(function (m) {
                    if (!merged.some(function (e) { return e.id === m.id; })) {
                        merged.push(m);
                    }
                });
                configToSave.CachedMods = JSON.stringify(merged);
                state.mods = merged;
            }

            return ApiClient.updatePluginConfiguration(PLUGIN_ID, configToSave).then(function () {
                state.config = configToSave;
                return ApiClient.ajax({
                    url: ApiClient.getUrl('JellyFrame/api/mods/cache/purge'),
                    type: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify({ modIds: _pendingUpdateIds })
                });
            });
        }).then(function () {
            _pendingUpdateIds = [];
            document.getElementById('whatsNewBar').style.display = 'none';
            Dashboard.processPluginConfigurationUpdateResult();
        }).catch(function () {
            btn.disabled = false;
            btn.textContent = 'Update Now';
            Dashboard.alert('Update failed. Try saving the page manually and restarting Jellyfin.');
        });
    });

    document.getElementById('btnWhatsNewDismiss').addEventListener('click', function () {
        document.getElementById('whatsNewBar').style.display = 'none';
    });
})();
