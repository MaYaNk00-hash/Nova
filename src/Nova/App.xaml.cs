using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Nova.Core;
using Nova.UI;

namespace Nova;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IslandWindow? _islandWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLogger.Initialize();
        AppLogger.Info($"NOVA {AppInfo.Version} starting up on {Environment.OSVersion}. Architecture: {(Environment.Is64BitProcess ? "x64" : "x86")}.");

        // Global exception handling
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // Support headless/smoke-test validation
        if (e.Args.Contains("--smoke-test"))
        {
            AppLogger.Info("Smoke test flag detected: validating IslandWindow lifecycle and exiting cleanly.");
            var testWindow = new IslandWindow();
            testWindow.Show();
            testWindow.TransitionTo(IslandState.Expanded);
            testWindow.TransitionTo(IslandState.Compact);
            testWindow.Close();
            Shutdown(0);
            return;
        }

        _islandWindow = new IslandWindow();
        _islandWindow.Show();

        if (e.Args.Contains("--demo-media"))
        {
            _islandWindow.UpdateMediaUI(new Nova.Media.MediaSessionSnapshot
            {
                SourceAppId = "spotify.exe",
                SourceDisplayName = "Spotify",
                PlaybackStatus = Nova.Media.MediaPlaybackStatus.Playing,
                HasActiveSession = true,
                TrackInfo = new Nova.Media.MediaTrackInfo
                {
                    Title = "Midnight City",
                    Artist = "M83",
                    AlbumTitle = "Hurry Up, We're Dreaming"
                },
                Capabilities = Nova.Media.MediaCapabilities.All
            });
        }

        if (e.Args.Contains("--demo-ytm"))
        {
            _islandWindow.UpdateMediaUI(new Nova.Media.MediaSessionSnapshot
            {
                SourceAppId = "msedge.exe",
                SourceDisplayName = "YouTube Music",
                PlaybackStatus = Nova.Media.MediaPlaybackStatus.Playing,
                HasActiveSession = true,
                TrackInfo = new Nova.Media.MediaTrackInfo
                {
                    Title = "Blinding Lights",
                    Artist = "The Weeknd",
                    AlbumTitle = "After Hours • YouTube Music"
                },
                Capabilities = Nova.Media.MediaCapabilities.All
            });
        }

        if (e.Args.Contains("--demo-hud"))
        {
            _islandWindow.UpdateMediaUI(new Nova.Media.MediaSessionSnapshot
            {
                SourceAppId = "Spotify.exe",
                SourceDisplayName = "Spotify",
                PlaybackStatus = Nova.Media.MediaPlaybackStatus.Playing,
                HasActiveSession = true,
                TrackInfo = new Nova.Media.MediaTrackInfo
                {
                    Title = "Underground",
                    Artist = "Men At Work",
                    AlbumTitle = "Business as Usual"
                },
                Capabilities = Nova.Media.MediaCapabilities.All
            });
        }

        if (e.Args.Contains("--demo-notification"))
        {
            _islandWindow.NotificationService.PostNotification(new Nova.Notifications.NovaNotification
            {
                Id = "demo-notif-1",
                SourceAppId = "Slack.exe",
                SourceDisplayName = "Slack",
                Title = "Design Review",
                Message = "Sarah requested feedback on Nova Island mockups",
                Priority = Nova.Events.EventPriority.High,
                Timestamp = DateTimeOffset.UtcNow
            });
            _islandWindow.UpdateNotificationUI();
        }

        if (e.Args.Contains("--slide-notes"))
        {
            _islandWindow.TransitionTo(IslandState.Expanded);
            _islandWindow.NavigateToSlide("notes");
        }
        else if (e.Args.Contains("--slide-todos"))
        {
            _islandWindow.TransitionTo(IslandState.Expanded);
            _islandWindow.NavigateToSlide("todos");
        }
        else if (e.Args.Contains("--slide-tools"))
        {
            _islandWindow.TransitionTo(IslandState.Expanded);
            _islandWindow.NavigateToSlide("tools");
        }
        else if (e.Args.Contains("--slide-mascot"))
        {
            _islandWindow.TransitionTo(IslandState.Expanded);
            _islandWindow.NavigateToSlide("mascot");
        }
        else if (e.Args.Contains("--expanded"))
        {
            _islandWindow.TransitionTo(IslandState.Expanded);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("Unhandled Dispatcher Exception encountered", e.Exception);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            AppLogger.Error("Fatal Unhandled Domain Exception", ex);
        }
        else
        {
            AppLogger.Error($"Fatal Unhandled Domain Exception (non-exception object): {e.ExceptionObject}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info($"NOVA shutting down with exit code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }
}
