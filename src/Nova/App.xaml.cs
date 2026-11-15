using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Nova.Core;

namespace Nova;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
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
            AppLogger.Info("Smoke test flag detected: validating window lifecycle and exiting cleanly.");
            var testWindow = new MainWindow();
            testWindow.Show();
            testWindow.Close();
            Shutdown(0);
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
