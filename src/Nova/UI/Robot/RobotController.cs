using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Nova.Core;

namespace Nova.UI.Robot;

public sealed class RobotController : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private readonly List<RobotCompanion> _registeredCompanions = new();
    private readonly DispatcherTimer _cursorTrackerTimer;
    private bool _isDisposed = false;

    public RobotSettings Settings { get; } = new();

    public RobotController()
    {
        // Smooth 30fps global cursor tracking
        _cursorTrackerTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _cursorTrackerTimer.Tick += CursorTrackerTimer_Tick;
        _cursorTrackerTimer.Start();
    }

    private void CursorTrackerTimer_Tick(object? sender, EventArgs e)
    {
        if (_isDisposed || !Settings.IsEnabled || !Settings.FollowCursor || Settings.ReducedMotion)
        {
            return;
        }

        if (!GetCursorPos(out POINT pt)) return;

        foreach (var companion in _registeredCompanions)
        {
            if (!companion.IsVisible || companion.ActualWidth <= 0 || companion.ActualHeight <= 0)
                continue;

            try
            {
                Point centerScreen;
                try
                {
                    centerScreen = companion.PointToScreen(new Point(companion.ActualWidth / 2.0, companion.ActualHeight / 2.0));
                }
                catch
                {
                    // Fallback using parent window coordinate mapping and DPI scaling
                    var window = Window.GetWindow(companion);
                    if (window == null) continue;
                    Point rel = companion.TranslatePoint(new Point(companion.ActualWidth / 2.0, companion.ActualHeight / 2.0), window);
                    var source = PresentationSource.FromVisual(window);
                    double m11 = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                    double m22 = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
                    centerScreen = new Point((window.Left + rel.X) * m11, (window.Top + rel.Y) * m22);
                }

                double dx = pt.X - centerScreen.X;
                double dy = pt.Y - centerScreen.Y;
                companion.UpdateGazeVector(dx, dy);
            }
            catch
            {
                // Can occur during rapid visual re-parenting
            }
        }
    }

    public void RegisterCompanion(RobotCompanion companion)
    {
        if (!_registeredCompanions.Contains(companion))
        {
            _registeredCompanions.Add(companion);
            // Sync settings
            companion.Settings.IsEnabled = Settings.IsEnabled;
            companion.Settings.Personality = Settings.Personality;
            companion.Settings.Intensity = Settings.Intensity;
            companion.Settings.ReducedMotion = Settings.ReducedMotion;
            companion.Settings.BatterySaver = Settings.BatterySaver;
            companion.Settings.FollowCursor = Settings.FollowCursor;
            companion.Settings.ScreenRoaming = Settings.ScreenRoaming;
            AppLogger.Debug("RobotCompanion registered with controller.");
        }
    }

    public void UnregisterCompanion(RobotCompanion companion)
    {
        _registeredCompanions.Remove(companion);
    }

    public void OnIslandStateChanged(IslandState newState)
    {
        AppLogger.Debug($"RobotController notified of IslandState change: {newState}.");

        foreach (var companion in _registeredCompanions)
        {
            switch (newState)
            {
                case IslandState.Expanded:
                    companion.WakeUp();
                    break;

                case IslandState.Compact:
                case IslandState.Circle:
                    companion.SetExpression(RobotExpression.Normal);
                    if (companion.CurrentState == RobotState.Sleeping)
                    {
                        companion.WakeUp();
                    }
                    break;

                case IslandState.Hidden:
                    companion.Sleep();
                    break;
            }
        }
    }

    public void OnUserInteraction()
    {
        foreach (var companion in _registeredCompanions)
        {
            companion.Acknowledge();
        }
    }

    public void OnCursorMoved(Point relativeCursorPos, Size containerSize)
    {
        foreach (var companion in _registeredCompanions)
        {
            companion.UpdateCursorGaze(relativeCursorPos, containerSize);
        }
    }

    public void TriggerExcitedTestEvent()
    {
        AppLogger.Info("Triggering Robot Excited test event.");
        foreach (var companion in _registeredCompanions)
        {
            companion.TriggerExcited();
        }
    }

    public void TriggerConfusedTestEvent()
    {
        AppLogger.Info("Triggering Robot Confused test event.");
        foreach (var companion in _registeredCompanions)
        {
            companion.TriggerConfused();
        }
    }

    public void TriggerNodTestEvent()
    {
        AppLogger.Info("Triggering Robot Nod test event.");
        foreach (var companion in _registeredCompanions)
        {
            companion.Acknowledge();
        }
    }

    public void SetPersonality(RobotPersonality personality)
    {
        Settings.Personality = personality;
        foreach (var companion in _registeredCompanions)
        {
            companion.Settings.Personality = personality;
        }
        AppLogger.Info($"Robot Personality updated to: {personality}.");
    }

    public void SetIntensity(AnimationIntensity intensity)
    {
        Settings.Intensity = intensity;
        foreach (var companion in _registeredCompanions)
        {
            companion.Settings.Intensity = intensity;
        }
        AppLogger.Info($"Robot Animation Intensity updated to: {intensity}.");
    }

    public void SetBatterySaver(bool enabled)
    {
        Settings.BatterySaver = enabled;
        foreach (var companion in _registeredCompanions)
        {
            companion.Settings.BatterySaver = enabled;
        }
        AppLogger.Info($"Robot Battery Saver updated to: {enabled}.");
    }

    public void SetReducedMotion(bool enabled)
    {
        Settings.ReducedMotion = enabled;
        foreach (var companion in _registeredCompanions)
        {
            companion.Settings.ReducedMotion = enabled;
        }
        AppLogger.Info($"Robot Reduced Motion updated to: {enabled}.");
    }

    public void SetFollowCursor(bool enabled)
    {
        Settings.FollowCursor = enabled;
        foreach (var companion in _registeredCompanions)
        {
            companion.Settings.FollowCursor = enabled;
        }
        AppLogger.Info($"Robot Eye Tracking (Follow Cursor) updated to: {enabled}.");
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cursorTrackerTimer.Stop();
        AppLogger.Info("RobotController disposed cleanly.");
    }
}

