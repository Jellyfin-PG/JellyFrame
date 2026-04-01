(function () {
    'use strict';

    var INTERVAL_MS   = parseInt('{{SLIDE_INTERVAL}}', 10) || 8000;
    var API_BASE      = '/JellyFrame/mods/media-bar/api';

    var slides       = [];
    var currentIndex = 0;
    var timer        = null;
    var paused       = false;
    var container    = null;
    var dotsEl       = null;
    var initialized  = false;

    function formatRuntime(ticks) {
        if (!ticks) return '';
        var mins  = Math.floor(ticks / 600000000);
        var hours = Math.floor(mins / 60);
        var rem   = mins % 60;
        return hours > 0 ? hours + 'h ' + rem + 'm' : rem + 'm';
    }

    function el(tag, cls, extra) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (extra) Object.assign(e, extra);
        return e;
    }

    function buildSlide(item, index) {
        var slide = el('div', 'slide' + (index === 0 ? ' active' : ''));

        var bdContainer = el('div', 'backdrop-container');
        if (item.backdropUrl) {
            var bdImg = el('img', 'backdrop');
            bdImg.src = item.backdropUrl;
            bdImg.alt = '';
            bdImg.loading = 'lazy';
            bdImg.addEventListener('load', function () {
                bdImg.classList.add('high-quality');
            });
            bdImg.classList.add('low-quality');
            bdContainer.appendChild(bdImg);
        }
        var bdOverlay  = el('div', 'backdrop-overlay');
        var gradOverlay = el('div', 'gradient-overlay');
        bdContainer.appendChild(bdOverlay);
        bdContainer.appendChild(gradOverlay);
        slide.appendChild(bdContainer);

        var logoContainer = el('div', 'logo-container');
        if (item.logoUrl) {
            var logo = el('img', 'logo');
            logo.src = item.logoUrl;
            logo.alt = item.name;
            logo.loading = 'lazy';
            logoContainer.appendChild(logo);
        } else {
            var titleFallback = el('div', 'logo');
            titleFallback.textContent = item.name;
            titleFallback.style.cssText = 'font-size:2.5rem;font-weight:700;color:#fff;text-shadow:0 2px 8px rgba(0,0,0,.7);';
            logoContainer.appendChild(titleFallback);
        }
        slide.appendChild(logoContainer);

        var infoContainer = el('div', 'info-container');
        var miscInfo      = el('div', 'misc-info');

        if (item.communityRating) {
            var starWrap = el('span', 'star-rating-container');
            var starIcon = el('span', 'material-icons community-rating-star');
            starIcon.textContent = 'star';
            var starVal = document.createTextNode(' ' + item.communityRating.toFixed(1));
            starWrap.appendChild(starIcon);
            starWrap.appendChild(starVal);
            miscInfo.appendChild(starWrap);
        }

        if (item.year) {
            var sep1 = el('span', 'material-icons separator-icon');
            sep1.textContent = 'fiber_manual_record';
            miscInfo.appendChild(sep1);
            var dateEl = el('span', 'date');
            dateEl.textContent = item.year;
            miscInfo.appendChild(dateEl);
        }

        if (item.rating) {
            var sep2 = el('span', 'material-icons separator-icon');
            sep2.textContent = 'fiber_manual_record';
            miscInfo.appendChild(sep2);
            var ratingEl = el('span', 'age-rating');
            ratingEl.textContent = item.rating;
            miscInfo.appendChild(ratingEl);
        }

        if (item.runTimeTicks) {
            var sep3 = el('span', 'material-icons separator-icon');
            sep3.textContent = 'fiber_manual_record';
            miscInfo.appendChild(sep3);
            var rtEl = el('span', 'runTime');
            rtEl.textContent = formatRuntime(item.runTimeTicks);
            miscInfo.appendChild(rtEl);
        }

        infoContainer.appendChild(miscInfo);
        slide.appendChild(infoContainer);

        if (item.genres && item.genres.length > 0) {
            var genreEl = el('div', 'genre');
            item.genres.slice(0, 3).forEach(function (g, i) {
                if (i > 0) {
                    var sep = el('span', 'material-icons separator-icon');
                    sep.textContent = 'fiber_manual_record';
                    genreEl.appendChild(sep);
                }
                var gSpan = document.createTextNode(g);
                genreEl.appendChild(gSpan);
            });
            slide.appendChild(genreEl);
        }

        if (item.overview) {
            var plotContainer = el('div', 'plot-container');
            var plot = el('div', 'plot');
            plot.textContent = item.overview;
            plotContainer.appendChild(plot);
            slide.appendChild(plotContainer);
        }

        var btnContainer = el('div', 'button-container');

        var playBtn = el('button', 'play-button raised button-submit emby-button');
        playBtn.style.cssText = 'background:rgba(255,255,255,0.9);color:#000;';
        playBtn.innerHTML = '<span class="material-icons" style="vertical-align:middle;margin-right:4px;">play_arrow</span>Play Now';
        playBtn.addEventListener('click', function () {
            window.location.href = item.playbackUrl;
        });
        btnContainer.appendChild(playBtn);

        var detailBtn = el('button', 'detail-button');
        detailBtn.title = 'More Info';
        detailBtn.style.cssText = 'background:rgba(255,255,255,0.15);color:#fff;';
        detailBtn.addEventListener('click', function () {
            window.location.href = item.detailUrl;
        });
        btnContainer.appendChild(detailBtn);

        var favBtn = el('button', 'favorite-button' + (item.isFavorite ? ' favorited' : ''));
        favBtn.title = item.isFavorite ? 'Remove from Favourites' : 'Add to Favourites';
        favBtn.dataset.itemId   = item.id;
        favBtn.dataset.favourite = item.isFavorite ? 'true' : 'false';
        favBtn.style.cssText = 'background:rgba(255,255,255,0.15);';
        favBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            var current = favBtn.dataset.favourite === 'true';
            var next    = !current;
            favBtn.dataset.favourite = next ? 'true' : 'false';
            favBtn.classList.toggle('favorited', next);
            item.isFavorite = next;
            fetch(API_BASE + '/favourite/' + item.id, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ favourite: next })
            });
        });
        btnContainer.appendChild(favBtn);

        slide.appendChild(btnContainer);
        return slide;
    }

    function buildUI(items) {
        var existing = document.getElementById('jf-media-bar-container');
        if (existing) existing.remove();

        slides   = items;
        currentIndex = 0;

        container = el('div', '', { id: 'jf-media-bar-container' });
        container.id = 'slides-container';

        items.forEach(function (item, i) {
            container.appendChild(buildSlide(item, i));
        });

        var leftArrow = el('div', 'arrow left-arrow');
        leftArrow.innerHTML = '<span class="material-icons" style="font-size:2rem;">chevron_left</span>';
        leftArrow.addEventListener('click', function () { prev(); resetTimer(); });
        container.appendChild(leftArrow);

        var rightArrow = el('div', 'arrow right-arrow');
        rightArrow.innerHTML = '<span class="material-icons" style="font-size:2rem;">chevron_right</span>';
        rightArrow.addEventListener('click', function () { next(); resetTimer(); });
        container.appendChild(rightArrow);

        var pauseBtn = el('div', 'pause-button');
        pauseBtn.innerHTML = '<span class="material-icons">pause</span>';
        pauseBtn.addEventListener('click', function () {
            paused = !paused;
            pauseBtn.innerHTML = paused
                ? '<span class="material-icons">play_arrow</span>'
                : '<span class="material-icons">pause</span>';
            if (paused) { clearInterval(timer); timer = null; }
            else { resetTimer(); }
        });
        container.appendChild(pauseBtn);

        dotsEl = el('div', 'dots-container');
        items.forEach(function (_, i) {
            var dot = el('div', 'dot' + (i === 0 ? ' active' : ''));
            dot.addEventListener('click', function () { goTo(i); resetTimer(); });
            dotsEl.appendChild(dot);
        });
        container.appendChild(dotsEl);

        var homeSections = document.querySelector('.homeSectionsContainer');
        if (homeSections && homeSections.parentNode) {
            homeSections.parentNode.insertBefore(container, homeSections);
        } else {
            var mainContent = document.querySelector('#mainAnimatedPages, .mainAnimatedPages, .mainContent');
            if (mainContent) mainContent.prepend(container);
        }

        animateSlide(0);
        resetTimer();
        initialized = true;
    }

    function getSlideEls() {
        return container ? container.querySelectorAll('.slide') : [];
    }

    function animateSlide(index) {
        var slideEls = getSlideEls();
        slideEls.forEach(function (s, i) {
            var bd   = s.querySelector('.backdrop');
            var logo = s.querySelector('.logo');
            s.classList.toggle('active', i === index);
            if (i === index) {
                if (bd)   { bd.classList.remove('animate');   void bd.offsetWidth;   bd.classList.add('animate'); }
                if (logo) { logo.classList.remove('animate'); void logo.offsetWidth; logo.classList.add('animate'); }
            }
        });
        var dots = dotsEl ? dotsEl.querySelectorAll('.dot') : [];
        dots.forEach(function (d, i) { d.classList.toggle('active', i === index); });
        currentIndex = index;
    }

    function next() {
        animateSlide((currentIndex + 1) % slides.length);
    }

    function prev() {
        animateSlide((currentIndex - 1 + slides.length) % slides.length);
    }

    function goTo(i) {
        animateSlide(i);
    }

    function resetTimer() {
        if (timer) clearInterval(timer);
        if (paused) return;
        timer = setInterval(function () { next(); }, INTERVAL_MS);
    }

    function init() {
        if (initialized) return;
        if (document.getElementById('slides-container')) return;

        fetch(API_BASE + '/items')
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (!data.items || data.items.length === 0) {
                    console.warn('[media-bar] No items returned from server mod');
                    return;
                }
                buildUI(data.items);
            })
            .catch(function (err) {
                console.error('[media-bar] Failed to load items:', err);
            });
    }

    function waitForHome() {
        var attempts = 0;
        var poll = setInterval(function () {
            attempts++;
            var homeSections = document.querySelector('.homeSectionsContainer');
            if (homeSections) {
                clearInterval(poll);
                init();
            }
            if (attempts > 100) clearInterval(poll);
        }, 300);
    }

    var lastPath = '';
    var navObserver = new MutationObserver(function () {
        var path = window.location.hash || window.location.pathname;
        if (path !== lastPath) {
            lastPath = path;
            if (path.includes('home') || path === '/' || path === '' || path === '#/home.html') {
                initialized = false;
                if (timer) { clearInterval(timer); timer = null; }
                waitForHome();
            }
        }
    });
    navObserver.observe(document.body, { childList: true, subtree: true });

    waitForHome();
})();
