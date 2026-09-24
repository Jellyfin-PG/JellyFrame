/**
 * Shared Material UI Styled Color & Gradient Picker Component
 */
function buildColorInput(savedValue, defaultValue, allowGradient) {
    var initial = (savedValue || defaultValue || '#000000').trim();

    function toHex(n) { return Math.round(Math.max(0, Math.min(255, n))).toString(16).padStart(2, '0'); }
    function toStr(r, g, b, a) { return a >= 1 ? '#' + toHex(r) + toHex(g) + toHex(b) : 'rgba(' + Math.round(r) + ', ' + Math.round(g) + ', ' + Math.round(b) + ', ' + Math.round(a * 100) / 100 + ')'; }

    function parseColor(str) {
        str = (str || '').trim();
        var c = { r: 0, g: 0, b: 0, a: 1 }, m;
        m = str.match(/rgba?\(\s*(\d+)[,\s]+(\d+)[,\s]+(\d+)(?:[,\/\s]+([\d.]+))?\s*\)/i);
        if (m) { c.r = parseInt(m[1]); c.g = parseInt(m[2]); c.b = parseInt(m[3]); c.a = m[4] !== undefined ? parseFloat(m[4]) : 1; return c; }
        m = str.match(/^#([0-9a-f]{8})$/i);
        if (m) { c.r = parseInt(m[1].slice(0, 2), 16); c.g = parseInt(m[1].slice(2, 4), 16); c.b = parseInt(m[1].slice(4, 6), 16); c.a = parseInt(m[1].slice(6, 8), 16) / 255; return c; }
        m = str.match(/^#([0-9a-f]{6})$/i);
        if (m) { c.r = parseInt(m[1].slice(0, 2), 16); c.g = parseInt(m[1].slice(2, 4), 16); c.b = parseInt(m[1].slice(4, 6), 16); return c; }
        m = str.match(/^#([0-9a-f]{3})$/i);
        if (m) { c.r = parseInt(m[1][0] + m[1][0], 16); c.g = parseInt(m[1][1] + m[1][1], 16); c.b = parseInt(m[1][2] + m[1][2], 16); return c; }
        return c;
    }

    function isGradient(str) { return /^(linear|radial|conic)-gradient\(/.test((str || '').trim()); }

    function parseGradientStops(str) {
        var stops = [];
        var inner = str.replace(/^(linear|radial|conic)-gradient\(\s*/, '').replace(/\s*\)$/, '');
        var chunks = inner.split(/,(?![^()]*\))/);
        var colorChunks = chunks.filter(function (ch) {
            return /rgba?\(|#[0-9a-f]/i.test(ch.trim());
        });
        colorChunks.forEach(function (ch) {
            ch = ch.trim();
            var posMatch = ch.match(/([\d.]+%)\s*$/);
            var pos = posMatch ? parseFloat(posMatch[1]) : null;
            var colorPart = posMatch ? ch.slice(0, ch.lastIndexOf(posMatch[1])).trim() : ch;
            stops.push({ color: colorPart, pos: pos });
        });
        if (stops.length < 2) { stops = [{ color: '#000000', pos: 0 }, { color: '#ffffff', pos: 100 }]; }
        return stops;
    }

    function parseGradientDir(str) {
        var m = str.match(/^linear-gradient\(\s*([^,]+),/);
        if (!m) return '180deg';
        var d = m[1].trim();
        if (/deg/.test(d)) return d;
        if (d === 'to bottom') return '180deg';
        if (d === 'to top') return '0deg';
        if (d === 'to right') return '90deg';
        if (d === 'to left') return '270deg';
        if (d === 'to bottom right') return '135deg';
        if (d === 'to bottom left') return '225deg';
        return '180deg';
    }

    function buildGradientStr(type, dir, stops) {
        var stopsStr = stops.map(function (s) {
            return s.color + (s.pos !== null ? ' ' + s.pos + '%' : '');
        }).join(', ');
        if (type === 'radial') return 'radial-gradient(circle, ' + stopsStr + ')';
        if (type === 'conic') return 'conic-gradient(from ' + dir + ', ' + stopsStr + ')';
        return 'linear-gradient(' + dir + ', ' + stopsStr + ')';
    }

    var MODE_SOLID = 'solid', MODE_GRADIENT = 'gradient';
    var mode = (isGradient(initial) && allowGradient !== false) ? MODE_GRADIENT : MODE_SOLID;

    var solidColor = mode === MODE_SOLID ? parseColor(initial) : { r: 0, g: 164, b: 220, a: 1 };
    var gradStops = mode === MODE_GRADIENT ? parseGradientStops(initial) : [{ color: '#00a4dc', pos: 0 }, { color: '#0066aa', pos: 100 }];
    var gradType = mode === MODE_GRADIENT ? (initial.startsWith('radial') ? 'radial' : initial.startsWith('conic') ? 'conic' : 'linear') : 'linear';
    var gradDir = mode === MODE_GRADIENT ? parseGradientDir(initial) : '180deg';

    var S = 'display:flex;flex-direction:column;gap:8px;';
    var wrap = document.createElement('div');
    wrap.style.cssText = S;

    var hidden = document.createElement('input');
    hidden.type = 'hidden'; hidden.name = 'var-color-value';

    var modeRow = document.createElement('div');
    modeRow.style.cssText = 'display:flex;gap:4px;';
    function modeBtn(label, val) {
        var b = document.createElement('button');
        b.type = 'button'; b.textContent = label; b.dataset.mode = val;
        b.style.cssText = 'flex:1;padding:4px 8px;border-radius:5px;border:1px solid rgba(255,255,255,0.14);font-size:11px;cursor:pointer;transition:all .15s;background:' + (mode === val ? '#00a4dc' : 'rgba(255,255,255,0.06)') + ';color:' + (mode === val ? '#fff' : 'rgba(255,255,255,0.7)') + ';';
        b.addEventListener('click', function () {
            mode = val;
            modeRow.querySelectorAll('button').forEach(function (x) {
                var active = x.dataset.mode === val;
                x.style.background = active ? '#00a4dc' : 'rgba(255,255,255,0.06)';
                x.style.color = active ? '#fff' : 'rgba(255,255,255,0.7)';
            });
            solidPanel.style.display = val === MODE_SOLID ? 'flex' : 'none';
            gradPanel.style.display = val === MODE_GRADIENT ? 'flex' : 'none';
            updateValue();
        });
        return b;
    }
    modeRow.appendChild(modeBtn('Solid', MODE_SOLID));
    if (allowGradient !== false) modeRow.appendChild(modeBtn('Gradient', MODE_GRADIENT));

    var swatch = document.createElement('div');
    swatch.style.cssText = 'height:28px;border-radius:5px;border:1px solid rgba(255,255,255,0.14);background-image:linear-gradient(45deg,#666 25%,transparent 25%),linear-gradient(-45deg,#666 25%,transparent 25%),linear-gradient(45deg,transparent 75%,#666 75%),linear-gradient(-45deg,transparent 75%,#666 75%);background-size:10px 10px;background-position:0 0,0 5px,5px -5px,-5px 0;position:relative;overflow:hidden;';
    var swatchInner = document.createElement('div');
    swatchInner.style.cssText = 'position:absolute;inset:0;';
    swatch.appendChild(swatchInner);

    var valueDisplay = document.createElement('div');
    valueDisplay.style.cssText = 'font-size:10px;opacity:0.4;font-family:monospace;word-break:break-all;';

    function updateValue() {
        var val;
        if (mode === MODE_SOLID) {
            var hex = solidPicker.value, a = parseFloat(solidAlpha.value);
            var r = parseInt(hex.slice(1, 3), 16), g = parseInt(hex.slice(3, 5), 16), b = parseInt(hex.slice(5, 7), 16);
            solidAlphaLabel.textContent = 'Opacity ' + Math.round(a * 100) + '%';
            val = toStr(r, g, b, a);
        } else {
            val = buildGradientStr(gradType, gradDir, gradStops);
        }
        hidden.value = val;
        swatchInner.style.background = val;
        valueDisplay.textContent = val;
    }

    var solidPanel = document.createElement('div');
    solidPanel.style.cssText = 'flex-direction:column;gap:6px;display:' + (mode === MODE_SOLID ? 'flex' : 'none') + ';';

    var solidRow1 = document.createElement('div'); solidRow1.style.cssText = 'display:flex;gap:8px;align-items:center;';
    var solidPicker = document.createElement('input'); solidPicker.type = 'color'; solidPicker.name = 'var-color-hex';
    solidPicker.value = '#' + toHex(solidColor.r) + toHex(solidColor.g) + toHex(solidColor.b);
    solidPicker.style.cssText = 'width:44px;height:34px;padding:2px 3px;border:1px solid rgba(255,255,255,0.14);border-radius:5px;background:rgba(255,255,255,0.06);cursor:pointer;flex-shrink:0;';
    var solidHexInput = document.createElement('input'); solidHexInput.type = 'text'; solidHexInput.name = 'var-color-hex-text';
    solidHexInput.style.cssText = 'flex:1;padding:5px 8px;background:rgba(255,255,255,0.06);border:1px solid rgba(255,255,255,0.14);border-radius:5px;color:inherit;font-size:12px;font-family:monospace;';
    solidHexInput.value = solidPicker.value;
    solidPicker.addEventListener('input', function () { solidHexInput.value = solidPicker.value; updateValue(); });
    solidHexInput.addEventListener('input', function () {
        var v = solidHexInput.value.trim();
        if (/^#[0-9a-f]{6}$/i.test(v) || /^#[0-9a-f]{3}$/i.test(v)) solidPicker.value = v;
        updateValue();
    });
    solidRow1.appendChild(solidPicker); solidRow1.appendChild(solidHexInput);

    var solidRow2 = document.createElement('div'); solidRow2.style.cssText = 'display:flex;gap:8px;align-items:center;';
    var solidAlphaLabel = document.createElement('div'); solidAlphaLabel.style.cssText = 'font-size:11px;opacity:0.55;flex-shrink:0;width:72px;';
    var solidAlpha = document.createElement('input'); solidAlpha.type = 'range'; solidAlpha.min = '0'; solidAlpha.max = '1'; solidAlpha.step = '0.01';
    solidAlpha.name = 'var-color-alpha'; solidAlpha.value = String(solidColor.a);
    solidAlpha.style.cssText = 'flex:1;cursor:pointer;accent-color:#00a4dc;';
    solidAlpha.addEventListener('input', updateValue);
    solidRow2.appendChild(solidAlphaLabel); solidRow2.appendChild(solidAlpha);
    solidPanel.appendChild(solidRow1); solidPanel.appendChild(solidRow2);

    var gradPanel = document.createElement('div');
    gradPanel.style.cssText = 'flex-direction:column;gap:8px;display:' + (mode === MODE_GRADIENT ? 'flex' : 'none') + ';';

    var gradTypeRow = document.createElement('div'); gradTypeRow.style.cssText = 'display:flex;gap:4px;';
    ['linear', 'radial', 'conic'].forEach(function (t) {
        var b = document.createElement('button'); b.type = 'button'; b.textContent = t.charAt(0).toUpperCase() + t.slice(1); b.dataset.gtype = t;
        b.style.cssText = 'flex:1;padding:3px 6px;border-radius:4px;border:1px solid rgba(255,255,255,0.14);font-size:11px;cursor:pointer;background:' + (gradType === t ? 'rgba(0,164,220,0.25)' : 'rgba(255,255,255,0.06)') + ';color:inherit;';
        b.addEventListener('click', function () {
            gradType = t;
            gradTypeRow.querySelectorAll('[data-gtype]').forEach(function (x) { x.style.background = x.dataset.gtype === t ? 'rgba(0,164,220,0.25)' : 'rgba(255,255,255,0.06)'; });
            gradDirRow.style.display = t === 'radial' ? 'none' : 'flex';
            updateValue();
        });
        gradTypeRow.appendChild(b);
    });

    var gradDirRow = document.createElement('div'); gradDirRow.style.cssText = 'display:' + (gradType === 'radial' ? 'none' : 'flex') + ';flex-direction:column;gap:4px;';
    var gradDirLabel = document.createElement('div'); gradDirLabel.style.cssText = 'font-size:11px;opacity:0.55;'; gradDirLabel.textContent = 'Direction';
    var gradDirBtns = document.createElement('div'); gradDirBtns.style.cssText = 'display:flex;flex-wrap:wrap;gap:3px;';
    var DIRS = [['↑', '0deg'], ['↗', '45deg'], ['→', '90deg'], ['↘', '135deg'], ['↓', '180deg'], ['↙', '225deg'], ['←', '270deg'], ['↖', '315deg']];
    DIRS.forEach(function (d) {
        var b = document.createElement('button'); b.type = 'button'; b.textContent = d[0]; b.dataset.deg = d[1];
        b.title = d[1];
        b.style.cssText = 'width:28px;height:28px;border-radius:4px;border:1px solid rgba(255,255,255,0.14);font-size:14px;cursor:pointer;background:' + (gradDir === d[1] ? 'rgba(0,164,220,0.3)' : 'rgba(255,255,255,0.06)') + ';color:inherit;';
        b.addEventListener('click', function () {
            gradDir = d[1];
            gradDirBtns.querySelectorAll('[data-deg]').forEach(function (x) { x.style.background = x.dataset.deg === d[1] ? 'rgba(0,164,220,0.3)' : 'rgba(255,255,255,0.06)'; });
            updateValue();
        });
        gradDirBtns.appendChild(b);
    });
    var gradDirCustomRow = document.createElement('div'); gradDirCustomRow.style.cssText = 'display:flex;gap:6px;align-items:center;';
    var gradDirCustomLabel = document.createElement('div'); gradDirCustomLabel.style.cssText = 'font-size:11px;opacity:0.55;flex-shrink:0;'; gradDirCustomLabel.textContent = 'Custom:';
    var gradDirCustom = document.createElement('input'); gradDirCustom.type = 'range'; gradDirCustom.min = '0'; gradDirCustom.max = '360'; gradDirCustom.step = '1';
    gradDirCustom.value = parseInt(gradDir);
    gradDirCustom.style.cssText = 'flex:1;cursor:pointer;accent-color:#00a4dc;';
    var gradDirCustomVal = document.createElement('div'); gradDirCustomVal.style.cssText = 'font-size:11px;font-family:monospace;width:40px;flex-shrink:0;';
    gradDirCustomVal.textContent = gradDir;
    gradDirCustom.addEventListener('input', function () {
        gradDir = gradDirCustom.value + 'deg';
        gradDirCustomVal.textContent = gradDir;
        gradDirBtns.querySelectorAll('[data-deg]').forEach(function (x) { x.style.background = 'rgba(255,255,255,0.06)'; });
        updateValue();
    });
    gradDirCustomRow.appendChild(gradDirCustomLabel); gradDirCustomRow.appendChild(gradDirCustom); gradDirCustomRow.appendChild(gradDirCustomVal);
    gradDirRow.appendChild(gradDirLabel); gradDirRow.appendChild(gradDirBtns); gradDirRow.appendChild(gradDirCustomRow);

    var stopsContainer = document.createElement('div'); stopsContainer.style.cssText = 'display:flex;flex-direction:column;gap:6px;';
    var stopsLabel = document.createElement('div'); stopsLabel.style.cssText = 'display:flex;justify-content:space-between;align-items:center;';
    var stopsTitle = document.createElement('div'); stopsTitle.style.cssText = 'font-size:11px;opacity:0.55;'; stopsTitle.textContent = 'Color stops';
    var addStopBtn = document.createElement('button'); addStopBtn.type = 'button'; addStopBtn.textContent = '+ Add stop';
    addStopBtn.style.cssText = 'font-size:11px;padding:2px 8px;border-radius:4px;border:1px solid rgba(255,255,255,0.14);background:rgba(255,255,255,0.06);color:inherit;cursor:pointer;';
    stopsLabel.appendChild(stopsTitle); stopsLabel.appendChild(addStopBtn);
    var stopsList = document.createElement('div'); stopsList.style.cssText = 'display:flex;flex-direction:column;gap:5px;';

    function renderStop(stop, idx) {
        var stopRow = document.createElement('div'); stopRow.style.cssText = 'display:flex;gap:6px;align-items:center;';
        var stopPicker = document.createElement('input'); stopPicker.type = 'color'; stopPicker.value = stop.color.startsWith('#') ? stop.color : '#000000';
        stopPicker.style.cssText = 'width:34px;height:28px;padding:1px 2px;border:1px solid rgba(255,255,255,0.14);border-radius:4px;background:rgba(255,255,255,0.06);cursor:pointer;flex-shrink:0;';
        var stopAlpha = document.createElement('input'); stopAlpha.type = 'range'; stopAlpha.min = '0'; stopAlpha.max = '1'; stopAlpha.step = '0.01';
        var sc = parseColor(stop.color); stopAlpha.value = String(sc.a);
        stopAlpha.style.cssText = 'flex:1;cursor:pointer;accent-color:#00a4dc;';
        var stopPos = document.createElement('input'); stopPos.type = 'number'; stopPos.min = '0'; stopPos.max = '100'; stopPos.step = '1';
        stopPos.value = stop.pos !== null ? stop.pos : Math.round(idx / (gradStops.length - 1 || 1) * 100);
        stopPos.style.cssText = 'width:44px;padding:3px 5px;background:rgba(255,255,255,0.06);border:1px solid rgba(255,255,255,0.14);border-radius:4px;color:inherit;font-size:11px;text-align:center;';
        var stopDel = document.createElement('button'); stopDel.type = 'button'; stopDel.textContent = '✕';
        stopDel.style.cssText = 'width:22px;height:22px;border-radius:4px;border:1px solid rgba(255,255,255,0.14);background:rgba(255,255,255,0.06);color:rgba(255,255,255,0.5);cursor:pointer;font-size:11px;flex-shrink:0;';
        if (gradStops.length <= 2) stopDel.disabled = true;
        function syncStop() {
            var hex = stopPicker.value, a = parseFloat(stopAlpha.value);
            var r = parseInt(hex.slice(1, 3), 16), g = parseInt(hex.slice(3, 5), 16), b = parseInt(hex.slice(5, 7), 16);
            stop.color = toStr(r, g, b, a);
            stop.pos = parseInt(stopPos.value) || 0;
            updateValue();
        }
        stopPicker.addEventListener('input', syncStop);
        stopAlpha.addEventListener('input', syncStop);
        stopPos.addEventListener('input', syncStop);
        stopDel.addEventListener('click', function () {
            if (gradStops.length > 2) { gradStops.splice(idx, 1); renderStops(); updateValue(); }
        });
        stopRow.appendChild(stopPicker); stopRow.appendChild(stopAlpha); stopRow.appendChild(stopPos); stopRow.appendChild(stopDel);
        return stopRow;
    }

    function renderStops() {
        stopsList.innerHTML = '';
        gradStops.forEach(function (stop, idx) { stopsList.appendChild(renderStop(stop, idx)); });
    }
    renderStops();

    addStopBtn.addEventListener('click', function () {
        gradStops.push({ color: '#888888', pos: 100 });
        renderStops(); updateValue();
    });

    stopsContainer.appendChild(stopsLabel); stopsContainer.appendChild(stopsList);
    gradPanel.appendChild(gradTypeRow); gradPanel.appendChild(gradDirRow); gradPanel.appendChild(stopsContainer);

    wrap.appendChild(modeRow);
    wrap.appendChild(swatch);
    wrap.appendChild(solidPanel);
    wrap.appendChild(gradPanel);
    wrap.appendChild(valueDisplay);
    wrap.appendChild(hidden);

    updateValue();
    return wrap;
}
