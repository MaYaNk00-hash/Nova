using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Nova.Core;
using Nova.Events;

namespace Nova.Media;

/// <summary>
/// Production Windows media integration using Windows.Media.Control GSMTC APIs.
/// Fully offline, local, event-driven, with safe WinRT COM lifecycle and capability checks.
/// </summary>
public sealed class WindowsMediaService : IMediaService
{
    private readonly EventEngine? _eventEngine;
    private readonly object _syncLock = new();

    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private MediaSessionSnapshot _currentSnapshot = MediaSessionSnapshot.Empty;
    private bool _isDisposed;
    private bool _isUpdating;

    public event EventHandler<MediaSessionSnapshot>? SnapshotChanged;

    public MediaSessionSnapshot CurrentSnapshot
    {
        get
        {
            lock (_syncLock)
            {
                return _currentSnapshot;
            }
        }
        private set
        {
            lock (_syncLock)
            {
                _currentSnapshot = value;
            }
            SnapshotChanged?.Invoke(this, value);
        }
    }

    public WindowsMediaService(EventEngine? eventEngine = null)
    {
        _eventEngine = eventEngine;
    }

    public async Task InitializeAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WindowsMediaService));

        try
        {
            AppLogger.Info("Initializing WindowsMediaService via GSMTC SessionManager...");
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                _sessionManager.SessionsChanged += OnSessionsChanged;

                await UpdateCurrentSessionAsync();
                AppLogger.Info("WindowsMediaService initialized successfully.");
            }
            else
            {
                AppLogger.Warning("GSMTC SessionManager returned null (media service may be unavailable on this platform).");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to initialize WindowsMediaService", ex);
        }
    }

    private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        try
        {
            await UpdateCurrentSessionAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error handling CurrentSessionChanged: {ex.Message}");
        }
    }

    private async void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        try
        {
            await UpdateCurrentSessionAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error handling SessionsChanged: {ex.Message}");
        }
    }

    public async Task RefreshAsync()
    {
        if (_isDisposed) return;
        await UpdateCurrentSessionAsync();
    }

    private async Task UpdateCurrentSessionAsync()
    {
        if (_isDisposed || _sessionManager == null) return;

        lock (_syncLock)
        {
            if (_isUpdating) return;
            _isUpdating = true;
        }

        try
        {
            GlobalSystemMediaTransportControlsSession? newSession = null;
            try
            {
                newSession = _sessionManager.GetCurrentSession();
                if (newSession == null)
                {
                    var all = _sessionManager.GetSessions();
                    newSession = all?.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Failed to query active GSMTC session: {ex.Message}");
            }

            // If session changed, unbind old session and bind new session
            GlobalSystemMediaTransportControlsSession? oldSessionToUnbind = null;
            lock (_syncLock)
            {
                if (!ReferenceEquals(_currentSession, newSession))
                {
                    oldSessionToUnbind = _currentSession;
                    _currentSession = newSession;

                    if (newSession != null)
                    {
                        try
                        {
                            newSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                            newSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warning($"Failed to hook session events: {ex.Message}");
                        }
                    }
                }
            }

            if (oldSessionToUnbind != null)
            {
                try
                {
                    oldSessionToUnbind.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    oldSessionToUnbind.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                }
                catch
                {
                    // Ignore unbind errors
                }
            }

            await FetchSnapshotFromSessionAsync(newSession);
        }
        finally
        {
            lock (_syncLock)
            {
                _isUpdating = false;
            }
        }
    }

    private async void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        try
        {
            await FetchSnapshotFromSessionAsync(sender);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error handling PlaybackInfoChanged: {ex.Message}");
        }
    }

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        try
        {
            await FetchSnapshotFromSessionAsync(sender);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error handling MediaPropertiesChanged: {ex.Message}");
        }
    }

    private async Task FetchSnapshotFromSessionAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_isDisposed) return;

        if (session == null)
        {
            var previous = CurrentSnapshot;
            CurrentSnapshot = MediaSessionSnapshot.Empty;

            if (previous.HasActiveSession && _eventEngine != null)
            {
                _eventEngine.Publish(NovaEvent.CreateMediaEvent(
                    eventType: "media.session_closed",
                    title: "Media session closed",
                    description: "No active media session",
                    payload: CurrentSnapshot,
                    priority: EventPriority.Low));
            }
            return;
        }

        try
        {
            var appId = string.Empty;
            try
            {
                appId = session.SourceAppUserModelId ?? string.Empty;
            }
            catch
            {
                // SourceAppUserModelId can throw if session terminated
            }

            var displayName = MediaSessionSnapshot.FormatSourceDisplayName(appId);

            // Playback Info & Controls
            var playbackStatus = MediaPlaybackStatus.Unknown;
            var capabilities = MediaCapabilities.None;

            try
            {
                var playbackInfo = session.GetPlaybackInfo();
                if (playbackInfo != null)
                {
                    playbackStatus = playbackInfo.PlaybackStatus switch
                    {
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackStatus.Closed,
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackStatus.Opened,
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackStatus.Changing,
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackStatus.Stopped,
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
                        _ => MediaPlaybackStatus.Unknown
                    };

                    var controls = playbackInfo.Controls;
                    if (controls != null)
                    {
                        capabilities = new MediaCapabilities
                        {
                            CanPlay = controls.IsPlayEnabled,
                            CanPause = controls.IsPauseEnabled,
                            CanTogglePlayPause = controls.IsPlayPauseToggleEnabled || (controls.IsPlayEnabled && controls.IsPauseEnabled),
                            CanNext = controls.IsNextEnabled,
                            CanPrevious = controls.IsPreviousEnabled,
                            CanStop = controls.IsStopEnabled
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Error reading playback info: {ex.Message}");
            }

            // Track Properties
            var trackInfo = MediaTrackInfo.Empty;
            try
            {
                var mediaProps = await session.TryGetMediaPropertiesAsync();
                if (mediaProps != null)
                {
                    byte[]? artwork = null;
                    if (mediaProps.Thumbnail != null)
                    {
                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            artwork = await LoadArtworkBytesAsync(mediaProps.Thumbnail, cts.Token);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warning($"Could not extract media thumbnail: {ex.Message}");
                        }
                    }

                    trackInfo = new MediaTrackInfo
                    {
                        Title = mediaProps.Title ?? string.Empty,
                        Artist = mediaProps.Artist ?? string.Empty,
                        AlbumTitle = mediaProps.AlbumTitle ?? string.Empty,
                        AlbumArtist = mediaProps.AlbumArtist ?? string.Empty,
                        TrackNumber = mediaProps.TrackNumber,
                        AlbumTrackCount = mediaProps.AlbumTrackCount,
                        Genres = mediaProps.Genres?.ToArray() ?? Array.Empty<string>(),
                        ArtworkBytes = artwork
                    };
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Error reading media properties: {ex.Message}");
            }

            // Identify YouTube Music when playing from a browser tab or web app wrapper
            if (displayName == "Microsoft Edge" || displayName == "Google Chrome" || displayName == "Brave" || displayName == "Mozilla Firefox" || displayName == "Unknown Player")
            {
                if (trackInfo.AlbumTitle.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase) ||
                    trackInfo.Artist.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase) ||
                    trackInfo.Title.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase) ||
                    appId.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    appId.Contains("YouTubeMusic", StringComparison.OrdinalIgnoreCase))
                {
                    displayName = "YouTube Music";
                }
            }

            var previousSnapshot = CurrentSnapshot;
            var newSnapshot = new MediaSessionSnapshot
            {
                SourceAppId = appId,
                SourceDisplayName = displayName,
                PlaybackStatus = playbackStatus,
                TrackInfo = trackInfo,
                Capabilities = capabilities,
                HasActiveSession = true,
                Timestamp = DateTimeOffset.UtcNow
            };

            CurrentSnapshot = newSnapshot;

            // Publish relevant events via EventEngine
            if (_eventEngine != null)
            {
                if (!previousSnapshot.HasActiveSession)
                {
                    _eventEngine.Publish(NovaEvent.CreateMediaEvent(
                        eventType: "media.session_detected",
                        title: $"Media: {displayName}",
                        description: trackInfo.DisplayString,
                        payload: newSnapshot));
                }

                if (previousSnapshot.TrackInfo.Title != trackInfo.Title ||
                    previousSnapshot.TrackInfo.Artist != trackInfo.Artist)
                {
                    _eventEngine.Publish(NovaEvent.CreateMediaEvent(
                        eventType: "media.track_changed",
                        title: $"Now Playing: {trackInfo.Title}",
                        description: trackInfo.Artist,
                        payload: newSnapshot,
                        deduplicationKey: $"media.track.{appId}.{trackInfo.Title}.{trackInfo.Artist}"));
                }

                if (previousSnapshot.PlaybackStatus != playbackStatus)
                {
                    _eventEngine.Publish(NovaEvent.CreateMediaEvent(
                        eventType: "media.playback_changed",
                        title: $"Media: {playbackStatus}",
                        description: trackInfo.DisplayString,
                        payload: newSnapshot,
                        deduplicationKey: $"media.status.{appId}.{playbackStatus}"));
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unexpected error reading GSMTC snapshot", ex);
        }
    }

    private static async Task<byte[]?> LoadArtworkBytesAsync(IRandomAccessStreamReference thumbnailRef, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await thumbnailRef.OpenReadAsync().AsTask(cancellationToken);
            if (stream == null || stream.Size == 0) return null;

            // Cap at 5 MB for safety
            if (stream.Size > 5 * 1024 * 1024)
            {
                AppLogger.Warning($"Artwork stream too large ({stream.Size} bytes), skipping.");
                return null;
            }

            using var netStream = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await netStream.CopyToAsync(ms, cancellationToken);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to load artwork bytes: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> TryPlayAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WindowsMediaService));

        GlobalSystemMediaTransportControlsSession? session;
        lock (_syncLock)
        {
            session = _currentSession;
        }

        if (session == null || !CurrentSnapshot.Capabilities.CanPlay)
            return false;

        try
        {
            return await session.TryPlayAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"TryPlayAsync failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TryPauseAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WindowsMediaService));

        GlobalSystemMediaTransportControlsSession? session;
        lock (_syncLock)
        {
            session = _currentSession;
        }

        if (session == null || !CurrentSnapshot.Capabilities.CanPause)
            return false;

        try
        {
            return await session.TryPauseAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"TryPauseAsync failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TryTogglePlayPauseAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WindowsMediaService));

        GlobalSystemMediaTransportControlsSession? session;
        lock (_syncLock)
        {
            session = _currentSession;
        }

        if (session == null) return false;

        try
        {
            if (CurrentSnapshot.Capabilities.CanTogglePlayPause)
            {
                return await session.TryTogglePlayPauseAsync();
            }

            if (CurrentSnapshot.PlaybackStatus == MediaPlaybackStatus.Playing && CurrentSnapshot.Capabilities.CanPause)
            {
                return await session.TryPauseAsync();
            }

            if (CurrentSnapshot.PlaybackStatus != MediaPlaybackStatus.Playing && CurrentSnapshot.Capabilities.CanPlay)
            {
                return await session.TryPlayAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"TryTogglePlayPauseAsync failed: {ex.Message}");
        }

        return false;
    }

    public async Task<bool> TryNextAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WindowsMediaService));

        GlobalSystemMediaTransportControlsSession? session;
        lock (_syncLock)
        {
            session = _currentSession;
        }

        if (session == null || !CurrentSnapshot.Capabilities.CanNext)
            return false;

        try
        {
            return await session.TrySkipNextAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"TryNextAsync failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TryPreviousAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WindowsMediaService));

        GlobalSystemMediaTransportControlsSession? session;
        lock (_syncLock)
        {
            session = _currentSession;
        }

        if (session == null || !CurrentSnapshot.Capabilities.CanPrevious)
            return false;

        try
        {
            return await session.TrySkipPreviousAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"TryPreviousAsync failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        try
        {
            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _sessionManager.SessionsChanged -= OnSessionsChanged;
                _sessionManager = null;
            }

            if (_currentSession != null)
            {
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession = null;
            }

            AppLogger.Info("WindowsMediaService disposed cleanly.");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error during WindowsMediaService disposal: {ex.Message}");
        }
    }
}
