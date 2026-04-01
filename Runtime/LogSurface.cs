using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class LogSurface
    {
        private readonly ILogger _logger;
        private readonly string  _modId;

        public LogSurface(ILogger logger, string modId)
        {
            _logger = logger;
            _modId  = modId;
        }

        public void Info(string message)  => _logger.LogInformation("[Mod:{ModId}] {Message}", _modId, message);
        public void Debug(string message) => _logger.LogDebug("[Mod:{ModId}] {Message}", _modId, message);
        public void Warn(string message)  => _logger.LogWarning("[Mod:{ModId}] {Message}", _modId, message);
        public void Error(string message) => _logger.LogError("[Mod:{ModId}] {Message}", _modId, message);
    }
}
