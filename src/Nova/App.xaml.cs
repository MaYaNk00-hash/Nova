using System.Linq;
using System.Threading;
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
    private const string MutexName = @"Local\NovaDesktopCompanion_SingleInstance_Mutex";
    private const string WakeupEventName = @"Local\NovaDesktopCompanion_Wakeup_Event";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _wakeupEvent;
    private static RegisteredWaitHandle? _waitHandleRegistration;

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
            testWindow.TransitionTo(IslandState.Circle);
            testWindow.TransitionTo(IslandState.Expanded);
            testWindow.TransitionTo(IslandState.Compact);
            testWindow.Close();
            Shutdown(0);
            return;
        }

        // Single-instance enforcement: prevent duplicate processes
        bool isNewInstance;
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out isNewInstance);
        }
        catch
        {
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            AppLogger.Info("Another NOVA instance is already active. Signaling existing instance and terminating duplicate process.");
            try
            {
                if (EventWaitHandle.TryOpenExisting(WakeupEventName, out var existingWakeupEvent))
                {
                    existingWakeupEvent.Set();
                    existingWakeupEvent.Dispose();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Could not signal existing instance wakeup handle: {ex.Message}");
            }

            Shutdown(0);
            return;
        }

        // Register listener on background thread pool for wakeup signals from duplicate launches
        try
        {
            _wakeupEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeupEventName);
            _waitHandleRegistration = ThreadPool.RegisterWaitForSingleObject(
                _wakeupEvent,
                (state, timedOut) =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (_islandWindow != null)
                        {
                            AppLogger.Info("Wakeup signal received from secondary launch attempt. Restoring window.");
                            if (_islandWindow.CurrentState == IslandState.Hidden)
                            {
                                _islandWindow.TransitionTo(IslandState.Compact);
                            }
                            _islandWindow.Show();
                            _islandWindow.WindowState = WindowState.Normal;
                            _islandWindow.Activate();
                            _islandWindow.Topmost = true;
                            _islandWindow.Focus();
                        }
                    });
                },
                null,
                -1,
                false);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not initialize single-instance wakeup listener: {ex.Message}");
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
        else if (e.Args.Contains("--circle"))
        {
            _islandWindow.TransitionTo(IslandState.Circle);
        }
        else if (e.Args.Contains("--compact"))
        {
            _islandWindow.TransitionTo(IslandState.Compact);
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

        try
        {
            _waitHandleRegistration?.Unregister(null);
            _wakeupEvent?.Dispose();
            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch { }
                _singleInstanceMutex.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error cleaning up single-instance handles: {ex.Message}");
        }

        base.OnExit(e);
    }
}
