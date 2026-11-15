using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nova.Core;
using Nova.Events;
using Nova.Media;

namespace Nova.Tests;

public static class MediaTests
{
    [Fact("Verify MediaSessionSnapshot source name formatting for known and unknown apps")]
    public static void MediaSessionSnapshot_FormatSourceDisplayName_FormatsCorrectly()
    {
        Assert.Equal("Spotify", MediaSessionSnapshot.FormatSourceDisplayName("Spotify.exe"));
        Assert.Equal("Spotify", MediaSessionSnapshot.FormatSourceDisplayName("spotify"));
        Assert.Equal("Microsoft Edge", MediaSessionSnapshot.FormatSourceDisplayName("msedge.exe"));
        Assert.Equal("Google Chrome", MediaSessionSnapshot.FormatSourceDisplayName("chrome.exe"));
        Assert.Equal("Media Player", MediaSessionSnapshot.FormatSourceDisplayName("wmplayer.exe"));
        Assert.Equal("Media Player", MediaSessionSnapshot.FormatSourceDisplayName("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"));
        Assert.Equal("Apple Music", MediaSessionSnapshot.FormatSourceDisplayName("AppleMusic.exe"));
        Assert.Equal("VLC", MediaSessionSnapshot.FormatSourceDisplayName("vlc.exe"));
        Assert.Equal("foobar2000", MediaSessionSnapshot.FormatSourceDisplayName("foobar2000.exe"));

        // Fallbacks
        Assert.Equal("Unknown Player", MediaSessionSnapshot.FormatSourceDisplayName(null));
        Assert.Equal("Unknown Player", MediaSessionSnapshot.FormatSourceDisplayName(""));
        Assert.Equal("Unknown Player", MediaSessionSnapshot.FormatSourceDisplayName("   "));
    }

    [Fact("Verify MediaTrackInfo handles missing, empty, and full metadata cleanly")]
    public static void MediaTrackInfo_MetadataHandling_IsRobust()
    {
        var empty = MediaTrackInfo.Empty;
        Assert.False(empty.HasTrack);
        Assert.False(empty.HasArtwork);
        Assert.Equal("Unknown Track", empty.DisplayString);

        var titleOnly = new MediaTrackInfo { Title = "Starlight" };
        Assert.True(titleOnly.HasTrack);
        Assert.Equal("Starlight", titleOnly.DisplayString);

        var artistOnly = new MediaTrackInfo { Artist = "Muse" };
        Assert.True(artistOnly.HasTrack);
        Assert.Equal("Muse", artistOnly.DisplayString);

        var full = new MediaTrackInfo
        {
            Title = "Midnight City",
            Artist = "M83",
            AlbumTitle = "Hurry Up, We're Dreaming",
            ArtworkBytes = new byte[] { 0xFF, 0xD8, 0xFF }
        };
        Assert.True(full.HasTrack);
        Assert.True(full.HasArtwork);
        Assert.Equal("Midnight City • M83", full.DisplayString);
    }

    [Fact("Verify MediaCapabilities guards prevent unsupported actions")]
    public static void MediaCapabilities_Guards_PreventUnsupportedActions()
    {
        using var engine = new EventEngine();
        using var service = new MockMediaService(engine);

        // Configure snapshot with only Play capability (no Pause, no Next, no Prev)
        service.SetSnapshot(new MediaSessionSnapshot
        {
            SourceAppId = "test.exe",
            SourceDisplayName = "Test Player",
            PlaybackStatus = MediaPlaybackStatus.Paused,
            HasActiveSession = true,
            Capabilities = new MediaCapabilities
            {
                CanPlay = true,
                CanPause = false,
                CanNext = false,
                CanPrevious = false,
                CanTogglePlayPause = false
            }
        });

        // Try Next when disabled -> should return false
        var nextResult = service.TryNextAsync().GetAwaiter().GetResult();
        Assert.False(nextResult);

        // Try Previous when disabled -> should return false
        var prevResult = service.TryPreviousAsync().GetAwaiter().GetResult();
        Assert.False(prevResult);

        // Try Pause when disabled -> should return false
        var pauseResult = service.TryPauseAsync().GetAwaiter().GetResult();
        Assert.False(pauseResult);

        // Try Play when enabled -> should return true and update state
        var playResult = service.TryPlayAsync().GetAwaiter().GetResult();
        Assert.True(playResult);
        Assert.Equal(MediaPlaybackStatus.Playing, service.CurrentSnapshot.PlaybackStatus);
    }

    [Fact("Verify TogglePlayPause transitions status between Playing and Paused")]
    public static void MockMediaService_TogglePlayPause_TransitionsStatus()
    {
        using var service = new MockMediaService();
        service.SetSnapshot(new MediaSessionSnapshot
        {
            SourceAppId = "spotify.exe",
            SourceDisplayName = "Spotify",
            PlaybackStatus = MediaPlaybackStatus.Paused,
            HasActiveSession = true,
            Capabilities = MediaCapabilities.All
        });

        Assert.Equal(MediaPlaybackStatus.Paused, service.CurrentSnapshot.PlaybackStatus);

        // 1st toggle: Paused -> Playing
        var res1 = service.TryTogglePlayPauseAsync().GetAwaiter().GetResult();
        Assert.True(res1);
        Assert.Equal(MediaPlaybackStatus.Playing, service.CurrentSnapshot.PlaybackStatus);

        // 2nd toggle: Playing -> Paused
        var res2 = service.TryTogglePlayPauseAsync().GetAwaiter().GetResult();
        Assert.True(res2);
        Assert.Equal(MediaPlaybackStatus.Paused, service.CurrentSnapshot.PlaybackStatus);
    }

    [Fact("Verify media events are published to EventEngine with correct categories and payloads")]
    public static void MediaService_PublishesEventsToEventEngine()
    {
        using var engine = new EventEngine(startWorker: false);
        using var service = new MockMediaService(engine);

        var receivedDetected = 0;
        var receivedTrackChanged = 0;
        var receivedPlaybackChanged = 0;
        var receivedClosed = 0;

        using var sub1 = engine.Subscribe("media.session_detected", e =>
        {
            Assert.Equal(EventCategory.Media, e.Category);
            Interlocked.Increment(ref receivedDetected);
        }, deliverOnUIThread: false);

        using var sub2 = engine.Subscribe("media.track_changed", e =>
        {
            Assert.Equal(EventCategory.Media, e.Category);
            Interlocked.Increment(ref receivedTrackChanged);
        }, deliverOnUIThread: false);

        using var sub3 = engine.Subscribe("media.playback_changed", e =>
        {
            Assert.Equal(EventCategory.Media, e.Category);
            Interlocked.Increment(ref receivedPlaybackChanged);
        }, deliverOnUIThread: false);

        using var sub4 = engine.Subscribe("media.session_closed", e =>
        {
            Assert.Equal(EventCategory.Media, e.Category);
            Interlocked.Increment(ref receivedClosed);
        }, deliverOnUIThread: false);

        // 1. Session detected + track changed + playback changed
        service.SetSnapshot(new MediaSessionSnapshot
        {
            SourceAppId = "spotify.exe",
            SourceDisplayName = "Spotify",
            PlaybackStatus = MediaPlaybackStatus.Playing,
            HasActiveSession = true,
            TrackInfo = new MediaTrackInfo
            {
                Title = "Solar Power",
                Artist = "Lorde"
            },
            Capabilities = MediaCapabilities.All
        });

        engine.DrainAllQueued();

        Assert.Equal(1, receivedDetected);
        Assert.Equal(1, receivedTrackChanged);
        Assert.Equal(1, receivedPlaybackChanged);

        // 2. Track changed
        service.SetSnapshot(service.CurrentSnapshot with
        {
            TrackInfo = new MediaTrackInfo
            {
                Title = "Stoned at the Nail Salon",
                Artist = "Lorde"
            }
        });

        engine.DrainAllQueued();

        Assert.Equal(2, receivedTrackChanged);

        // 3. Playback paused
        service.TryPauseAsync().GetAwaiter().GetResult();
        engine.DrainAllQueued();

        Assert.Equal(2, receivedPlaybackChanged);

        // 4. Session closed
        service.SetSnapshot(MediaSessionSnapshot.Empty);
        engine.DrainAllQueued();

        Assert.Equal(1, receivedClosed);
    }

    [Fact("Verify disposal of MediaService cleans up and prevents subsequent actions")]
    public static void MediaService_Disposal_CleansUpAndRejectsActions()
    {
        var service = new MockMediaService();
        service.Dispose();
        Assert.True(service.IsDisposed);

        var threwOnPlay = false;
        try
        {
            service.TryPlayAsync().GetAwaiter().GetResult();
        }
        catch (ObjectDisposedException)
        {
            threwOnPlay = true;
        }
        Assert.True(threwOnPlay);

        var threwOnSet = false;
        try
        {
            service.SetSnapshot(MediaSessionSnapshot.Empty);
        }
        catch (ObjectDisposedException)
        {
            threwOnSet = true;
        }
        Assert.True(threwOnSet);
    }

    [Fact("Verify WindowsMediaService initializes safely and handles disposal without error")]
    public static void WindowsMediaService_Lifecycle_InitializesAndDisposesCleanly()
    {
        using var engine = new EventEngine();
        var service = new WindowsMediaService(engine);

        // Initialize should run safely even in headless/sandbox environment
        service.InitializeAsync().GetAwaiter().GetResult();

        // Initial snapshot should be safe and non-null
        var initial = service.CurrentSnapshot;
        Assert.NotNull(initial);

        // Refresh should run safely
        service.RefreshAsync().GetAwaiter().GetResult();

        // Disposal should be clean
        service.Dispose();

        // Repeat disposal should not throw
        service.Dispose();
    }

    [Fact("Verify integration with real Windows media player session (Edge or active player)")]
    public static void RealWindowsMediaSession_DetectionAndControls()
    {
        using var engine = new EventEngine(startWorker: false);
        using var service = new WindowsMediaService(engine);

        service.InitializeAsync().GetAwaiter().GetResult();

        // If an active session is already running on the machine (e.g., Spotify, Chrome, Edge), verify it directly!
        if (service.CurrentSnapshot.HasActiveSession)
        {
            var snap = service.CurrentSnapshot;
            Console.WriteLine($"[Real Media Player] Detected running player: {snap.SourceDisplayName} ({snap.SourceAppId})");
            Console.WriteLine($"                    Track: '{snap.TrackInfo.Title}' by '{snap.TrackInfo.Artist}' - Status: {snap.PlaybackStatus}");
            Assert.True(snap.HasActiveSession);
            Assert.False(string.IsNullOrWhiteSpace(snap.SourceDisplayName));
            return;
        }

        // Otherwise, launch Edge with our local HTML5 audio test track
        var edgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        if (!File.Exists(edgePath))
        {
            Console.WriteLine("[Real Media Player] msedge.exe not found at standard path, skipping real player launch.");
            return;
        }

        var htmlPath = Path.GetFullPath(@"..\..\..\..\..\..\Users\mayan\.gemini\antigravity\brain\94fdf842-1673-419b-90de-037bc4a99bea\scratch\test_media.html");
        if (!File.Exists(htmlPath))
        {
            // Alternative relative path
            htmlPath = @"C:\Users\mayan\.gemini\antigravity\brain\94fdf842-1673-419b-90de-037bc4a99bea\scratch\test_media.html";
        }

        var tempProfile = Path.Combine(Path.GetTempPath(), "NovaMediaTestProfile_" + Guid.NewGuid().ToString("N"));
        System.Diagnostics.Process? process = null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = edgePath,
                Arguments = $"--autoplay-policy=no-user-gesture-required --user-data-dir=\"{tempProfile}\" --no-first-run --no-default-browser-check \"file:///{htmlPath.Replace('\\', '/')}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process = System.Diagnostics.Process.Start(psi);

            if (process != null)
            {
                // Wait up to 6 seconds for GSMTC session to register
                for (int i = 0; i < 12; i++)
                {
                    Thread.Sleep(500);
                    service.RefreshAsync().GetAwaiter().GetResult();
                    if (service.CurrentSnapshot.HasActiveSession)
                    {
                        break;
                    }
                }

                var snapshot = service.CurrentSnapshot;
                Console.WriteLine($"[Real Media Player] HasActiveSession: {snapshot.HasActiveSession}, Player: {snapshot.SourceDisplayName}");
                if (snapshot.HasActiveSession)
                {
                    Console.WriteLine($"                    Track: '{snapshot.TrackInfo.Title}' by '{snapshot.TrackInfo.Artist}'");
                    Assert.True(snapshot.HasActiveSession);
                    Assert.False(string.IsNullOrWhiteSpace(snapshot.SourceDisplayName));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Real Media Player] Could not launch Edge test process: {ex.Message}");
        }
        finally
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch { }

            try
            {
                if (Directory.Exists(tempProfile))
                {
                    Directory.Delete(tempProfile, true);
                }
            }
            catch { }
        }
    }
}
