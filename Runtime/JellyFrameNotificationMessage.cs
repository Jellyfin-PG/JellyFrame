using System;
using MediaBrowser.Controller.Net.WebSocketMessages;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    /// <summary>
    /// Data payload carried inside a JellyFrameNotification WebSocket message.
    /// </summary>
    public sealed class JellyFrameNotificationData
    {
        /// <summary>Short notification title.</summary>
        public string Title   { get; init; }

        /// <summary>Notification body text.</summary>
        public string Body    { get; init; }

        /// <summary>
        /// Hint for the browser mod renderer — e.g. "info", "success", "warning", "error".
        /// The JellyFrame runtime does not enforce this; it is purely advisory for the
        /// browser-side handler.
        /// </summary>
        public string Type    { get; init; }

        /// <summary>
        /// Arbitrary extra data the mod wants to pass to the browser-side handler.
        /// Serialised as-is into the JSON envelope.
        /// </summary>
        public object Data    { get; init; }
    }

    /// <summary>
    /// Custom outbound WebSocket message that carries a <see cref="JellyFrameNotificationData"/>
    /// payload to browser mod scripts.
    ///
    /// The Jellyfin web client ignores unknown MessageType values, so this passes
    /// through silently. Browser mods listen for it with:
    ///
    /// <code>
    /// ApiClient.addEventListener('message', function(e, msg) {
    ///   if (msg.MessageType === 'JellyFrameNotification') {
    ///     var d = msg.Data; // { Title, Body, Type, Data }
    ///   }
    /// });
    /// </code>
    /// </summary>
    public sealed class JellyFrameNotificationMessage : OutboundWebSocketMessage
    {
        /// <summary>
        /// Fixed type string that browser mods match against.
        /// Not a <c>SessionMessageType</c> enum value — the Jellyfin client
        /// simply ignores it, while browser mod JS handles it explicitly.
        /// </summary>
        public new string MessageType => "JellyFrameNotification";

        /// <summary>The notification payload.</summary>
        public JellyFrameNotificationData Data { get; }

        public JellyFrameNotificationMessage(
            string title, string body, string type = "info", object extra = null)
        {
            MessageId = Guid.NewGuid();
            Data = new JellyFrameNotificationData
            {
                Title = title,
                Body  = body,
                Type  = type,
                Data  = extra
            };
        }
    }
}
