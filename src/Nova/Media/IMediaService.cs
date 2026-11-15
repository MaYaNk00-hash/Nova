using System;
using System.Threading.Tasks;

namespace Nova.Media;

/// <summary>
/// Abstraction for Windows media playback detection and transport controls.
/// </summary>
public interface IMediaService : IDisposable
{
    /// <summary>
    /// Current snapshot of media playback state.
    /// </summary>
    MediaSessionSnapshot CurrentSnapshot { get; }

    /// <summary>
    /// Fired whenever track info, playback state, or active session changes.
    /// </summary>
    event EventHandler<MediaSessionSnapshot>? SnapshotChanged;

    /// <summary>
    /// Initializes media monitoring and hooks session events.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Attempts to start or resume playback on active session if supported.
    /// </summary>
    Task<bool> TryPlayAsync();

    /// <summary>
    /// Attempts to pause playback on active session if supported.
    /// </summary>
    Task<bool> TryPauseAsync();

    /// <summary>
    /// Attempts to toggle play/pause on active session if supported.
    /// </summary>
    Task<bool> TryTogglePlayPauseAsync();

    /// <summary>
    /// Attempts to skip to the next track on active session if supported.
    /// </summary>
    Task<bool> TryNextAsync();

    /// <summary>
    /// Attempts to skip to the previous track on active session if supported.
    /// </summary>
    Task<bool> TryPreviousAsync();

    /// <summary>
    /// Forces a manual refresh of current session state and metadata.
    /// </summary>
    Task RefreshAsync();
}

