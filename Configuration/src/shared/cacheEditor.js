/**
 * Shared Cache Editor & Changelog Modals
 */

function openChangelogModal(changelogs, kind) {
    var existing = document.getElementById('jfChangelogOverlay');
    if (existing) existing.remove();

    var overlay = document.createElement('div');
    overlay.id = 'jfChangelogOverlay';
    overlay.style.cssText = [
        'position:fixed', 'inset:0', 'background:rgba(0,0,0,0.75)',
        'backdrop-filter:blur(4px)', '-webkit-backdrop-filter:blur(4px)',
        'z-index:100020', 'display:flex', 'align-items:center',
        'justify-content:center', 'padding:24px'
    ].join(';');

    var dialog = document.createElement('div');
    dialog.style.cssText = [
        'background:#1e1e1e',
        'border:1px solid rgba(255,167,38,0.3)',
        'border-radius:16px',
        'width:100%',
        'max-width:560px',
        'max-height:85vh',
        'display:flex',
        'flex-direction:column',
        'box-shadow:0px 11px 15px -7px rgba(0,0,0,0.2),0px 24px 38px 3px rgba(0,0,0,0.14),0px 9px 46px 8px rgba(0,0,0,0.12)',
        'overflow:hidden'
    ].join(';');

    var header = document.createElement('div');
    header.style.cssText = [
        'display:flex', 'align-items:center', 'gap:10px',
        'padding:18px 24px 14px',
        'border-bottom:1px solid rgba(255,255,255,0.1)',
        'flex-shrink:0'
    ].join(';');

    var headerTitle = document.createElement('div');
    headerTitle.style.cssText = 'font-size:1.125rem;font-weight:600;color:rgba(255,255,255,0.87);flex:1;';
    headerTitle.textContent = "What's New";

    var headerSub = document.createElement('div');
    headerSub.style.cssText = 'font-size:0.75rem;color:rgba(255,255,255,0.5);';
    headerSub.textContent = changelogs.length + ' ' + kind + (changelogs.length === 1 ? '' : 's') + ' updated';

    var closeBtn = document.createElement('button');
    closeBtn.type = 'button';
    closeBtn.textContent = '\u2715';
    closeBtn.style.cssText = [
        'background:none', 'border:none', 'color:rgba(255,255,255,0.6)',
        'cursor:pointer', 'font-size:1.1rem', 'padding:4px 6px', 'line-height:1'
    ].join(';');
    closeBtn.addEventListener('click', function () { overlay.remove(); });

    header.appendChild(headerTitle);
    header.appendChild(headerSub);
    header.appendChild(closeBtn);

    var body = document.createElement('div');
    body.style.cssText = 'overflow-y:auto;padding:18px 24px;flex:1;';

    changelogs.forEach(function (item, i) {
        if (i > 0) {
            var divider = document.createElement('div');
            divider.style.cssText = 'border-top:1px solid rgba(255,255,255,0.08);margin:18px 0;';
            body.appendChild(divider);
        }

        var nameRow = document.createElement('div');
        nameRow.style.cssText = 'display:flex;align-items:baseline;gap:8px;margin-bottom:10px;flex-wrap:wrap;';

        var nameEl = document.createElement('div');
        nameEl.style.cssText = 'font-size:1.0rem;font-weight:600;color:rgba(255,255,255,0.87);';
        nameEl.textContent = item.name;

        var pill = document.createElement('div');
        pill.style.cssText = [
            'font-size:0.75rem', 'font-family:monospace',
            'background:rgba(255,167,38,0.12)',
            'border:1px solid rgba(255,167,38,0.3)',
            'color:#ffa726', 'border-radius:4px',
            'padding:1px 8px', 'white-space:nowrap'
        ].join(';');
        pill.textContent = item.prevVersion + ' \u2192 ' + item.newVersion;

        nameRow.appendChild(nameEl);
        nameRow.appendChild(pill);
        body.appendChild(nameRow);

        var entries = item.entries || [];
        var matched = null;
        var unversioned = [];
        entries.forEach(function (e) {
            if (typeof e === 'string') {
                unversioned.push({ version: null, changes: [e] });
            } else if (e && typeof e === 'object') {
                if (e.version === item.newVersion) {
                    matched = e;
                } else if (!e.version) {
                    unversioned.push(e);
                }
            }
        });

        var toShow = matched ? [matched] : entries.map(function (e) {
            return typeof e === 'string' ? { version: null, changes: [e] } : e;
        });

        if (toShow.length === 0) {
            var noChanges = document.createElement('div');
            noChanges.style.cssText = 'font-size:0.8125rem;color:rgba(255,255,255,0.5);font-style:italic;';
            noChanges.textContent = 'No changelog entries for this version.';
            body.appendChild(noChanges);
            return;
        }

        toShow.forEach(function (entry) {
            if (!entry) return;
            var changes = Array.isArray(entry.changes) ? entry.changes
                : (typeof entry.changes === 'string' ? [entry.changes] : []);
            if (changes.length === 0) return;

            if (entry.version && toShow.length > 1) {
                var vLabel = document.createElement('div');
                vLabel.style.cssText = 'font-size:0.75rem;font-family:monospace;color:rgba(255,255,255,0.5);margin-bottom:4px;margin-top:6px;';
                vLabel.textContent = 'v' + entry.version;
                body.appendChild(vLabel);
            }

            var list = document.createElement('ul');
            list.style.cssText = 'margin:0 0 6px 4px;padding-left:16px;list-style:disc;';

            changes.forEach(function (change) {
                var li = document.createElement('li');
                li.style.cssText = 'font-size:0.875rem;line-height:1.6;color:rgba(255,255,255,0.75);margin-bottom:3px;';
                li.textContent = change;
                list.appendChild(li);
            });

            body.appendChild(list);
        });
    });

    var footer = document.createElement('div');
    footer.style.cssText = [
        'display:flex', 'justify-content:flex-end',
        'padding:14px 24px',
        'border-top:1px solid rgba(255,255,255,0.1)',
        'flex-shrink:0'
    ].join(';');

    var dismissBtn = document.createElement('button');
    dismissBtn.type = 'button';
    dismissBtn.className = 'mui-btn-outlined';
    dismissBtn.textContent = 'Close';
    dismissBtn.style.cssText = 'padding:6px 18px;';
    dismissBtn.addEventListener('click', function () { overlay.remove(); });

    footer.appendChild(dismissBtn);
    dialog.appendChild(header);
    dialog.appendChild(body);
    dialog.appendChild(footer);
    overlay.appendChild(dialog);

    overlay.addEventListener('click', function (e) { if (e.target === overlay) overlay.remove(); });
    document.addEventListener('keydown', function onKey(e) {
        if (e.key === 'Escape') { overlay.remove(); document.removeEventListener('keydown', onKey); }
    });

    document.body.appendChild(overlay);
}

function openCacheEditor(itemId, itemName, kind) {
    var existing = document.getElementById('jfCacheEditorOverlay');
    if (existing) existing.remove();

    var overlay = document.createElement('div');
    overlay.id = 'jfCacheEditorOverlay';
    overlay.style.cssText = [
        'position:fixed', 'inset:0', 'background:rgba(0,0,0,0.85)',
        'backdrop-filter:blur(4px)', '-webkit-backdrop-filter:blur(4px)',
        'z-index:100010', 'display:flex', 'flex-direction:column',
        'align-items:center', 'justify-content:center', 'padding:24px'
    ].join(';');

    var dialog = document.createElement('div');
    dialog.style.cssText = [
        'background:#1e1e1e', 'border:1px solid rgba(255,255,255,0.12)',
        'border-radius:16px', 'width:100%', 'max-width:940px',
        'height:88vh', 'display:flex', 'flex-direction:column',
        'box-shadow:0px 11px 15px -7px rgba(0,0,0,0.2),0px 24px 38px 3px rgba(0,0,0,0.14),0px 9px 46px 8px rgba(0,0,0,0.12)', 'overflow:hidden'
    ].join(';');

    var header = document.createElement('div');
    header.style.cssText = [
        'display:flex', 'align-items:center', 'gap:12px',
        'padding:14px 20px', 'background:#262626',
        'border-bottom:1px solid rgba(255,255,255,0.1)', 'flex-shrink:0'
    ].join(';');

    var titleEl = document.createElement('div');
    titleEl.style.cssText = 'font-size:0.95rem;font-weight:600;color:rgba(255,255,255,0.87);flex:1;';
    titleEl.textContent = 'Cache Editor: ' + itemName;

    var saveBtn = document.createElement('button');
    saveBtn.type = 'button';
    saveBtn.className = 'mui-btn-contained';
    saveBtn.style.cssText = 'padding:6px 18px;font-size:0.8125rem;';
    saveBtn.textContent = 'Save';

    var saveStatus = document.createElement('span');
    saveStatus.style.cssText = 'font-size:0.8125rem;color:rgba(255,255,255,0.6);min-width:90px;text-align:right;';

    var closeBtn = document.createElement('button');
    closeBtn.type = 'button';
    closeBtn.textContent = '\u2715';
    closeBtn.style.cssText = [
        'background:none', 'border:none', 'color:rgba(255,255,255,0.6)',
        'cursor:pointer', 'font-size:1.1rem', 'line-height:1', 'padding:4px 6px'
    ].join(';');
    closeBtn.addEventListener('click', function () { overlay.remove(); });

    header.appendChild(titleEl);
    header.appendChild(saveStatus);
    header.appendChild(saveBtn);
    header.appendChild(closeBtn);

    var tabStrip = document.createElement('div');
    tabStrip.style.cssText = [
        'display:flex', 'background:#262626',
        'border-bottom:1px solid rgba(255,255,255,0.1)',
        'flex-shrink:0', 'overflow-x:auto', 'min-height:38px'
    ].join(';');

    var editorWrap = document.createElement('div');
    editorWrap.style.cssText = 'flex:1;overflow:hidden;position:relative;';

    var textarea = document.createElement('textarea');
    textarea.style.cssText = [
        'width:100%', 'height:100%', 'background:#121212',
        'color:#e0e0e0', 'border:none', 'outline:none',
        'font-family:\'Consolas\',\'Monaco\',\'Courier New\',monospace',
        'font-size:13px', 'line-height:1.5', 'padding:16px',
        'resize:none', 'box-sizing:border-box', 'tab-size:2'
    ].join(';');
    textarea.spellcheck = false;

    textarea.addEventListener('keydown', function (e) {
        if (e.key === 'Tab') {
            e.preventDefault();
            var start = textarea.selectionStart;
            var end = textarea.selectionEnd;
            textarea.value = textarea.value.substring(0, start) + '  ' + textarea.value.substring(end);
            textarea.selectionStart = textarea.selectionEnd = start + 2;
        }
        if ((e.ctrlKey || e.metaKey) && e.key === 's') {
            e.preventDefault();
            doSave();
        }
    });

    editorWrap.appendChild(textarea);

    var statusBar = document.createElement('div');
    statusBar.style.cssText = [
        'display:flex', 'align-items:center', 'gap:16px',
        'padding:6px 18px', 'background:#1e1e1e', 'border-top:1px solid rgba(255,255,255,0.1)',
        'font-size:0.75rem', 'color:rgba(255,255,255,0.7)', 'flex-shrink:0'
    ].join(';');
    var langLabel = document.createElement('span');
    langLabel.style.cssText = 'color:#90caf9;font-weight:600;';
    langLabel.textContent = '';
    var fileLabel = document.createElement('span');
    fileLabel.style.cssText = 'color:rgba(255,255,255,0.6);flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
    fileLabel.textContent = '';
    var sizeLabel = document.createElement('span');
    sizeLabel.style.cssText = 'color:rgba(255,255,255,0.5);font-variant-numeric:tabular-nums;';
    statusBar.appendChild(langLabel);
    statusBar.appendChild(fileLabel);
    statusBar.appendChild(sizeLabel);

    dialog.appendChild(header);
    dialog.appendChild(tabStrip);
    dialog.appendChild(editorWrap);
    dialog.appendChild(statusBar);

    overlay.appendChild(dialog);
    overlay.addEventListener('click', function (e) { if (e.target === overlay) overlay.remove(); });
    document.addEventListener('keydown', function onKey(e) {
        if (e.key === 'Escape') { overlay.remove(); document.removeEventListener('keydown', onKey); }
    });
    document.body.appendChild(overlay);

    var files = [];
    var activeIdx = 0;
    var dirty = false;

    function setDirty(val) {
        dirty = val;
        saveBtn.style.background = val ? '#ffa726' : '#90caf9';
        saveBtn.style.color = 'rgba(0,0,0,0.87)';
        saveStatus.textContent = val ? 'Unsaved changes' : '';
    }

    textarea.addEventListener('input', function () { setDirty(true); });

    function loadTab(idx) {
        if (files.length === 0) return;
        if (dirty) {
            files[activeIdx].pendingContent = textarea.value;
        }
        activeIdx = idx;
        var f = files[idx];
        textarea.value = f.pendingContent !== undefined ? f.pendingContent : (f.content || '');
        langLabel.textContent = (f.lang || '').toUpperCase();
        fileLabel.textContent = f.filename || '';
        sizeLabel.textContent = ((f.content || '').length / 1024).toFixed(1) + ' KB';
        setDirty(f.pendingContent !== undefined);

        var tabs = tabStrip.querySelectorAll('[data-tab-idx]');
        for (var i = 0; i < tabs.length; i++) {
            var active = parseInt(tabs[i].getAttribute('data-tab-idx')) === idx;
            tabs[i].style.background = active ? '#1e1e1e' : 'transparent';
            tabs[i].style.borderBottom = active ? '2px solid #90caf9' : '2px solid transparent';
            tabs[i].style.color = active ? '#90caf9' : 'rgba(255,255,255,0.6)';
        }
        textarea.focus();
    }

    function buildTabs() {
        tabStrip.innerHTML = '';
        for (var i = 0; i < files.length; i++) {
            (function (idx) {
                var tab = document.createElement('button');
                tab.type = 'button';
                tab.setAttribute('data-tab-idx', idx);
                tab.style.cssText = [
                    'padding:8px 20px', 'border:none', 'background:transparent',
                    'color:rgba(255,255,255,0.5)', 'cursor:pointer',
                    'font-size:.8em', 'border-bottom:2px solid transparent',
                    'white-space:nowrap', 'flex-shrink:0'
                ].join(';');
                tab.textContent = files[idx].tabLabel || (files[idx].type + '.' + files[idx].lang);
                tab.addEventListener('click', function () { loadTab(idx); });
                tabStrip.appendChild(tab);
            })(i);
        }
    }

    function doSave() {
        if (files.length === 0) return;
        files[activeIdx].pendingContent = textarea.value;

        var toSave = [];
        for (var i = 0; i < files.length; i++) {
            if (files[i].pendingContent !== undefined) {
                toSave.push({ idx: i, file: files[i] });
            }
        }
        if (toSave.length === 0) return;

        saveBtn.disabled = true;
        saveStatus.textContent = 'Saving...';

        var done = 0;
        var failed = 0;
        toSave.forEach(function (item) {
            ApiClient.ajax({
                url: ApiClient.getUrl('JellyFrame/api/cache/file') + '?path=' + encodeURIComponent(item.file.path),
                type: 'PUT',
                data: JSON.stringify({ content: item.file.pendingContent }),
                contentType: 'application/json'
            }).then(function () {
                files[item.idx].content = files[item.idx].pendingContent;
                delete files[item.idx].pendingContent;
                done++;
                if (done + failed === toSave.length) onSaveDone(failed);
            }).catch(function () {
                failed++;
                if (done + failed === toSave.length) onSaveDone(failed);
            });
        });
    }

    function onSaveDone(failCount) {
        saveBtn.disabled = false;
        if (failCount > 0) {
            saveStatus.textContent = failCount + ' file(s) failed to save';
            saveStatus.style.color = '#f44';
        } else {
            saveStatus.textContent = 'Saved - reloading...';
            saveStatus.style.color = '#4f4';
            setDirty(false);
            setTimeout(function () {
                try {
                    if (window.parent && window.parent.location) {
                        window.parent.location.reload();
                    }
                } catch (e) {
                    saveStatus.textContent = 'Saved - refresh to apply';
                    saveStatus.style.color = '';
                }
            }, 800);
        }
    }

    saveBtn.addEventListener('click', doSave);

    textarea.value = 'Loading...';
    ApiClient.ajax({
        url: ApiClient.getUrl('JellyFrame/api/cache/files/' + encodeURIComponent(itemId)) + '?kind=' + kind,
        type: 'GET',
        dataType: 'json'
    }).then(function (data) {
        files = data.files || [];
        if (files.length === 0) {
            textarea.value = '// No cached files found for this ' + kind + '.\n// Enable the ' + kind + ' and trigger a page load to populate the cache, then reopen this editor.';
            tabStrip.innerHTML = '';
            langLabel.textContent = '';
            fileLabel.textContent = 'No files cached';
            sizeLabel.textContent = '';
            return;
        }

        var pending = files.length;
        files.forEach(function (f, idx) {
            ApiClient.ajax({
                url: ApiClient.getUrl('JellyFrame/api/cache/file') + '?path=' + encodeURIComponent(f.path),
                type: 'GET',
                dataType: 'json'
            }).then(function (res) {
                files[idx].content = res.content || '';
                pending--;
                if (pending === 0) {
                    buildTabs();
                    loadTab(0);
                }
            }).catch(function () {
                files[idx].content = '// Failed to load file content.';
                pending--;
                if (pending === 0) {
                    buildTabs();
                    loadTab(0);
                }
            });
        });
    }).catch(function () {
        textarea.value = '// Failed to load file list. Are you logged in as admin?';
    });
}
