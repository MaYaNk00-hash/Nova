using System;
using System.Collections.Generic;

namespace Nova.Media;

/// <summary>
/// Immutable representation of media track metadata.
/// </summary>
public sealed record MediaTrackInfo
{
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string AlbumTitle { get; init; } = string.Empty;
    public string AlbumArtist { get; init; } = string.Empty;
    public int TrackNumber { get; init; }
    public int AlbumTrackCount { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
    public byte[]? ArtworkBytes { get; init; }

    public bool HasTrack => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist);
    public bool HasArtwork => ArtworkBytes != null && ArtworkBytes.Length > 0;

    /// <summary>
    /// Friendly display string combining Title and Artist.
    /// </summary>
    public string DisplayString
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Artist))
                return $"{Title} • {Artist}";
            if (!string.IsNullOrWhiteSpace(Title))
                return Title;
            if (!string.IsNullOrWhiteSpace(Artist))
                return Artist;
            return "Unknown Track";
        }
    }

    public static MediaTrackInfo Empty { get; } = new();
}

