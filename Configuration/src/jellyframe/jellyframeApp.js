(function () {
    var currentConfig = null;

    function initPage() {
        el('saveStatus').textContent = '';
        el('feedbackAlert').classList.remove('show');
        el('btnSaveConfig').disabled = true;

        ApiClient.getPluginConfiguration(PLUGIN_ID).then(function (config) {
            currentConfig = config;

            var switchContainer = el('loomToggle');
            if (config.UseLoomInjector) {
                switchContainer.classList.add('checked');
            } else {
                switchContainer.classList.remove('checked');
            }

            el('btnSaveConfig').disabled = false;
        }).catch(function () {
            el('saveStatus').textContent = 'Failed to load configuration.';
            el('saveStatus').style.color = '#ef5350';
        });
    }

    // Bind Toggle Event
    el('loomToggle').addEventListener('click', function () {
        this.classList.toggle('checked');
    });

    // Bind Save Event
    el('btnSaveConfig').addEventListener('click', function () {
        if (!currentConfig) return;

        var btn = this;
        btn.disabled = true;
        el('saveStatus').textContent = 'Saving...';
        el('saveStatus').style.color = '';
        el('feedbackAlert').classList.remove('show');

        var isLoomChecked = el('loomToggle').classList.contains('checked');

        var updatedConfig = Object.assign({}, currentConfig, {
            UseLoomInjector: isLoomChecked
        });

        ApiClient.updatePluginConfiguration(PLUGIN_ID, updatedConfig).then(function () {
            currentConfig = updatedConfig;
            btn.disabled = false;
            el('saveStatus').textContent = 'Saved successfully.';
            el('saveStatus').style.color = '#66bb6a';

            // Show MUI Alert notifying about restart
            var alertEl = el('feedbackAlert');
            alertEl.className = 'mui-alert mui-alert-warning show';
            el('alertMessage').textContent = 'Configuration saved successfully! A restart of the Jellyfin server is required to swap the frontend injection method.';

            Dashboard.processPluginConfigurationUpdateResult();
        }).catch(function () {
            btn.disabled = false;
            el('saveStatus').textContent = 'Failed to save configuration.';
            el('saveStatus').style.color = '#ef5350';
        });
    });

    // Bind page initialization to viewshow
    var pageEl = document.querySelector('.jfConfigPage');
    if (pageEl) {
        pageEl.addEventListener('viewshow', initPage);
    }
})();
