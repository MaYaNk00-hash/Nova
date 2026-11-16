using System;
using System.IO;

namespace Nova.Media;

/// <summary>
/// Immutable snapshot of the current Windows media session state.
/// </summary>
public sealed record MediaSessionSnapshot
{
    public string SourceAppId { get; init; } = string.Empty;
    public string SourceDisplayName { get; init; } = string.Empty;
    public MediaPlaybackStatus PlaybackStatus { get; init; } = MediaPlaybackStatus.Unknown;
    public MediaTrackInfo TrackInfo { get; init; } = MediaTrackInfo.Empty;
    public MediaCapabilities Capabilities { get; init; } = MediaCapabilities.None;
    public bool HasActiveSession { get; init; }
    public TimeSpan Position { get; init; } = TimeSpan.Zero;
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public bool IsPlaying => PlaybackStatus == MediaPlaybackStatus.Playing;

    /// <summary>
    /// Indicates whether the active session originated from YouTube Music (web tab, PWA, or desktop app).
    /// </summary>
    public bool IsYouTubeMusic =>
        SourceDisplayName.Equals("YouTube Music", StringComparison.OrdinalIgnoreCase) ||
        SourceAppId.Contains("YouTubeMusic", StringComparison.OrdinalIgnoreCase) ||
        SourceAppId.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase) ||
        TrackInfo.AlbumTitle.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase) ||
        TrackInfo.Title.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase) ||
        TrackInfo.Artist.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase);

    public static MediaSessionSnapshot Empty { get; } = new()
    {
        SourceAppId = string.Empty,
        SourceDisplayName = "No Media Player",
        PlaybackStatus = MediaPlaybackStatus.Closed,
        TrackInfo = MediaTrackInfo.Empty,
        Capabilities = MediaCapabilities.None,
        HasActiveSession = false
    };

    /// <summary>
    /// Parses a raw Windows Application User Model ID or executable path into a friendly display name.
    /// </summary>
    public static string FormatSourceDisplayName(string? rawAppId)
    {
        if (string.IsNullOrWhiteSpace(rawAppId))
            return "Unknown Player";

        var id = rawAppId.Trim();

        // Check common apps
        if (id.Contains("YouTubeMusic", StringComparison.OrdinalIgnoreCase) || 
            id.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase) || 
            id.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase))
            return "YouTube Music";
        if (id.Contains("YouTube", StringComparison.OrdinalIgnoreCase))
            return "YouTube";
        if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
            return "Spotify";
        if (id.Contains("msedge", StringComparison.OrdinalIgnoreCase) || id.Contains("MicrosoftEdge", StringComparison.OrdinalIgnoreCase))
            return "Microsoft Edge";
        if (id.Contains("chrome", StringComparison.OrdinalIgnoreCase))
            return "Google Chrome";
        if (id.Contains("firefox", StringComparison.OrdinalIgnoreCase))
            return "Mozilla Firefox";
        if (id.Contains("brave", StringComparison.OrdinalIgnoreCase))
            return "Brave";
        if (id.Contains("wmplayer", StringComparison.OrdinalIgnoreCase) || id.Contains("ZuneMusic", StringComparison.OrdinalIgnoreCase) || id.Contains("Media Player", StringComparison.OrdinalIgnoreCase))
            return "Media Player";
        if (id.Contains("iTunes", StringComparison.OrdinalIgnoreCase) || id.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase))
            return "Apple Music";
        if (id.Contains("vlc", StringComparison.OrdinalIgnoreCase))
            return "VLC";
        if (id.Contains("foobar", StringComparison.OrdinalIgnoreCase))
            return "foobar2000";
        if (id.Contains("MusicBee", StringComparison.OrdinalIgnoreCase))
            return "MusicBee";
        if (id.Contains("TIDAL", StringComparison.OrdinalIgnoreCase))
            return "TIDAL";
        if (id.Contains("Deezer", StringComparison.OrdinalIgnoreCase))
            return "Deezer";

        // Try extracting clean executable name or app name
        try
        {
            if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var withoutExt = Path.GetFileNameWithoutExtension(id);
                if (!string.IsNullOrWhiteSpace(withoutExt))
                    return char.ToUpperInvariant(withoutExt[0]) + withoutExt.Substring(1);
            }

            // AUMID format: PackageFamilyName!AppId
            var bangIndex = id.IndexOf('!');
            if (bangIndex > 0 && bangIndex < id.Length - 1)
            {
                var appPart = id.Substring(bangIndex + 1);
                return appPart;
            }
        }
        catch
        {
            // Fall back to raw id
        }

        return id;
    }
}

