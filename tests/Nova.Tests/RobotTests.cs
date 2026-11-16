using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    [Fact("Verify RobotSettings ScreenRoaming toggle and ShouldRoam conditions")]
    public static void RobotSettings_ScreenRoaming_TogglesAndChecksShouldRoam()
    {
        var settings = new RobotSettings();
        Assert.True(settings.ScreenRoaming, "ScreenRoaming should default to true.");
        Assert.True(settings.ShouldRoam, "ShouldRoam should be true by default.");

        settings.ScreenRoaming = false;
        Assert.False(settings.ScreenRoaming);
        Assert.False(settings.ShouldRoam);

        settings.ScreenRoaming = true;
        settings.ReducedMotion = true;
        Assert.False(settings.ShouldRoam, "Reduced motion must suppress roaming.");

        settings.ReducedMotion = false;
        settings.BatterySaver = true;
        Assert.False(settings.ShouldRoam, "Battery saver must suppress roaming.");
    }

    [Fact("Verify IslandWindow ScreenRoaming lifecycle: StartRoaming, WaveHi, and FlyToNotch")]
    public static void IslandWindow_ScreenRoaming_StartAndFlyToNotchLifecycle()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Nova.UI.IslandWindow();
                Assert.False(window.IsRoaming);
                Assert.Equal(Nova.UI.IslandState.Compact, window.CurrentState);

                // Start roaming
                window.StartRoaming();
                Assert.True(window.IsRoaming, "IsRoaming should be true after StartRoaming.");
                Assert.Equal(Nova.UI.IslandState.Circle, window.CurrentState);

                // Wave Hi
                window.PerformWaveHi();
                Assert.Equal(System.Windows.Visibility.Visible, window.RoamingGreetingBubble.Visibility);

                // Smile
                window.PerformSmile();

                // Sit
                window.PerformSit();

                // Fly to notch
                window.FlyToNotch(immediate: true);
                Assert.False(window.IsRoaming, "IsRoaming should be false after FlyToNotch.");
                Assert.Equal(Nova.UI.IslandState.Compact, window.CurrentState);

                window.Close();
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

    [Fact("Verify RobotCompanion full body anatomy, ShowBody toggling, and limb animations")]
    public static void RobotCompanion_FullBodyAnatomyAndAnimations()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var robot = new RobotCompanion();

                // 1. Verify full-body anatomy elements are initialized
                Assert.NotNull(robot.LeftLegContainer);
                Assert.NotNull(robot.RightLegContainer);
                Assert.NotNull(robot.TorsoContainer);
                Assert.NotNull(robot.LeftArmContainer);
                Assert.NotNull(robot.RightArmContainer);
                Assert.NotNull(robot.ChestCoreLed);
                Assert.NotNull(robot.MasterCanvas);

                // 2. Default ShowBody is true -> Height is 78
                Assert.True(robot.ShowBody);
                robot.UpdateBodyVisibility();
                Assert.Equal(System.Windows.Visibility.Visible, robot.TorsoContainer.Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, robot.LeftLegContainer.Visibility);
                Assert.Equal(78.0, robot.MasterCanvas.Height);

                // 3. Toggle ShowBody false (for minimalist notch mode) -> Height is 44
                robot.ShowBody = false;
                Assert.Equal(System.Windows.Visibility.Collapsed, robot.TorsoContainer.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, robot.LeftLegContainer.Visibility);
                Assert.Equal(44.0, robot.MasterCanvas.Height);

                // 4. Restore ShowBody true
                robot.ShowBody = true;
                Assert.Equal(System.Windows.Visibility.Visible, robot.TorsoContainer.Visibility);
                Assert.Equal(78.0, robot.MasterCanvas.Height);

                // 5. Verify limb animations execute smoothly
                robot.StartWalking(1.5);
                robot.StopWalking();

                robot.StartWaving();

                robot.SitDown();
                robot.StandUp();

                robot.SetFlightPosture(true);
                robot.SetFlightPosture(false);

                // 6. Export high-fidelity visual snapshots to brain artifact directory
                string artDir = @"C:\Users\mayan\.gemini\antigravity\brain\94fdf842-1673-419b-90de-037bc4a99bea";
                if (Directory.Exists(artDir))
                {
                    RenderAndSaveRobot(true, 0, 0, 10, -10, Path.Combine(artDir, "nova_full_body_standing.png"));
                    RenderAndSaveRobot(true, 0, 0, 10, -130, Path.Combine(artDir, "nova_full_body_waving.png"));
                    RenderAndSaveRobot(true, -55, -2.5, 22, -22, Path.Combine(artDir, "nova_full_body_sitting.png"));
                    RenderAndSaveRobot(false, 0, 0, 0, 0, Path.Combine(artDir, "nova_compact_notch_head.png"));
                }
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

    private static void RenderAndSaveRobot(bool showBody, double legAngle, double legY, double armL, double armR, string outputPath)
    {
        var robot = new RobotCompanion();
        robot.ShowBody = showBody;
        robot.UpdateBodyVisibility();
        robot.LeftLegRotate.Angle = legAngle;
        robot.RightLegRotate.Angle = legAngle;
        robot.LeftLegTranslate.Y = legY;
        robot.RightLegTranslate.Y = legY;
        robot.LeftArmRotate.Angle = armL;
        robot.RightArmRotate.Angle = armR;

        double size = showBody ? 96.0 : 64.0;
        robot.CompanionSize = size;

        var container = new Border
        {
            Width = 140,
            Height = 140,
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 11, 19, 43)),
            CornerRadius = new CornerRadius(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0, 242, 254)),
            BorderThickness = new Thickness(1.2),
            Child = robot
        };

        container.Measure(new Size(140, 140));
        container.Arrange(new Rect(0, 0, 140, 140));
        container.UpdateLayout();

        var rtb = new RenderTargetBitmap(140, 140, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(container);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }
}

