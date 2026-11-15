namespace Nova.Media;

/// <summary>
/// Status of a Windows media transport controls session.
/// Aligned with Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.
/// </summary>
public enum MediaPlaybackStatus
{
    Unknown = 0,
    Closed = 1,
    Opened = 2,
    Changing = 3,
    Stopped = 4,
    Playing = 5,
    Paused = 6
}

