namespace Jellyfin.Plugin.JellyFrame.Services
{
    /// <summary>
    /// Local mirror of File Transformation's PatchRequestPayload.
    /// File Transformation deserializes its own type into this shape before
    /// invoking the callback via reflection.
    /// </summary>
    public class PatchRequestPayload
    {
        public string Contents { get; set; }
    }
}
