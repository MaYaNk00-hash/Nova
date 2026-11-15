namespace Nova.Media;

/// <summary>
/// Advertised playback control capabilities of a media session.
/// Controls should only be executed if the session explicitly advertises support.
/// </summary>
public sealed record MediaCapabilities
{
    public bool CanPlay { get; init; }
    public bool CanPause { get; init; }
    public bool CanTogglePlayPause { get; init; }
    public bool CanNext { get; init; }
    public bool CanPrevious { get; init; }
    public bool CanStop { get; init; }

    public static MediaCapabilities None { get; } = new();

    public static MediaCapabilities All { get; } = new()
    {
        CanPlay = true,
        CanPause = true,
        CanTogglePlayPause = true,
        CanNext = true,
        CanPrevious = true,
        CanStop = true
    };
}

