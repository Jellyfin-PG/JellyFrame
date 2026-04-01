using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyFrame.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public int ConfigVersion { get; set; } = 0;

        public string ModsUrl { get; set; } = string.Empty;

        public List<string> EnabledMods { get; set; } = new List<string>();

        public string CachedMods { get; set; } = string.Empty;

        public string ModVars { get; set; } = "{}";

        public string ThemesUrl { get; set; } = string.Empty;

        public string ActiveTheme { get; set; } = string.Empty;

        public string CachedThemes { get; set; } = string.Empty;

        public string ThemeVars { get; set; } = "{}";

        public bool DebugLogging { get; set; } = false;
    }
}
