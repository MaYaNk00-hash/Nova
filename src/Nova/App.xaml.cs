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

        if (e.Args.Contains("--expanded"))
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
