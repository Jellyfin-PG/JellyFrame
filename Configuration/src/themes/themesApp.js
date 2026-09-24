(function () {
    var state = {
        config: {},
        themes: [],
        serverVersion: '',
        activeId: null,
        pendingId: null,
        pendingVars: null,
        themeVars: {},
        query: '',
        activeTags: [],
        sortBy: 'recently-updated'
    };

    function isThemeSupported(theme, serverVersion) {
        if (!serverVersion) return true;
        if (theme.jellyfin && !isVersionCompatible(theme.jellyfin, serverVersion)) {
            return false;
        }
        if (Array.isArray(theme.files) && theme.files.length > 0) {
            var hasComp = theme.files.some(function (f) {
                return isVersionCompatible(f.jellyfin || theme.jellyfin, serverVersion);
            });
            if (!hasComp) return false;
        }
        return true;
    }

    var _pendingThemeUpdateIds = [];
    var _pendingThemeChangelogs = [];

    function checkThemeWhatsNew(freshThemes) {
        var cached = [];
        try { cached = JSON.parse(state.config.CachedThemes || '[]'); } catch (e) { }

        var cachedById = {};
        cached.forEach(function (t) { cachedById[t.id] = t.version; });

        var updatedLabels = [];
        var updatedIds = [];
        var changelogs = [];
        freshThemes.forEach(function (t) {
            var prev = cachedById[t.id];
            if (prev && prev !== t.version) {
                var title = getLocalizedTitle(t) || t.id;
                updatedLabels.push(title + ' (' + prev + ' \u2192 ' + t.version + ')');
                updatedIds.push(t.id);
                if (t.changelog && t.changelog.length > 0) {
                    changelogs.push({ id: t.id, name: title, prevVersion: prev, newVersion: t.version, entries: t.changelog });
                }
            }
        });

        var bar = el('jftWhatsNewBar');
        var text = el('jftWhatsNewText');
        if (updatedLabels.length > 0) {
            _pendingThemeUpdateIds = updatedIds;
            _pendingThemeChangelogs = changelogs;
            text.textContent = 'Updated: ' + updatedLabels.join(' \u00b7 ');
            bar.style.display = 'flex';
            var clBtn = document.getElementById('btnJftWhatsNewChangelog');
            if (clBtn) clBtn.style.display = changelogs.length > 0 ? '' : 'none';
        } else {
            if (bar) bar.style.display = 'none';
        }
    }

    document.getElementById('btnJftWhatsNewChangelog').addEventListener('click', function () {
        if (_pendingThemeChangelogs && _pendingThemeChangelogs.length > 0) {
            openChangelogModal(_pendingThemeChangelogs, 'theme');
        }
    });

    document.getElementById('btnJftWhatsNewUpdate').addEventListener('click', function () {
        if (!_pendingThemeUpdateIds.length) return;
        var btn = document.getElementById('btnJftWhatsNewUpdate');
        btn.disabled = true;
        btn.textContent = 'Updating\u2026';

        var themesUrl = el('themesUrl').value.trim();
        var manifestPromise = themesUrl
            ? fetch(themesUrl).then(function (r) { return r.ok ? r.json() : null; }).catch(function () { return null; })
            : Promise.resolve(null);

        manifestPromise.then(function (freshThemes) {
            var configToSave = Object.assign({}, state.config);
            if (freshThemes && freshThemes.length > 0) {
                var existing = [];
                try { existing = JSON.parse(state.config.CachedThemes || '[]'); } catch (e) { existing = []; }
                var freshById = {};
                freshThemes.forEach(function (t) { freshById[t.id] = t; });
                var merged = existing.map(function (t) {
                    return freshById[t.id] ? freshById[t.id] : t;
                });
                freshThemes.forEach(function (t) {
                    if (!merged.some(function (e) { return e.id === t.id; })) {
                        merged.push(t);
                    }
                });
                configToSave.CachedThemes = JSON.stringify(merged);
                state.themes = merged;
            }

            return ApiClient.updatePluginConfiguration(PLUGIN_ID, configToSave).then(function () {
                state.config = configToSave;
                return ApiClient.ajax({
                    url: ApiClient.getUrl('JellyFrame/api/themes/cache/purge'),
                    type: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify({ themeIds: _pendingThemeUpdateIds })
                });
            });
        }).then(function () {
            _pendingThemeUpdateIds = [];
            el('jftWhatsNewBar').style.display = 'none';
            Dashboard.processPluginConfigurationUpdateResult();
        }).catch(function () {
            btn.disabled = false;
            btn.textContent = 'Update Now';
            Dashboard.alert('Update failed. Try saving the page manually and restarting Jellyfin.');
        });
    });

    document.getElementById('btnJftWhatsNewDismiss').addEventListener('click', function () {
        el('jftWhatsNewBar').style.display = 'none';
    });

    var OFFICIAL_THEMES_URL = 'https://cdn.jsdelivr.net/gh/Jellyfin-PG/JellyFrame-Resources@main/themes.v2.json';

    function updateThemeSourceStatus() {
        var url = (el('themesUrl').value || '').trim();
        var statusEl = document.getElementById('jft-sourceStatus');
        if (!statusEl) { return; }
        if (!url) { statusEl.style.display = 'none'; return; }
        statusEl.style.display = 'block';
        if (url === OFFICIAL_THEMES_URL) {
            statusEl.innerHTML = '<span style="color:#4ade80;font-weight:600;">Official JellyFrame repository</span>';
        } else {
            statusEl.innerHTML = '<span style="color:#fbbf24;font-weight:600;">Unofficial source</span><span style="opacity:.55;"> - only load repositories you trust</span>';
        }
    }

    function initThemesPage() {
        switchThemeTab('themes');
        fetchServerVersion().then(function (ver) {
            state.serverVersion = ver || '';
            if (state.themes && state.themes.length > 0) render();
        });
        ApiClient.getPluginConfiguration(PLUGIN_ID).then(function (cfg) {
            state.config = cfg;
            state.activeId = cfg.ActiveTheme || null;
            state.themeVars = safeParseJson(cfg.ThemeVars, {});

            el('themesUrl').value = cfg.ThemesUrl || OFFICIAL_THEMES_URL;
            updateThemeSourceStatus();
            el('jft-searchInput').value = '';

            updateActiveBar();

            if (cfg.CachedThemes) {
                try {
                    state.themes = JSON.parse(cfg.CachedThemes) || [];
                    if (state.themes.length > 0) {
                        buildTagFilters();
                        render();
                    }
                } catch (e) { }
            }
        });
    }

    var jftPageEl = document.querySelector('.jfThemePage');
    if (jftPageEl) {
        jftPageEl.addEventListener('viewshow', initThemesPage);
    }

    el('jft-btnLoad').addEventListener('click', function () {
        var url = el('themesUrl').value.trim();
        if (!url) return;
        loadThemes(url);
    });

    function loadThemes(url) {
        el('themeGrid').innerHTML = '<div class="stateMsg">Loading themes\u2026</div>';
        el('jft-tagFilters').innerHTML = '';

        fetch(url)
            .then(function (r) { return r.json(); })
            .then(function (themes) {
                state.themes = themes || [];
                buildTagFilters();
                render();
                checkThemeWhatsNew(themes || []);
            })
            .catch(function (err) {
                el('themeGrid').innerHTML = '<div class="stateMsg">Could not load themes: ' + escHtml(err.message) + '</div>';
            });
    }

    function buildTagFilters() {
        var set = {};
        state.themes.forEach(function (t) { (t.tags || []).forEach(function (g) { set[g] = true; }); });
        var tags = Object.keys(set).sort();
        state.activeTags = [];
        var c = el('jft-tagFilters');
        c.innerHTML = '';
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
        var q = (state.query || '').toLowerCase();
        var list = state.themes.filter(function (t) {
            if (!isThemeSupported(t, state.serverVersion)) return false;
            var match = !q ||
                getLocalizedTitle(t).toLowerCase().includes(q) ||
                (t.author || '').toLowerCase().includes(q) ||
                getLocalizedDescription(t).toLowerCase().includes(q);
            var tagMatch = state.activeTags.length === 0 ||
                state.activeTags.every(function (tag) { return (t.tags || []).includes(tag); });
            return match && tagMatch;
        });

        list = sortItems(list, state.sortBy || 'recently-updated');

        var grid = el('themeGrid');
        if (list.length === 0) {
            grid.innerHTML = '<div class="stateMsg">No themes match your search or current server version (' + escHtml(state.serverVersion || 'all') + ').</div>';
            return;
        }
        grid.innerHTML = '';
        list.forEach(function (t) { grid.appendChild(buildCard(t)); });
    }

    el('themesUrl').addEventListener('input', updateThemeSourceStatus);
    el('jft-searchInput').addEventListener('input', function () { state.query = this.value; render(); });
    var jftSortSelect = el('jft-sortSelect');
    if (jftSortSelect) {
        jftSortSelect.addEventListener('change', function () { state.sortBy = this.value; render(); });
    }

    function buildCard(theme) {
        var effectiveId = state.pendingId !== null ? state.pendingId : state.activeId;
        var isActive = effectiveId === theme.id;
        var hasVars = (theme.vars || []).length > 0 || (theme.addons || []).length > 0;
        var card = document.createElement('div');
        card.className = 'card mui-card' + (isActive ? ' activeRing' : '') + (theme.editorsChoice ? ' editorsChoiceCard' : '');

        var imgContainer = document.createElement('div');
        imgContainer.className = 'cardImageContainer coveredImage';

        imgContainer.style.cursor = 'zoom-in';
        imgContainer.style.pointerEvents = 'auto';
        var _shotCount = (theme.previewUrl ? 1 : 0)
            + (theme.screenshots ? theme.screenshots.length : 0);
        imgContainer.title = _shotCount > 1 ? 'Click to view gallery (' + _shotCount + ' images)' : 'Click to preview';
        imgContainer.addEventListener('click', function (e) {
            e.stopPropagation();
            openPreviewModal(theme);
        });
        if (theme.previewUrl) {
            var img = document.createElement('img');
            img.src = theme.previewUrl;
            img.alt = theme.name || '';
            img.onerror = function () {
                img.remove();
                var fb = document.createElement('div');
                fb.className = 'cardImageFallback';
                fb.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;min-height:160px;display:flex;align-items:center;justify-content:center;opacity:.25;pointer-events:none;color:#90caf9;';
                fb.innerHTML = '<svg width="44" height="44" viewBox="0 0 24 24" fill="currentColor"><path d="M12 3c-4.97 0-9 4.03-9 9 0 2.12.74 4.07 1.97 5.61L4.35 19.4c-.39.39-.39 1.02 0 1.41.39.39 1.02.39 1.41 0l1.9-1.9C9.36 19.64 10.63 20 12 20c4.97 0 9-4.03 9-9s-4.03-9-9-9zm0 15c-3.31 0-6-2.69-6-6s2.69-6 6-6 6 2.69 6 6-2.69 6-6 6z"/></svg>';
                imgContainer.appendChild(fb);
            };
            imgContainer.appendChild(img);
        } else {
            var fb = document.createElement('div');
            fb.className = 'cardImageFallback';
            fb.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;min-height:160px;display:flex;align-items:center;justify-content:center;opacity:.25;pointer-events:none;color:#90caf9;';
            fb.innerHTML = '<svg width="44" height="44" viewBox="0 0 24 24" fill="currentColor"><path d="M12 3c-4.97 0-9 4.03-9 9 0 2.12.74 4.07 1.97 5.61L4.35 19.4c-.39.39-.39 1.02 0 1.41.39.39 1.02.39 1.41 0l1.9-1.9C9.36 19.64 10.63 20 12 20c4.97 0 9-4.03 9-9s-4.03-9-9-9zm0 15c-3.31 0-6-2.69-6-6s2.69-6 6-6 6 2.69 6 6-2.69 6-6 6z"/></svg>';
            imgContainer.appendChild(fb);
        }

        var jfConstraint = theme.jellyfin || (theme.files && theme.files[0] && theme.files[0].jellyfin) || '';
        var badge = document.createElement('div');
        badge.className = 'themeTypeBadge';
        badge.style.pointerEvents = 'none';
        badge.textContent = (theme.addons || []).length > 0 ? 'CSS+' : 'CSS';
        imgContainer.appendChild(badge);

        if (isActive) {
            var chk = document.createElement('div');
            chk.className = 'activeCheck';
            chk.textContent = '\u2713';
            chk.style.pointerEvents = 'none';
            imgContainer.appendChild(chk);
        }

        if (hasVars) {
            var varsBadge = document.createElement('div');
            varsBadge.style.cssText = 'position:absolute;bottom:8px;left:8px;background:rgba(18,18,18,0.85);border:1px solid rgba(255,255,255,0.2);color:rgba(255,255,255,0.87);border-radius:4px;font-size:10px;font-weight:600;padding:2px 6px;z-index:1;letter-spacing:0.04em;pointer-events:none;backdrop-filter:blur(4px);';
            varsBadge.textContent = 'CONFIGURABLE';
            imgContainer.appendChild(varsBadge);
        }

        if (_shotCount > 1) {
            var shotsBadge = document.createElement('div');
            shotsBadge.style.cssText = 'position:absolute;bottom:8px;right:8px;background:rgba(18,18,18,0.85);border:1px solid rgba(255,255,255,0.2);color:rgba(255,255,255,0.87);border-radius:4px;font-size:10px;font-weight:600;padding:2px 6px;z-index:1;letter-spacing:0.04em;pointer-events:none;backdrop-filter:blur(4px);display:inline-flex;align-items:center;gap:3px;';
            shotsBadge.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M12 12m-3.2 0a3.2 3.2 0 1 0 6.4 0a3.2 3.2 0 1 0 -6.4 0M9 2L7.17 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2h-3.17L15 2H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z"/></svg>' + _shotCount;
            imgContainer.appendChild(shotsBadge);
        }

        if (theme.editorsChoice) {
            var ecBadge = document.createElement('div');
            ecBadge.style.cssText = 'position:absolute;top:8px;right:8px;z-index:3;background:linear-gradient(135deg,#ffa726,#f57c00);border:1px solid rgba(255,255,255,0.4);border-radius:4px;padding:2px 8px;font-size:10px;font-weight:700;letter-spacing:.05em;text-transform:uppercase;color:#000;backdrop-filter:blur(4px);-webkit-backdrop-filter:blur(4px);box-shadow:0 2px 6px rgba(245,124,0,0.5);pointer-events:none;display:inline-flex;align-items:center;gap:3px;';
            ecBadge.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z"/></svg>Editor\'s Choice';
            imgContainer.appendChild(ecBadge);
        }

        card.appendChild(imgContainer);

        var footer = document.createElement('div');
        footer.className = 'cardFooter';
        footer.style.cssText = 'padding:14px 16px;display:flex;flex-direction:column;flex:1;';

        var headerRow = document.createElement('div');
        headerRow.style.cssText = 'display:flex;align-items:flex-start;justify-content:space-between;gap:8px;margin-bottom:2px;';

        var nameEl = document.createElement('div');
        nameEl.className = 'cardText';
        nameEl.style.cssText = 'font-size:1.0rem;font-weight:600;color:rgba(255,255,255,0.87);line-height:1.3;letter-spacing:-0.01em;flex:1;';
        nameEl.textContent = getLocalizedTitle(theme);
        headerRow.appendChild(nameEl);

        if (jfConstraint) {
            var jfPill = document.createElement('span');
            jfPill.style.cssText = 'display:inline-flex;align-items:center;padding:1px 6px;border-radius:4px;font-size:10px;font-weight:600;background:rgba(144,202,249,0.12);border:1px solid rgba(144,202,249,0.3);color:#90caf9;white-space:nowrap;flex-shrink:0;';
            jfPill.textContent = 'JF ' + jfConstraint;
            headerRow.appendChild(jfPill);
        }
        footer.appendChild(headerRow);

        var metaParts = [];
        if (theme.author) metaParts.push('by ' + theme.author);
        if (theme.version) metaParts.push('v' + theme.version);
        var formattedUpdated = formatDate(theme.updatedAt || theme.date || (theme.files && theme.files[0] && theme.files[0].date));
        if (formattedUpdated) metaParts.push(formattedUpdated);

        if (metaParts.length > 0) {
            var authEl = document.createElement('div');
            authEl.className = 'cardText cardText-secondary';
            authEl.style.cssText = 'font-size:0.75rem;color:rgba(255,255,255,0.6);margin-bottom:6px;';
            authEl.textContent = metaParts.join(' \u2022 ');
            footer.appendChild(authEl);
        }

        var _themeDesc = getLocalizedDescription(theme);
        if (_themeDesc) {
            var descEl = document.createElement('div');
            descEl.className = 'cardText cardText-secondary';
            descEl.style.cssText = 'font-size:0.8125rem;color:rgba(255,255,255,0.6);line-height:1.43;margin-top:4px;white-space:normal;word-break:break-word;overflow-wrap:break-word;';
            var TRUNCATE_LEN = 100;
            if (_themeDesc.length <= TRUNCATE_LEN) {
                descEl.textContent = _themeDesc;
            } else {
                var shortText = document.createTextNode(_themeDesc.slice(0, TRUNCATE_LEN) + '\u2026 ');
                var readMoreBtn = document.createElement('button');
                readMoreBtn.type = 'button';
                readMoreBtn.textContent = 'more';
                readMoreBtn.style.cssText = 'background:none;border:none;padding:0;color:#90caf9;cursor:pointer;font-size:inherit;font-family:inherit;font-weight:500;pointer-events:auto;text-decoration:underline;';
                readMoreBtn.addEventListener('click', function (e) {
                    e.stopPropagation();
                    openPreviewModal(theme);
                });
                descEl.appendChild(shortText);
                descEl.appendChild(readMoreBtn);
            }
            footer.appendChild(descEl);
        }

        if ((theme.tags || []).length > 0) {
            var tagRow = document.createElement('div');
            tagRow.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;margin-top:8px;';
            (theme.tags || []).slice(0, 4).forEach(function (tag) {
                var t = document.createElement('span');
                t.style.cssText = 'display:inline-block;padding:2px 8px;border-radius:9999px;font-size:11px;font-weight:500;background:rgba(255,255,255,0.06);border:1px solid rgba(255,255,255,0.14);color:rgba(255,255,255,0.7);line-height:1.5;';
                t.textContent = tag;
                tagRow.appendChild(t);
            });
            footer.appendChild(tagRow);
        }

        if ((theme.addons || []).length > 0) {
            var addEl = document.createElement('div');
            addEl.style.cssText = 'margin-top:6px;font-size:0.75rem;color:rgba(255,255,255,0.5);font-weight:500;';
            addEl.textContent = (theme.addons || []).length + ' addon(s) available';
            footer.appendChild(addEl);
        }

        var btnRow = document.createElement('div');
        btnRow.style.cssText = 'display:flex;gap:6px;margin-top:auto;padding-top:12px;';

        var activateBtn = document.createElement('button');
        activateBtn.type = 'button';
        activateBtn.style.cssText = 'flex:1;padding:6px 14px;font-size:0.8125rem;font-weight:600;letter-spacing:0.02857em;text-transform:uppercase;border-radius:8px;border:none;cursor:pointer;transition:all 200ms cubic-bezier(0.4,0,0.2,1);'
            + (isActive
                ? 'background:#90caf9;color:rgba(0,0,0,0.87);box-shadow:0px 2px 1px -1px rgba(0,0,0,0.2),0px 1px 1px 0px rgba(0,0,0,0.14),0px 1px 3px 0px rgba(0,0,0,0.12);'
                : 'background:rgba(144,202,249,0.08);border:1px solid rgba(144,202,249,0.5);color:#90caf9;');
        activateBtn.textContent = isActive ? '\u2713 Active' : 'Apply';
        activateBtn.style.pointerEvents = 'auto';
        activateBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            selectTheme(theme);
        });
        btnRow.appendChild(activateBtn);

        var infoBtn = document.createElement('button');
        infoBtn.type = 'button';
        infoBtn.style.cssText = 'padding:6px 10px;font-size:0.875rem;border-radius:8px;border:1px solid rgba(255,255,255,0.23);background:rgba(255,255,255,0.04);color:rgba(255,255,255,0.87);cursor:pointer;pointer-events:auto;display:inline-flex;align-items:center;justify-content:center;transition:all 200ms;';
        infoBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></svg>';
        infoBtn.title = 'View details & full preview';
        infoBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            openPreviewModal(theme);
        });
        btnRow.appendChild(infoBtn);

        if (isActive && hasVars) {
            var cfgBtn = document.createElement('button');
            cfgBtn.type = 'button';
            cfgBtn.style.cssText = 'padding:6px 10px;font-size:0.875rem;border-radius:8px;border:1px solid rgba(255,255,255,0.23);background:rgba(255,255,255,0.04);color:rgba(255,255,255,0.87);cursor:pointer;pointer-events:auto;display:inline-flex;align-items:center;justify-content:center;transition:all 200ms;';
            cfgBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.57 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>';
            cfgBtn.title = 'Configure theme';
            cfgBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                openVarsModal(theme);
            });
            btnRow.appendChild(cfgBtn);
        }

        if (isActive) {
            (function (t) {
                var editBtn = document.createElement('button');
                editBtn.type = 'button';
                editBtn.style.cssText = 'padding:6px 10px;font-size:0.875rem;border-radius:8px;border:1px solid rgba(255,255,255,0.23);background:rgba(255,255,255,0.04);color:rgba(255,255,255,0.87);cursor:pointer;pointer-events:auto;display:inline-flex;align-items:center;justify-content:center;transition:all 200ms;';
                editBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z"/></svg>';
                editBtn.title = 'Edit cached files';
                editBtn.addEventListener('click', function (e) {
                    e.stopPropagation();
                    openCacheEditor(t.id, t.name || t.id, 'theme');
                });
                btnRow.appendChild(editBtn);
            })(theme);
        }

        if (theme.sourceUrl) {
            var srcBtn = document.createElement('a');
            srcBtn.style.cssText = 'padding:6px 10px;border-radius:8px;border:1px solid rgba(255,255,255,0.23);background:rgba(255,255,255,0.04);color:rgba(255,255,255,0.87);cursor:pointer;text-decoration:none;display:inline-flex;align-items:center;pointer-events:auto;transition:all 200ms;';
            srcBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg>';
            srcBtn.title = 'View source';
            srcBtn.href = theme.sourceUrl;
            srcBtn.target = '_blank';
            srcBtn.rel = 'noopener noreferrer';
            srcBtn.addEventListener('click', function (e) { e.stopPropagation(); });
            btnRow.appendChild(srcBtn);
        }

        footer.appendChild(btnRow);
        card.appendChild(footer);
        return card;
    }

    function selectTheme(theme) {
        var effectiveId = state.pendingId !== null ? state.pendingId : state.activeId;
        if (effectiveId === theme.id) return;

        state.pendingId = theme.id;
        el('jft-btnSave').disabled = false;
        updatePending();
        updateActiveBar();
        render();

        var hasVars = (theme.vars || []).length > 0 || (theme.addons || []).length > 0;
        if (hasVars) openVarsModal(theme);
    }

    function deactivateTheme() {
        state.pendingId = '';
        state.pendingVars = {};
        el('jft-btnSave').disabled = false;
        updatePending();
        updateActiveBar();
        render();
    }

    el('btnDeactivate').addEventListener('click', deactivateTheme);

    el('jft-btnSave').addEventListener('click', doSave);

    function doSave() {
        var newId = state.pendingId !== null ? state.pendingId : state.activeId;
        var newVars = state.pendingVars !== null ? state.pendingVars : state.themeVars;

        var cfg = Object.assign({}, state.config, {
            ThemesUrl: el('themesUrl').value.trim(),
            ActiveTheme: newId || '',
            CachedThemes: state.themes.length > 0 ? JSON.stringify(state.themes) : state.config.CachedThemes || '',
            ThemeVars: JSON.stringify(newVars || {})
        });

        el('jft-btnSave').disabled = true;
        ApiClient.updatePluginConfiguration(PLUGIN_ID, cfg).then(function () {
            state.config = cfg;
            state.activeId = newId;
            state.themeVars = newVars;
            state.pendingId = null;
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
        }).catch(function () {
            el('jft-btnSave').disabled = false;
        });
    }

    function currentId() { return state.pendingId !== null ? state.pendingId : state.activeId; }
    function currentVars() { return state.pendingVars !== null ? state.pendingVars : state.themeVars; }

    function updateActiveBar() {
        var id = currentId();
        var name = '';
        if (id) {
            var t = state.themes.find(function (x) { return x.id === id; });
            name = t ? getLocalizedTitle(t) : id;
        }
        el('jft-activeBar').style.display = id ? 'flex' : 'none';
        el('activeThemeName').textContent = name || '';
    }

    function updatePending() {
        var hasPending = state.pendingId !== null || state.pendingVars !== null;
        el('jft-pendingLabel').textContent = hasPending ? 'Unsaved changes' : 'No pending changes';
        el('jft-pendingLabel').style.opacity = hasPending ? '1' : '.6';
    }

    function openVarsModal(theme) {
        var saved = Object.assign({}, currentVars());

        var overlay = document.createElement('div');
        overlay.id = 'jfThemeModalOverlay';
        overlay.style.cssText = [
            'position:fixed', 'inset:0', 'background:rgba(0,0,0,0.7)',
            'backdrop-filter:blur(4px)', '-webkit-backdrop-filter:blur(4px)',
            'z-index:100000', 'display:flex', 'align-items:center',
            'justify-content:center', 'padding:24px'
        ].join(';') + ';';

        var dialog = document.createElement('div');
        dialog.style.cssText = 'background:#1e1e1e;border:1px solid rgba(255,255,255,0.12);border-radius:16px;padding:24px;width:100%;max-width:540px;max-height:85vh;overflow-y:auto;box-shadow:0px 11px 15px -7px rgba(0,0,0,0.2),0px 24px 38px 3px rgba(0,0,0,0.14),0px 9px 46px 8px rgba(0,0,0,0.12);color:rgba(255,255,255,0.87);font-family:inherit;';

        var title = document.createElement('h3');
        title.style.cssText = 'margin:0 0 6px;font-size:1.25rem;font-weight:600;color:rgba(255,255,255,0.87);line-height:1.3;';
        title.textContent = getLocalizedTitle(theme) + ' \u2014 Configuration';
        dialog.appendChild(title);

        var subtitle = document.createElement('p');
        subtitle.style.cssText = 'margin:0 0 20px;font-size:0.8125rem;color:rgba(255,255,255,0.6);line-height:1.43;';
        subtitle.textContent = 'Variables apply CSS custom properties. Addon toggles add extra stylesheets.';
        dialog.appendChild(subtitle);

        var regularVars = (theme.vars || []).filter(function (v) { return (v.type || 'text') !== 'boolean'; });
        if (regularVars.length > 0) {
            var varsSection = document.createElement('div');
            varsSection.style.cssText = 'display:flex;flex-direction:column;gap:16px;margin-bottom:20px;';

            regularVars.forEach(function (v) {
                var row = document.createElement('div');
                row.style.cssText = 'display:flex;flex-direction:column;gap:6px;';

                var label = document.createElement('label');
                label.htmlFor = 'tv-' + v.key;
                label.style.cssText = 'font-size:0.75rem;font-weight:600;color:rgba(255,255,255,0.7);text-transform:uppercase;letter-spacing:0.01em;';
                label.textContent = v.name || v.key;
                row.appendChild(label);

                if (v.description) {
                    var hint = document.createElement('div');
                    hint.style.cssText = 'font-size:0.75rem;color:rgba(255,255,255,0.5);margin-bottom:2px;line-height:1.3;';
                    hint.textContent = v.description;
                    row.appendChild(hint);
                }

                var inputType = (v.type || 'text').toLowerCase();
                if (inputType === 'color') {
                    var colorWidget = buildColorInput(
                        saved[v.key] !== undefined ? saved[v.key] : null,
                        v.default || '#000000',
                        v.allowGradient === true
                    );
                    colorWidget.querySelector('input[type=hidden]').dataset.varKey = v.key;
                    row.appendChild(colorWidget);
                } else {
                    var input = document.createElement('input');
                    input.type = inputType === 'number' ? 'number' : 'text';
                    input.id = 'tv-' + v.key;
                    input.value = saved[v.key] !== undefined ? saved[v.key] : (v.default || '');
                    input.style.cssText = 'padding:10px 14px;background-color:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.23);border-radius:8px;color:rgba(255,255,255,0.87);font-size:0.875rem;font-family:inherit;width:100%;box-sizing:border-box;transition:border-color 200ms;';
                    input.dataset.varKey = v.key;
                    row.appendChild(input);
                }
                varsSection.appendChild(row);
            });

            dialog.appendChild(varsSection);
        }

        if ((theme.addons || []).length > 0) {
            var addonsTitle = document.createElement('p');
            addonsTitle.style.cssText = 'margin:0 0 10px;font-size:0.75rem;font-weight:700;color:rgba(255,255,255,0.7);text-transform:uppercase;letter-spacing:0.06em;';
            addonsTitle.textContent = 'Addon Stylesheets';
            dialog.appendChild(addonsTitle);

            var addonsSection = document.createElement('div');
            addonsSection.style.cssText = 'display:flex;flex-direction:column;gap:8px;margin-bottom:20px;';

            (theme.addons || []).forEach(function (addon) {
                var varKey = addon.triggerVar || ('__addon__' + addon.id);

                var isChecked;
                if (addon.triggerVar) {
                    isChecked = saved[addon.triggerVar] === 'true' ||
                        (saved[addon.triggerVar] === undefined &&
                            (theme.vars || []).some(function (v) {
                                return v.key === addon.triggerVar && v.default === 'true';
                            }));
                } else {
                    isChecked = saved[varKey] === 'true';
                }

                var row = document.createElement('div');
                row.className = 'jfAddonToggle';
                row.dataset.varKey = varKey;
                row.dataset.checked = isChecked ? 'true' : 'false';
                row.style.cssText = 'display:flex;align-items:center;justify-content:space-between;gap:12px;padding:12px 16px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.12);border-radius:8px;cursor:pointer;user-select:none;transition:background-color 150ms;';

                var info = document.createElement('div');
                info.style.cssText = 'flex:1;min-width:0;';

                var addonName = document.createElement('div');
                addonName.style.cssText = 'font-size:0.875rem;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;color:rgba(255,255,255,0.87);margin:0;padding:0;';
                addonName.textContent = addon.name || addon.id;
                info.appendChild(addonName);

                if (addon.description) {
                    var addonDesc = document.createElement('div');
                    addonDesc.style.cssText = 'font-size:0.75rem;color:rgba(255,255,255,0.6);margin-top:2px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-bottom:0;padding:0;';
                    addonDesc.textContent = addon.description;
                    info.appendChild(addonDesc);
                }

                var pill = document.createElement('div');
                pill.style.cssText = [
                    'display:inline-block',
                    'flex-shrink:0',
                    'width:38px',
                    'height:20px',
                    'border-radius:10px',
                    'position:relative',
                    'transition:background 200ms cubic-bezier(0.4, 0, 0.2, 1)',
                    'cursor:pointer',
                    isChecked
                        ? 'background:#90caf9'
                        : 'background:rgba(255,255,255,0.38)'
                ].join(';');

                var knob = document.createElement('div');
                knob.style.cssText = [
                    'position:absolute',
                    'top:3px',
                    'width:14px',
                    'height:14px',
                    'border-radius:50%',
                    'background:#ffffff',
                    'box-shadow:0 1px 3px rgba(0,0,0,0.4)',
                    'transition:left 200ms cubic-bezier(0.4, 0, 0.2, 1)',
                    isChecked ? 'left:21px' : 'left:3px'
                ].join(';');
                pill.appendChild(knob);

                row.appendChild(info);
                row.appendChild(pill);

                row.addEventListener('click', function () {
                    var nowChecked = row.dataset.checked !== 'true';
                    row.dataset.checked = nowChecked ? 'true' : 'false';
                    pill.style.background = nowChecked ? '#90caf9' : 'rgba(255,255,255,0.38)';
                    knob.style.left = nowChecked ? '21px' : '3px';
                });

                addonsSection.appendChild(row);
            });

            dialog.appendChild(addonsSection);
        }

        var actions = document.createElement('div');
        actions.style.cssText = 'display:flex;justify-content:flex-end;gap:12px;margin-top:12px;';

        var cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.className = 'mui-btn-outlined';
        cancelBtn.textContent = 'Cancel';
        cancelBtn.style.cssText = 'padding:6px 18px;';
        cancelBtn.addEventListener('click', closeModal);

        var saveBtn = document.createElement('button');
        saveBtn.type = 'button';
        saveBtn.className = 'mui-btn-contained';
        saveBtn.textContent = 'Apply';
        saveBtn.style.cssText = 'padding:6px 20px;';
        saveBtn.addEventListener('click', function () {
            var newVars = Object.assign({}, currentVars());

            dialog.querySelectorAll('input[type=hidden][data-var-key]').forEach(function (input) {
                newVars[input.dataset.varKey] = input.value;
            });

            dialog.querySelectorAll('[data-var-key]').forEach(function (input) {
                if (input.type === 'hidden') return;
                if (input.tagName === 'DIV') return;
                if (!newVars.hasOwnProperty(input.dataset.varKey)) {
                    newVars[input.dataset.varKey] = input.value;
                }
            });

            dialog.querySelectorAll('.jfAddonToggle[data-var-key]').forEach(function (row) {
                newVars[row.dataset.varKey] = row.dataset.checked === 'true' ? 'true' : 'false';
            });

            state.pendingVars = newVars;
            el('jft-btnSave').disabled = false;
            updatePending();
            closeModal();
            render();
        });

        actions.appendChild(cancelBtn);
        actions.appendChild(saveBtn);
        dialog.appendChild(actions);

        overlay.appendChild(dialog);
        overlay.addEventListener('click', function (e) { if (e.target === overlay) closeModal(); });
        document.body.appendChild(overlay);
    }

    function closeModal() {
        var o = document.getElementById('jfThemeModalOverlay');
        if (o) o.remove();
    }

    function openPreviewModal(theme) {
        var existing = document.getElementById('jftPreviewOverlay');
        if (existing) existing.remove();

        var shots = [];
        if (theme.previewUrl) shots.push(theme.previewUrl);
        if (theme.screenshots && theme.screenshots.length > 0) {
            for (var si = 0; si < theme.screenshots.length; si++) {
                shots.push(theme.screenshots[si]);
            }
        }
        var isGallery = shots.length > 1;
        var idx = 0;
        var desc = getLocalizedDescription(theme);
        var effectiveId = state.pendingId !== null ? state.pendingId : state.activeId;
        var isActive = effectiveId === theme.id;
        var hasVars = (theme.vars || []).length > 0 || (theme.addons || []).length > 0;
        var jfConstraint = theme.jellyfin || (theme.files && theme.files[0] && theme.files[0].jellyfin) || 'Any';

        var overlay = document.createElement('div');
        overlay.id = 'jftPreviewOverlay';
        overlay.style.cssText = [
            'position:fixed', 'inset:0', 'background:rgba(0,0,0,0.85)',
            'backdrop-filter:blur(6px)', '-webkit-backdrop-filter:blur(6px)',
            'z-index:100001', 'display:flex', 'align-items:center',
            'justify-content:center', 'padding:24px', 'cursor:pointer'
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
            'transition:background 150ms ease'
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
            'min-height:220px',
            'display:flex',
            'align-items:center',
            'justify-content:center'
        ].join(';');

        var imgEl;
        if (shots.length > 0) {
            imgEl = document.createElement('img');
            imgEl.style.cssText = 'display:block;width:100%;max-height:48vh;object-fit:contain;pointer-events:none;';
            imgEl.alt = theme.name || '';
            imgEl.src = shots[0];
        } else {
            imgEl = document.createElement('div');
            imgEl.style.cssText = 'width:100%;height:220px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;opacity:.4;';
            var noIcon = document.createElement('div');
            noIcon.style.cssText = 'color:#90caf9;display:flex;align-items:center;justify-content:center;';
            noIcon.innerHTML = '<svg width="48" height="48" viewBox="0 0 24 24" fill="currentColor"><path d="M12 3c-4.97 0-9 4.03-9 9 0 2.12.74 4.07 1.97 5.61L4.35 19.4c-.39.39-.39 1.02 0 1.41.39.39 1.02.39 1.41 0l1.9-1.9C9.36 19.64 10.63 20 12 20c4.97 0 9-4.03 9-9s-4.03-9-9-9zm0 15c-3.31 0-6-2.69-6-6s2.69-6 6-6 6 2.69 6 6-2.69 6-6 6z"/></svg>';
            var noText = document.createElement('div');
            noText.style.cssText = 'font-size:0.8125rem;letter-spacing:0.04em;';
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
                'border-radius:8px', 'cursor:pointer', 'z-index:2',
                'display:flex', 'align-items:center', 'justify-content:center',
                'transition:opacity 200ms ease, background 200ms ease'
            ].join(';');

            var prevBtn = document.createElement('button');
            prevBtn.type = 'button';
            prevBtn.style.cssText = ARROW_BASE + ';left:12px;';
            prevBtn.innerHTML = '&#8249;';
            prevBtn.title = 'Previous';

            var nextBtn = document.createElement('button');
            nextBtn.type = 'button';
            nextBtn.style.cssText = ARROW_BASE + ';right:12px;';
            nextBtn.innerHTML = '&#8250;';
            nextBtn.title = 'Next';

            dotContainer = document.createElement('div');
            dotContainer.style.cssText = 'display:flex;gap:8px;justify-content:center;padding:12px 0 6px;flex-shrink:0;background:#1e1e1e;';

            function renderDots() {
                dotContainer.innerHTML = '';
                for (var di = 0; di < shots.length; di++) {
                    (function (i) {
                        var dot = document.createElement('button');
                        dot.type = 'button';
                        var active = i === idx;
                        dot.style.cssText = [
                            'width:' + (active ? '24px' : '8px'),
                            'height:8px', 'border-radius:4px', 'border:none',
                            'background:' + (active ? '#90caf9' : 'rgba(255,255,255,0.25)'),
                            'cursor:pointer', 'padding:0', 'transition:all 200ms cubic-bezier(0.4,0,0.2,1)'
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
                prevBtn.style.opacity = idx === 0 ? '0.25' : '0.85';
                nextBtn.style.opacity = idx === shots.length - 1 ? '0.25' : '0.85';
                renderDots();
            }

            prevBtn.addEventListener('click', function (e) { e.stopPropagation(); if (idx > 0) { idx--; updateGallery(); } });
            nextBtn.addEventListener('click', function (e) { e.stopPropagation(); if (idx < shots.length - 1) { idx++; updateGallery(); } });
            [prevBtn, nextBtn].forEach(function (btn) {
                btn.addEventListener('mouseenter', function () { this.style.opacity = '1'; });
                btn.addEventListener('mouseleave', function () { updateGallery(); });
            });

            document.addEventListener('keydown', function onGalleryKey(e) {
                if (!document.getElementById('jftPreviewOverlay')) { document.removeEventListener('keydown', onGalleryKey); return; }
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

        // Header row
        var titleEl = document.createElement('div');
        titleEl.style.cssText = 'font-size:1.35rem;font-weight:700;color:rgba(255,255,255,0.92);margin-bottom:6px;line-height:1.25;letter-spacing:-0.01em;';
        titleEl.textContent = getLocalizedTitle(theme);
        infoPanel.appendChild(titleEl);

        // Badges row
        var badgeRow = document.createElement('div');
        badgeRow.style.cssText = 'display:flex;flex-wrap:wrap;align-items:center;gap:6px;margin-bottom:14px;';

        if (theme.version) {
            var verPill = document.createElement('span');
            verPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:600;background:rgba(255,255,255,0.08);border:1px solid rgba(255,255,255,0.16);color:rgba(255,255,255,0.87);';
            verPill.textContent = 'v' + theme.version;
            badgeRow.appendChild(verPill);
        }

        if (jfConstraint) {
            var jfModalPill = document.createElement('span');
            jfModalPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:600;background:rgba(144,202,249,0.14);border:1px solid rgba(144,202,249,0.35);color:#90caf9;';
            jfModalPill.textContent = 'Jellyfin ' + jfConstraint;
            badgeRow.appendChild(jfModalPill);
        }

        if (theme.author) {
            var authorPill = document.createElement('span');
            authorPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:500;background:rgba(255,255,255,0.05);color:rgba(255,255,255,0.7);';
            authorPill.textContent = 'by ' + theme.author;
            badgeRow.appendChild(authorPill);
        }

        var createdDateStr = formatDate(theme.createdAt);
        if (createdDateStr) {
            var createdPill = document.createElement('span');
            createdPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;background:rgba(255,255,255,0.04);color:rgba(255,255,255,0.55);';
            createdPill.textContent = 'Created ' + createdDateStr;
            badgeRow.appendChild(createdPill);
        }

        var updatedDateStr = formatDate(theme.updatedAt || theme.date || (theme.files && theme.files[0] && theme.files[0].date));
        if (updatedDateStr && updatedDateStr !== createdDateStr) {
            var updatedPill = document.createElement('span');
            updatedPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:4px;font-size:11px;background:rgba(255,255,255,0.04);color:rgba(255,255,255,0.55);';
            updatedPill.textContent = 'Updated ' + updatedDateStr;
            badgeRow.appendChild(updatedPill);
        }

        if ((theme.tags || []).length > 0) {
            (theme.tags || []).forEach(function (tag) {
                var tagPill = document.createElement('span');
                tagPill.style.cssText = 'display:inline-flex;align-items:center;padding:2px 8px;border-radius:9999px;font-size:11px;font-weight:500;background:rgba(255,255,255,0.06);border:1px solid rgba(255,255,255,0.12);color:rgba(255,255,255,0.7);';
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

        // Files & Assets section
        if (Array.isArray(theme.files) && theme.files.length > 0) {
            var filesHeader = document.createElement('div');
            filesHeader.style.cssText = 'font-size:0.8125rem;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#90caf9;margin:18px 0 8px;display:flex;align-items:center;gap:6px;';
            filesHeader.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z"/></svg>Theme Files &amp; Assets (' + theme.files.length + ')';
            infoPanel.appendChild(filesHeader);

            var filesList = document.createElement('div');
            filesList.style.cssText = 'display:flex;flex-direction:column;gap:6px;margin-bottom:18px;';
            theme.files.forEach(function (f) {
                var fCard = document.createElement('div');
                fCard.style.cssText = 'padding:8px 12px;border-radius:8px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.08);display:flex;flex-direction:column;gap:4px;';

                var fRow = document.createElement('div');
                fRow.style.cssText = 'display:flex;align-items:center;gap:8px;flex-wrap:wrap;';

                var fType = document.createElement('span');
                fType.style.cssText = 'padding:1px 6px;border-radius:3px;font-size:10px;font-weight:700;letter-spacing:0.03em;background:rgba(144,202,249,0.15);color:#90caf9;border:1px solid rgba(144,202,249,0.3);';
                fType.textContent = (f.type || 'CSS').toUpperCase();
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

        // Configurable variables section
        if (Array.isArray(theme.vars) && theme.vars.length > 0) {
            var varsHeader = document.createElement('div');
            varsHeader.style.cssText = 'font-size:0.8125rem;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#90caf9;margin:18px 0 8px;display:flex;align-items:center;gap:6px;';
            varsHeader.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.57 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>Configurable Variables (' + theme.vars.length + ')';
            infoPanel.appendChild(varsHeader);

            var varsList = document.createElement('div');
            varsList.style.cssText = 'display:flex;flex-direction:column;gap:6px;margin-bottom:18px;';
            theme.vars.forEach(function (v) {
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
        if (theme.changelog && typeof theme.changelog === 'object' && Object.keys(theme.changelog).length > 0) {
            var clHeader = document.createElement('div');
            clHeader.style.cssText = 'font-size:0.8125rem;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#90caf9;margin:18px 0 8px;display:flex;align-items:center;gap:6px;';
            clHeader.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-5 14H7v-2h7v2zm3-4H7v-2h10v2zm0-4H7V7h10v2z"/></svg>Version History';
            infoPanel.appendChild(clHeader);

            var clList = document.createElement('div');
            clList.style.cssText = 'display:flex;flex-direction:column;gap:6px;margin-bottom:12px;';
            Object.keys(theme.changelog).forEach(function (verKey) {
                var clCard = document.createElement('div');
                clCard.style.cssText = 'padding:8px 12px;border-radius:8px;background:rgba(255,255,255,0.03);border:1px solid rgba(255,255,255,0.07);display:flex;flex-direction:column;gap:2px;';

                var clVer = document.createElement('span');
                clVer.style.cssText = 'font-size:11px;font-weight:700;color:#90caf9;';
                clVer.textContent = 'v' + verKey;
                clCard.appendChild(clVer);

                var clNote = document.createElement('div');
                clNote.style.cssText = 'font-size:11px;color:rgba(255,255,255,0.7);line-height:1.4;';
                clNote.textContent = theme.changelog[verKey];
                clCard.appendChild(clNote);

                clList.appendChild(clCard);
            });
            infoPanel.appendChild(clList);
        }

        dialog.appendChild(infoPanel);

        // Actions Footer
        var footerArea = document.createElement('div');
        footerArea.style.cssText = 'padding:14px 24px;border-top:1px solid rgba(255,255,255,0.1);background:#1a1a1a;display:flex;align-items:center;justify-content:flex-end;gap:10px;flex-shrink:0;';

        if (theme.sourceUrl) {
            var modalSrcBtn = document.createElement('a');
            modalSrcBtn.href = theme.sourceUrl;
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
            modalCfgBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.57 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>Configure';
            modalCfgBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                overlay.remove();
                openVarsModal(theme);
            });
            footerArea.appendChild(modalCfgBtn);
        }

        var modalApplyBtn = document.createElement('button');
        modalApplyBtn.type = 'button';
        modalApplyBtn.style.cssText = 'padding:8px 18px;border-radius:8px;border:none;font-size:0.8125rem;font-weight:700;letter-spacing:0.02857em;text-transform:uppercase;cursor:pointer;transition:all 150ms;'
            + (isActive
                ? 'background:#90caf9;color:rgba(0,0,0,0.87);'
                : 'background:#90caf9;color:rgba(0,0,0,0.87);box-shadow:0px 2px 4px rgba(0,0,0,0.25);');
        modalApplyBtn.textContent = isActive ? '\u2713 Active Theme' : 'Apply Theme';
        modalApplyBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            overlay.remove();
            selectTheme(theme);
        });
        footerArea.appendChild(modalApplyBtn);

        dialog.appendChild(footerArea);
        overlay.appendChild(dialog);

        overlay.addEventListener('click', function () { overlay.remove(); });
        dialog.addEventListener('click', function (e) { e.stopPropagation(); });
        document.addEventListener('keydown', function onKey(e) {
            if (e.key === 'Escape') { overlay.remove(); document.removeEventListener('keydown', onKey); }
        });

        document.body.appendChild(overlay);
    }

    var selectedThemeCacheIds = [];

    function switchThemeTab(name) {
        var panels = { themes: 'tabThemes', settings: 'jft-tabSettings' };
        Object.keys(panels).forEach(function (k) {
            var panel = document.getElementById(panels[k]);
            if (panel) panel.style.display = k === name ? '' : 'none';
        });
        var jftRoot = document.querySelector('.jfThemePage') || document;
        jftRoot.querySelectorAll('.jfThemeTab').forEach(function (btn) {
            var tabKey = btn.getAttribute('data-tab') || btn.dataset.tab;
            var active = tabKey === name;
            btn.classList.toggle('jfThemeTabActive', active);
            btn.classList.toggle('mui-tab-active', active);
            btn.setAttribute('aria-selected', active ? 'true' : 'false');
            btn.setAttribute('tabindex', active ? '0' : '-1');
        });
        if (name === 'settings') loadThemeCacheInfo();
    }

    document.querySelectorAll('.jfThemeTab').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var tabKey = this.getAttribute('data-tab') || this.dataset.tab;
            if (tabKey) switchThemeTab(tabKey);
        });
    });

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

    function loadThemeCacheInfo() {
        var infoBar = document.getElementById('themeCacheInfoBar');
        var list = document.getElementById('themeCacheList');
        var purgeSel = document.getElementById('btnPurgeSelectedThemes');
        infoBar.textContent = 'Loading\u2026';
        list.innerHTML = '';
        selectedThemeCacheIds = [];
        if (purgeSel) purgeSel.disabled = true;

        ApiClient.ajax({ url: ApiClient.getUrl('JellyFrame/api/themes/cache'), type: 'GET', dataType: 'json' })
            .then(function (data) {
                var kb = (data.totalBytes / 1024).toFixed(1);
                infoBar.textContent = data.fileCount + ' file(s) \u00b7 ' + kb + ' KB  \u2014  ' + data.cacheDir;

                var grouped = {};
                (data.entries || []).forEach(function (e) {
                    var key = e.themeId || 'unknown';
                    if (!grouped[key]) grouped[key] = [];
                    grouped[key].push(e);
                });

                if (Object.keys(grouped).length === 0) {
                    list.innerHTML = '<div style="font-size:0.8125rem;color:rgba(255,255,255,0.5);padding:8px 0;">Cache is empty.</div>';
                    return;
                }

                Object.keys(grouped).sort().forEach(function (themeId) {
                    var files = grouped[themeId];
                    var kb2 = (files.reduce(function (s, f) { return s + f.sizeBytes; }, 0) / 1024).toFixed(1);
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
                            if (!selectedThemeCacheIds.includes(themeId)) selectedThemeCacheIds.push(themeId);
                        } else {
                            selectedThemeCacheIds = selectedThemeCacheIds.filter(function (x) { return x !== themeId; });
                        }
                        if (purgeSel) purgeSel.disabled = selectedThemeCacheIds.length === 0;
                    });

                    var label = document.createElement('div');
                    label.style.cssText = 'flex:1;min-width:0;display:flex;flex-direction:column;gap:3px;';

                    var badgesHtml = '';
                    var hasBase = files.some(function (f) { return (f.type === 'base' || !f.type) && !f.isCompiled; });
                    var hasCompiled = files.some(function (f) { return f.isCompiled; });
                    var addonFiles = files.filter(function (f) { return f.type === 'addon' || f.addonId; });

                    if (hasBase) {
                        badgesHtml += '<span style="display:inline-block;padding:1px 6px;border-radius:4px;background:rgba(144,202,249,0.12);color:#90caf9;font-size:0.7rem;font-weight:600;">BASE</span>';
                    }
                    if (hasCompiled) {
                        badgesHtml += '<span style="display:inline-block;padding:1px 6px;border-radius:4px;background:rgba(206,147,216,0.15);color:#ce93d8;font-size:0.7rem;font-weight:600;">CONFIGURED</span>';
                    }
                    if (addonFiles.length > 0) {
                        var addonNames = [];
                        addonFiles.forEach(function (f) {
                            var aName = f.addonId ? f.addonId : 'ADDON';
                            if (!addonNames.includes(aName)) addonNames.push(aName);
                        });
                        badgesHtml += '<span style="display:inline-block;padding:1px 6px;border-radius:4px;background:rgba(102,187,106,0.15);color:#66bb6a;font-size:0.7rem;font-weight:600;">+' + escHtml(addonNames.join(', ')).toUpperCase() + '</span>';
                    }

                    var topRow = '<div style="display:flex;align-items:center;flex-wrap:wrap;gap:6px;">'
                        + '<span style="font-weight:600;font-size:0.875rem;color:rgba(255,255,255,0.87);">' + escHtml(themeId) + '</span>'
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
                    size.textContent = kb2 + ' KB';

                    row.appendChild(chk);
                    row.appendChild(label);
                    row.appendChild(size);
                    row.addEventListener('click', function (e) {
                        if (e.target !== chk) chk.checked = !chk.checked;
                        chk.dispatchEvent(new Event('change'));
                    });
                    list.appendChild(row);
                });
            })
            .catch(function () {
                infoBar.textContent = 'Failed to load theme cache info.';
            });
    }

    function doPurgeThemes(themeIds) {
        var resultEl = document.getElementById('themeCacheResult');
        var body = themeIds && themeIds.length > 0 ? JSON.stringify({ themeIds: themeIds }) : '{}';
        ApiClient.ajax({
            url: ApiClient.getUrl('JellyFrame/api/themes/cache/purge'),
            type: 'POST',
            data: body,
            contentType: 'application/json',
            dataType: 'json'
        }).then(function (res) {
            if (resultEl) {
                resultEl.style.display = '';
                resultEl.style.color = '#66bb6a';
                resultEl.textContent = '\u2713 Purged. Cache will rebuild on next page load.';
                setTimeout(function () { resultEl.style.display = 'none'; }, 4000);
            }
            selectedThemeCacheIds = [];
            loadThemeCacheInfo();
        }).catch(function () {
            if (resultEl) {
                resultEl.style.display = '';
                resultEl.style.color = '#f44336';
                resultEl.textContent = '\u2717 Purge failed. Check that you are logged in as admin.';
            }
        });
    }

    document.getElementById('btnPurgeAllThemes').addEventListener('click', function () {
        if (confirm('Purge the entire theme cache? All themes will re-download on next page load.'))
            doPurgeThemes([]);
    });
    document.getElementById('btnPurgeSelectedThemes').addEventListener('click', function () {
        if (selectedThemeCacheIds.length === 0) return;
        if (confirm('Purge cache for: ' + selectedThemeCacheIds.join(', ') + '?'))
            doPurgeThemes(selectedThemeCacheIds);
    });
    document.getElementById('btnRefreshThemeCache').addEventListener('click', loadThemeCacheInfo);
})();
