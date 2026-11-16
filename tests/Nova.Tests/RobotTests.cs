using Nova.UI;
using Nova.UI.Robot;

namespace Nova.Tests;

public class RobotTests
{
    [Fact("Verify RobotSettings default configuration")]
    public void RobotSettings_DefaultConfiguration()
    {
        var settings = new RobotSettings();
        Assert.True(settings.IsEnabled, "Robot should be enabled by default.");
        Assert.Equal(RobotPersonality.Calm, settings.Personality);
        Assert.Equal(AnimationIntensity.Normal, settings.Intensity);
        Assert.False(settings.ReducedMotion, "Reduced motion should be off by default.");
        Assert.False(settings.BatterySaver, "Battery saver should be off by default.");
        Assert.True(settings.FollowCursor, "FollowCursor should be on by default.");
        Assert.True(settings.ShouldFloat, "Robot should float by default.");
    }

    [Fact("Verify RobotSettings FollowCursor toggles correctly and syncs with controller")]
    public void RobotSettings_FollowCursor_TogglesCorrectly()
    {
        var controller = new RobotController();
        Assert.True(controller.Settings.FollowCursor);

        controller.SetFollowCursor(false);
        Assert.False(controller.Settings.FollowCursor);

        controller.SetFollowCursor(true);
        Assert.True(controller.Settings.FollowCursor);

        controller.Dispose();
    }

    [Fact("Verify RobotSettings personality blink intervals")]
    public void RobotSettings_PersonalityIntervals()
    {
        var settings = new RobotSettings();

        // Playful: fast/lively
        settings.Personality = RobotPersonality.Playful;
        var playful = settings.GetBlinkIntervalRange();
        Assert.True(playful.MinMs < 3000, "Playful blink min should be lively (<3000ms).");

        // Calm: balanced
        settings.Personality = RobotPersonality.Calm;
        var calm = settings.GetBlinkIntervalRange();
        Assert.True(calm.MinMs > playful.MinMs, "Calm min interval should be longer than Playful.");

        // Minimal: rare blinks
        settings.Personality = RobotPersonality.Minimal;
        var minimal = settings.GetBlinkIntervalRange();
        Assert.True(minimal.MinMs >= 7000, "Minimal personality should have rare blinks.");

        // Battery saver override
        settings.BatterySaver = true;
        var battery = settings.GetBlinkIntervalRange();
        Assert.True(battery.MinMs >= 6000, "Battery saver mode should stretch blink interval.");
    }

    [Fact("Verify RobotSettings ShouldFloat respects power and motion modes")]
    public void RobotSettings_ShouldFloat_RespectsRestrictions()
    {
        var settings = new RobotSettings();
        Assert.True(settings.ShouldFloat, "Default should float.");

        settings.ReducedMotion = true;
        Assert.False(settings.ShouldFloat, "Reduced motion must disable idle floating.");

        settings.ReducedMotion = false;
        settings.BatterySaver = true;
        Assert.False(settings.ShouldFloat, "Battery saver must disable idle floating.");

        settings.BatterySaver = false;
        settings.Personality = RobotPersonality.Minimal;
        Assert.False(settings.ShouldFloat, "Minimal personality must disable idle floating.");

        settings.Personality = RobotPersonality.Calm;
        settings.IsEnabled = false;
        Assert.False(settings.ShouldFloat, "Disabled robot must not float.");
    }

    [Fact("Verify RobotCompanion instantiation and defaults on STA thread")]
    public void RobotCompanion_InstantiatesWithDefaultState()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var robot = new RobotCompanion();
                Assert.NotNull(robot);
                Assert.Equal(RobotState.Idle, robot.CurrentState);
                Assert.Equal(RobotExpression.Normal, robot.CurrentExpression);
                Assert.Equal(32.0, robot.CompanionSize);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (threadException != null)
        {
            throw new InvalidOperationException($"STA thread failed: {threadException.Message}", threadException);
        }
    }

    [Fact("Verify RobotCompanion expressions and state transitions")]
    public void RobotCompanion_StateTransitions_ExecuteCorrectly()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var robot = new RobotCompanion();

                // Test Sleep
                robot.Sleep();
                Assert.Equal(RobotState.Sleeping, robot.CurrentState);
                Assert.Equal(RobotExpression.Sleeping, robot.CurrentExpression);

                // Test WakeUp
                robot.WakeUp();
                Assert.Equal(RobotState.Waking, robot.CurrentState);
                Assert.Equal(RobotExpression.Normal, robot.CurrentExpression);

                // Test Acknowledgement Nod
                robot.Acknowledge();
                Assert.Equal(RobotState.Acknowledging, robot.CurrentState);

                // Test Excited Reaction
                robot.TriggerExcited();
                Assert.Equal(RobotState.Excited, robot.CurrentState);
                Assert.Equal(RobotExpression.Happy, robot.CurrentExpression);

                // Test Confused Expression
                robot.TriggerConfused();
                Assert.Equal(RobotState.Confused, robot.CurrentState);
                Assert.Equal(RobotExpression.Confused, robot.CurrentExpression);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (threadException != null)
        {
            throw new InvalidOperationException($"STA thread failed: {threadException.Message}", threadException);
        }
    }

    [Fact("Verify RobotController coordinates companion lifecycle")]
    public void RobotController_CoordinatesCompanions()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var controller = new RobotController();
                var robot1 = new RobotCompanion();
                var robot2 = new RobotCompanion();

                controller.RegisterCompanion(robot1);
                controller.RegisterCompanion(robot2);

                // Test personality broadcast
                controller.SetPersonality(RobotPersonality.Playful);
                Assert.Equal(RobotPersonality.Playful, robot1.Settings.Personality);
                Assert.Equal(RobotPersonality.Playful, robot2.Settings.Personality);

                // Test Island Hidden -> Sleep
                controller.OnIslandStateChanged(IslandState.Hidden);
                Assert.Equal(RobotState.Sleeping, robot1.CurrentState);
                Assert.Equal(RobotState.Sleeping, robot2.CurrentState);

                // Test Island Circle -> WakeUp
                controller.OnIslandStateChanged(IslandState.Circle);
                Assert.Equal(RobotState.Waking, robot1.CurrentState);
                Assert.Equal(RobotState.Waking, robot2.CurrentState);

                // Test Island Expanded -> WakeUp
                controller.OnIslandStateChanged(IslandState.Expanded);
                Assert.Equal(RobotState.Waking, robot1.CurrentState);
                Assert.Equal(RobotState.Waking, robot2.CurrentState);

                // Test Test Events
                controller.TriggerExcitedTestEvent();
                Assert.Equal(RobotState.Excited, robot1.CurrentState);
                Assert.Equal(RobotState.Excited, robot2.CurrentState);

                controller.TriggerConfusedTestEvent();
                Assert.Equal(RobotState.Confused, robot1.CurrentState);
                Assert.Equal(RobotState.Confused, robot2.CurrentState);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (threadException != null)
        {
            throw new InvalidOperationException($"STA thread failed: {threadException.Message}", threadException);
        }
    }

    [Fact("Verify RobotCompanion eye gaze tracks cursor across full desktop quadrants")]
    public void RobotCompanion_GlobalScreenEyeTracking_TracksCursorAcrossFullDesktop()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var robot = new RobotCompanion();

                // 1. Bottom-Right screen vector (e.g. mouse at bottom right of 1080p display)
                robot.UpdateGazeVector(800.0, 600.0);
                Assert.True(robot.GazeTransform.X > 0, "Gaze X should deflect right for positive deltaX.");
                Assert.True(robot.GazeTransform.Y > 0, "Gaze Y should deflect down for positive deltaY.");
                Assert.True(robot.HeadRotateTransform.Angle > 0, "Head should subtly tilt right.");

                // 2. Bottom-Left screen vector (e.g. mouse at bottom left of display)
                robot.UpdateGazeVector(-800.0, 600.0);
                Assert.True(robot.GazeTransform.X < 0, "Gaze X should deflect left for negative deltaX.");
                Assert.True(robot.GazeTransform.Y > 0, "Gaze Y should deflect down for positive deltaY.");
                Assert.True(robot.HeadRotateTransform.Angle < 0, "Head should subtly tilt left.");

                // 3. Center reset
                robot.UpdateGazeVector(0.0, 0.0);
                Assert.Equal(0.0, robot.GazeTransform.X);
                Assert.Equal(0.0, robot.GazeTransform.Y);

                // 4. FollowCursor toggled off
                robot.Settings.FollowCursor = false;
                robot.UpdateGazeVector(500.0, 500.0);
                Assert.Equal(0.0, robot.GazeTransform.X);
                Assert.Equal(0.0, robot.GazeTransform.Y);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (threadException != null)
        {
            throw new InvalidOperationException($"STA thread failed: {threadException.Message}", threadException);
        }
    }
}

