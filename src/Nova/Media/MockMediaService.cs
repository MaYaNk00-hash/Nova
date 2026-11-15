using System;
using System.Threading.Tasks;
using Nova.Events;

namespace Nova.Media;

/// <summary>
/// Mock media service for deterministic automated testing of media UI and event integration.
/// </summary>
public sealed class MockMediaService : IMediaService
{
    private readonly EventEngine? _eventEngine;
    private MediaSessionSnapshot _snapshot = MediaSessionSnapshot.Empty;
    private bool _isDisposed;

    public event EventHandler<MediaSessionSnapshot>? SnapshotChanged;

    public int PlayCallCount { get; private set; }
    public int PauseCallCount { get; private set; }
    public int TogglePlayPauseCallCount { get; private set; }
    public int NextCallCount { get; private set; }
    public int PreviousCallCount { get; private set; }
    public bool IsDisposed => _isDisposed;

    public MockMediaService(EventEngine? eventEngine = null)
    {
        _eventEngine = eventEngine;
    }

    public MediaSessionSnapshot CurrentSnapshot => _snapshot;

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public void SetSnapshot(MediaSessionSnapshot snapshot)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(MockMediaService));

        var previous = _snapshot;
        _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);

        if (_eventEngine != null)
        {
            if (!previous.HasActiveSession && snapshot.HasActiveSession)
            {
                _eventEngine.Publish(NovaEvent.CreateMediaEvent(
                    eventType: "media.session_detected",
                    title: $"Media detected: {snapshot.SourceDisplayName}",
                    description: snapshot.TrackInfo.DisplayString,
                    payload: snapshot));
            }
            else if (previous.HasActiveSession && !snapshot.HasActiveSession)
            {
                _eventEngine.Publish(NovaEvent.CreateMediaEvent(
                    eventType: "media.session_closed",
                    title: "Media session closed",
                    description: "No active media sessions",
                    payload: snapshot,
                    priority: EventPriority.Low));
            }

            if (previous.TrackInfo != snapshot.TrackInfo)
            {
                _eventEngine.Publish(NovaEvent.CreateMediaEvent(
                    eventType: "media.track_changed",
                    title: $"Now Playing: {snapshot.TrackInfo.Title}",
                    description: snapshot.TrackInfo.Artist,
                    payload: snapshot));
            }

            if (previous.PlaybackStatus != snapshot.PlaybackStatus)
            {
                _eventEngine.Publish(NovaEvent.CreateMediaEvent(
                    eventType: "media.playback_changed",
                    title: $"Playback: {snapshot.PlaybackStatus}",
                    description: snapshot.TrackInfo.DisplayString,
                    payload: snapshot));
            }
        }
    }

    public Task<bool> TryPlayAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(MockMediaService));
        PlayCallCount++;

        if (!_snapshot.Capabilities.CanPlay)
            return Task.FromResult(false);

        SetSnapshot(_snapshot with { PlaybackStatus = MediaPlaybackStatus.Playing });
        return Task.FromResult(true);
    }

    public Task<bool> TryPauseAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(MockMediaService));
        PauseCallCount++;

        if (!_snapshot.Capabilities.CanPause)
            return Task.FromResult(false);

        SetSnapshot(_snapshot with { PlaybackStatus = MediaPlaybackStatus.Paused });
        return Task.FromResult(true);
    }

    public Task<bool> TryTogglePlayPauseAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(MockMediaService));
        TogglePlayPauseCallCount++;

        if (!_snapshot.Capabilities.CanTogglePlayPause)
            return Task.FromResult(false);

        var newStatus = _snapshot.PlaybackStatus == MediaPlaybackStatus.Playing
            ? MediaPlaybackStatus.Paused
            : MediaPlaybackStatus.Playing;

        SetSnapshot(_snapshot with { PlaybackStatus = newStatus });
        return Task.FromResult(true);
    }

    public Task<bool> TryNextAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(MockMediaService));
        NextCallCount++;

        if (!_snapshot.Capabilities.CanNext)
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    public Task<bool> TryPreviousAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(MockMediaService));
        PreviousCallCount++;

        if (!_snapshot.Capabilities.CanPrevious)
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    public Task RefreshAsync()
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _isDisposed = true;
    }
}

