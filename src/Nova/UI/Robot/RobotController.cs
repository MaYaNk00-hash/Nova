using System.Windows;
using Nova.Core;

namespace Nova.UI.Robot;

public sealed class RobotController
{
    private readonly List<RobotCompanion> _registeredCompanions = new();

    public RobotSettings Settings { get; } = new();

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
}
