using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nova.Core;

namespace Nova.UI.Robot;

public partial class RobotCompanion : UserControl
{
    public static readonly DependencyProperty CompanionSizeProperty =
        DependencyProperty.Register(nameof(CompanionSize), typeof(double), typeof(RobotCompanion), new PropertyMetadata(32.0));

    public static readonly DependencyProperty ShowBodyProperty =
        DependencyProperty.Register(nameof(ShowBody), typeof(bool), typeof(RobotCompanion), new PropertyMetadata(true, OnShowBodyChanged));

    public double CompanionSize
    {
        get => (double)GetValue(CompanionSizeProperty);
        set => SetValue(CompanionSizeProperty, value);
    }

    public bool ShowBody
    {
        get => (bool)GetValue(ShowBodyProperty);
        set => SetValue(ShowBodyProperty, value);
    }

    private static void OnShowBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RobotCompanion robot)
        {
            robot.UpdateBodyVisibility();
        }
    }

    public void UpdateBodyVisibility()
    {
        var vis = ShowBody ? Visibility.Visible : Visibility.Collapsed;
        if (TorsoContainer != null) TorsoContainer.Visibility = vis;
        if (LeftArmContainer != null) LeftArmContainer.Visibility = vis;
        if (RightArmContainer != null) RightArmContainer.Visibility = vis;
        if (LeftLegContainer != null) LeftLegContainer.Visibility = vis;
        if (RightLegContainer != null) RightLegContainer.Visibility = vis;

        if (MasterCanvas != null)
        {
            MasterCanvas.Height = ShowBody ? 78 : 44;
        }
    }

    private readonly DispatcherTimer _blinkTimer;
    private readonly Random _random = new();
    private bool _isFloating = false;

    public RobotSettings Settings { get; } = new();
    public RobotState CurrentState { get; private set; } = RobotState.Idle;
    public RobotExpression CurrentExpression { get; private set; } = RobotExpression.Normal;

    public RobotCompanion()
    {
        InitializeComponent();

        _blinkTimer = new DispatcherTimer();
        _blinkTimer.Tick += BlinkTimer_Tick;

        Settings.SettingsChanged += Settings_SettingsChanged;

        Loaded += RobotCompanion_Loaded;
        Unloaded += RobotCompanion_Unloaded;
    }

    private void RobotCompanion_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateBodyVisibility();
        ApplySettings();
        if (CurrentState != RobotState.Sleeping)
        {
            StartBlinkCycle();
            if (Settings.ShouldFloat)
            {
                StartFloating();
            }
        }
    }

    private void RobotCompanion_Unloaded(object sender, RoutedEventArgs e)
    {
        StopBlinkCycle();
        StopFloating();
    }

    private void Settings_SettingsChanged(object? sender, EventArgs e)
    {
        ApplySettings();
    }

    private void ApplySettings()
    {
        if (!Settings.IsEnabled)
        {
            Visibility = Visibility.Collapsed;
            StopFloating();
            StopBlinkCycle();
            return;
        }

        Visibility = Visibility.Visible;

        if (Settings.ShouldFloat && CurrentState != RobotState.Sleeping)
        {
            StartFloating();
        }
        else
        {
            StopFloating();
        }

        if (Settings.ReducedMotion || !Settings.FollowCursor)
        {
            GazeTransform.X = 0;
            GazeTransform.Y = 0;
            HeadRotateTransform.Angle = 0;
        }
    }

    // --- BLINK LOGIC ---
    private void StartBlinkCycle()
    {
        _blinkTimer.Stop();
        if (!Settings.IsEnabled || CurrentState == RobotState.Sleeping) return;

        var (minMs, maxMs) = Settings.GetBlinkIntervalRange();
        int nextDelay = _random.Next(minMs, maxMs);
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(nextDelay);
        _blinkTimer.Start();
    }

    private void StopBlinkCycle()
    {
        _blinkTimer.Stop();
    }

    private void BlinkTimer_Tick(object? sender, EventArgs e)
    {
        if (CurrentExpression == RobotExpression.Normal && CurrentState != RobotState.Sleeping)
        {
            ExecuteBlink();
        }
        StartBlinkCycle();
    }

    public void ExecuteBlink()
    {
        if (CurrentExpression != RobotExpression.Normal && CurrentExpression != RobotExpression.Blink) return;

        var anim = new DoubleAnimation(1.0, 0.1, TimeSpan.FromMilliseconds(70))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        LeftEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        RightEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    // --- IDLE FLOAT LOGIC ---
    public void StartFloating()
    {
        if (_isFloating || !Settings.ShouldFloat) return;

        _isFloating = true;
        double displacement = 2.2 * Settings.GetIntensityMultiplier();
        var anim = new DoubleAnimation(-displacement, displacement, TimeSpan.FromSeconds(2.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        HeadFloatTransform.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    public void StopFloating()
    {
        _isFloating = false;
        HeadFloatTransform.BeginAnimation(TranslateTransform.YProperty, null);
        HeadFloatTransform.Y = 0;
    }

    // --- CURSOR AWARENESS ---
    public void UpdateCursorGaze(Point relativeCursorPos, Size containerSize)
    {
        if (Settings.ReducedMotion || CurrentState == RobotState.Sleeping || !Settings.FollowCursor)
        {
            UpdateGazeVector(0, 0);
            return;
        }

        double centerX = containerSize.Width / 2.0;
        double centerY = containerSize.Height / 2.0;
        double deltaX = relativeCursorPos.X - centerX;
        double deltaY = relativeCursorPos.Y - centerY;
        UpdateGazeVector(deltaX, deltaY);
    }

    /// <summary>
    /// Updates eye gaze based on a global screen vector from companion center to mouse cursor across the entire screen.
    /// </summary>
    public void UpdateGazeVector(double deltaX, double deltaY)
    {
        if (Settings.ReducedMotion || CurrentState == RobotState.Sleeping || !Settings.FollowCursor)
        {
            if (GazeTransform.X != 0 || GazeTransform.Y != 0 || HeadRotateTransform.Angle != 0)
            {
                if (GazeTransform.HasAnimatedProperties)
                {
                    GazeTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    GazeTransform.BeginAnimation(TranslateTransform.YProperty, null);
                }
                GazeTransform.X = 0;
                GazeTransform.Y = 0;
                HeadRotateTransform.Angle = 0;
            }
            return;
        }

        double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (distance < 0.001)
        {
            if (GazeTransform.HasAnimatedProperties)
            {
                GazeTransform.BeginAnimation(TranslateTransform.XProperty, null);
                GazeTransform.BeginAnimation(TranslateTransform.YProperty, null);
            }
            GazeTransform.X = 0;
            GazeTransform.Y = 0;
            HeadRotateTransform.Angle = 0;
            return;
        }

        // Maximum deflection inside visor plate (DIPs)
        double maxOffsetX = 5.2 * Settings.GetIntensityMultiplier();
        double maxOffsetY = 4.2 * Settings.GetIntensityMultiplier();

        // Screen-wide normalization with hyperbolic tangent for smooth non-linear saturation across full desktop
        double normX = Math.Tanh(deltaX / 320.0);
        double normY = Math.Tanh(deltaY / 260.0);

        double targetX = normX * maxOffsetX;
        double targetY = normY * maxOffsetY;

        // Clear any animation clocks that would lock dependency property value precedence
        if (GazeTransform.HasAnimatedProperties)
        {
            GazeTransform.BeginAnimation(TranslateTransform.XProperty, null);
            GazeTransform.BeginAnimation(TranslateTransform.YProperty, null);
        }

        // Responsive smooth damping interpolation (no locking animation clocks)
        GazeTransform.X += (targetX - GazeTransform.X) * 0.45;
        GazeTransform.Y += (targetY - GazeTransform.Y) * 0.45;

        // Subtle head tilt towards cursor across the screen
        if (Settings.Personality != RobotPersonality.Minimal)
        {
            double targetTilt = Math.Clamp(normX * 4.2, -4.5, 4.5);
            HeadRotateTransform.Angle += (targetTilt - HeadRotateTransform.Angle) * 0.3;
        }
    }

    // --- EXPRESSIONS ---
    public void SetExpression(RobotExpression expression)
    {
        CurrentExpression = expression;

        // Reset elements
        LeftEyeNormal.Visibility = Visibility.Collapsed;
        RightEyeNormal.Visibility = Visibility.Collapsed;
        LeftEyeHappy.Visibility = Visibility.Collapsed;
        RightEyeHappy.Visibility = Visibility.Collapsed;
        LeftEyeSleeping.Visibility = Visibility.Collapsed;
        RightEyeSleeping.Visibility = Visibility.Collapsed;

        LeftEyeRotate.Angle = 0;
        RightEyeRotate.Angle = 0;
        LeftEyeScale.ScaleX = 1.0;
        LeftEyeScale.ScaleY = 1.0;
        RightEyeScale.ScaleX = 1.0;
        RightEyeScale.ScaleY = 1.0;
        HeadRotateTransform.Angle = 0;

        switch (expression)
        {
            case RobotExpression.Normal:
                LeftEyeNormal.Visibility = Visibility.Visible;
                RightEyeNormal.Visibility = Visibility.Visible;
                AntennaLed.Fill = System.Windows.Media.Brushes.Cyan;
                break;

            case RobotExpression.Happy:
                LeftEyeHappy.Visibility = Visibility.Visible;
                RightEyeHappy.Visibility = Visibility.Visible;
                AntennaLed.Fill = System.Windows.Media.Brushes.DeepSkyBlue;
                break;

            case RobotExpression.Confused:
                LeftEyeNormal.Visibility = Visibility.Visible;
                RightEyeNormal.Visibility = Visibility.Visible;
                LeftEyeScale.ScaleX = 1.25;
                LeftEyeScale.ScaleY = 1.25;
                LeftEyeRotate.Angle = -12;
                RightEyeScale.ScaleX = 0.85;
                RightEyeScale.ScaleY = 0.85;
                RightEyeRotate.Angle = 8;
                HeadRotateTransform.Angle = 6;
                AntennaLed.Fill = System.Windows.Media.Brushes.LightSkyBlue;
                break;

            case RobotExpression.Sleeping:
                LeftEyeSleeping.Visibility = Visibility.Visible;
                RightEyeSleeping.Visibility = Visibility.Visible;
                AntennaLed.Fill = System.Windows.Media.Brushes.CadetBlue;
                break;
        }
    }

    // --- STATE ACTIONS ---
    public void WakeUp()
    {
        CurrentState = RobotState.Waking;
        SetExpression(RobotExpression.Normal);

        var duration = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // Eye open animation
        var scaleAnim = new DoubleAnimation(0.2, 1.0, duration) { EasingFunction = ease };
        LeftEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        RightEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

        // Cheerful slight dip and rise
        var nodAnim = new DoubleAnimation(0, -3.0, duration)
        {
            AutoReverse = true,
            EasingFunction = ease
        };
        nodAnim.Completed += (s, e) =>
        {
            CurrentState = RobotState.Idle;
            if (Settings.ShouldFloat) StartFloating();
            StartBlinkCycle();
        };
        HeadFloatTransform.BeginAnimation(TranslateTransform.YProperty, nodAnim);
    }

    public void Sleep()
    {
        CurrentState = RobotState.Sleeping;
        StopFloating();
        StopBlinkCycle();
        SetExpression(RobotExpression.Sleeping);
        GazeTransform.X = 0;
        GazeTransform.Y = 0;
    }

    public void Acknowledge()
    {
        if (CurrentState == RobotState.Sleeping) return;

        CurrentState = RobotState.Acknowledging;
        double nodAmount = 3.5 * Settings.GetIntensityMultiplier();
        var nod = new DoubleAnimation(0, nodAmount, TimeSpan.FromMilliseconds(130))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        nod.Completed += (s, e) =>
        {
            CurrentState = RobotState.Idle;
            if (Settings.ShouldFloat) StartFloating();
        };
        HeadFloatTransform.BeginAnimation(TranslateTransform.YProperty, nod);
    }

    public void TriggerExcited()
    {
        if (CurrentState == RobotState.Sleeping) return;

        CurrentState = RobotState.Excited;
        SetExpression(RobotExpression.Happy);

        // Double nod bounce
        var bounce = new DoubleAnimation(0, -4.0 * Settings.GetIntensityMultiplier(), TimeSpan.FromMilliseconds(160))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        bounce.Completed += (s, e) =>
        {
            // Reset to normal after short pause
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
            timer.Tick += (ts, te) =>
            {
                timer.Stop();
                if (CurrentState == RobotState.Excited)
                {
                    SetExpression(RobotExpression.Normal);
                    CurrentState = RobotState.Idle;
                    if (Settings.ShouldFloat) StartFloating();
                }
            };
            timer.Start();
        };

        HeadFloatTransform.BeginAnimation(TranslateTransform.YProperty, bounce);
    }

    public void TriggerConfused()
    {
        if (CurrentState == RobotState.Sleeping) return;

        CurrentState = RobotState.Confused;
        SetExpression(RobotExpression.Confused);

        // Revert to normal after inquiry pause
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        timer.Tick += (ts, te) =>
        {
            timer.Stop();
            if (CurrentState == RobotState.Confused)
            {
                SetExpression(RobotExpression.Normal);
                CurrentState = RobotState.Idle;
                if (Settings.ShouldFloat) StartFloating();
            }
        };
        timer.Start();
    }

    // --- FULL BODY MOVEMENTS & ANIMATIONS ---

    /// <summary>
    /// Starts walking stride: alternating legs stepping and arms swinging with head bobbing.
    /// </summary>
    public void StartWalking(double durationSec = 2.0)
    {
        if (Settings.ReducedMotion || CurrentState == RobotState.Sleeping) return;

        // Alternating leg strides
        var legStep1 = new DoubleAnimation(-22.0, 22.0, TimeSpan.FromMilliseconds(220))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        var legStep2 = new DoubleAnimation(22.0, -22.0, TimeSpan.FromMilliseconds(220))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        LeftLegRotate.BeginAnimation(RotateTransform.AngleProperty, legStep1);
        RightLegRotate.BeginAnimation(RotateTransform.AngleProperty, legStep2);

        // Natural arm counter-swing
        var armSwing1 = new DoubleAnimation(20.0, -15.0, TimeSpan.FromMilliseconds(220))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        var armSwing2 = new DoubleAnimation(-20.0, 15.0, TimeSpan.FromMilliseconds(220))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        LeftArmRotate.BeginAnimation(RotateTransform.AngleProperty, armSwing1);
        RightArmRotate.BeginAnimation(RotateTransform.AngleProperty, armSwing2);

        // Walking head/body bob
        var walkBob = new DoubleAnimation(0.0, -2.5, TimeSpan.FromMilliseconds(110))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        HeadFloatTransform.BeginAnimation(TranslateTransform.YProperty, walkBob);
    }

    /// <summary>
    /// Stops walking stride and eases body back into neutral standing/floating posture.
    /// </summary>
    public void StopWalking()
    {
        LeftLegRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        RightLegRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        LeftArmRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        RightArmRotate.BeginAnimation(RotateTransform.AngleProperty, null);

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeLeg = new DoubleAnimation(LeftLegRotate.Angle, 0.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = easeOut };
        LeftLegRotate.BeginAnimation(RotateTransform.AngleProperty, easeLeg);
        RightLegRotate.BeginAnimation(RotateTransform.AngleProperty, easeLeg);

        var easeArmL = new DoubleAnimation(LeftArmRotate.Angle, 10.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = easeOut };
        var easeArmR = new DoubleAnimation(RightArmRotate.Angle, -10.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = easeOut };
        LeftArmRotate.BeginAnimation(RotateTransform.AngleProperty, easeArmL);
        RightArmRotate.BeginAnimation(RotateTransform.AngleProperty, easeArmR);

        HeadFloatTransform.BeginAnimation(TranslateTransform.YProperty, null);
        if (Settings.ShouldFloat && CurrentState != RobotState.Sleeping)
        {
            StartFloating();
        }
    }

    /// <summary>
    /// Waves right hand cheerfully with animated arm raise and rapid waving motion.
    /// </summary>
    public void StartWaving()
    {
        if (Settings.ReducedMotion || CurrentState == RobotState.Sleeping) return;

        var raiseArm = new DoubleAnimation(RightArmRotate.Angle, -130.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
        };

        raiseArm.Completed += (s, e) =>
        {
            var wave = new DoubleAnimation(-115.0, -145.0, TimeSpan.FromMilliseconds(120))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            wave.Completed += (ws, we) =>
            {
                var lowerArm = new DoubleAnimation(RightArmRotate.Angle, -10.0, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                RightArmRotate.BeginAnimation(RotateTransform.AngleProperty, lowerArm);
            };

            RightArmRotate.BeginAnimation(RotateTransform.AngleProperty, wave);
        };

        RightArmRotate.BeginAnimation(RotateTransform.AngleProperty, raiseArm);
    }

    /// <summary>
    /// Folds legs forward into an adorable sitting posture.
    /// </summary>
    public void SitDown()
    {
        if (Settings.ReducedMotion || CurrentState == RobotState.Sleeping) return;

        var sitAngle = new DoubleAnimation(0.0, -55.0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        LeftLegRotate.BeginAnimation(RotateTransform.AngleProperty, sitAngle);
        RightLegRotate.BeginAnimation(RotateTransform.AngleProperty, sitAngle);

        var sitLift = new DoubleAnimation(0.0, -2.5, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        LeftLegTranslate.BeginAnimation(TranslateTransform.YProperty, sitLift);
        RightLegTranslate.BeginAnimation(TranslateTransform.YProperty, sitLift);

        var sitArms = new DoubleAnimation(LeftArmRotate.Angle, 22.0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        LeftArmRotate.BeginAnimation(RotateTransform.AngleProperty, sitArms);
        var sitArmR = new DoubleAnimation(RightArmRotate.Angle, -22.0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        RightArmRotate.BeginAnimation(RotateTransform.AngleProperty, sitArmR);
    }

    /// <summary>
    /// Restores full standing posture from sitting.
    /// </summary>
    public void StandUp()
    {
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var standLeg = new DoubleAnimation(LeftLegRotate.Angle, 0.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = easeOut };
        LeftLegRotate.BeginAnimation(RotateTransform.AngleProperty, standLeg);
        RightLegRotate.BeginAnimation(RotateTransform.AngleProperty, standLeg);

        LeftLegTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        RightLegTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        LeftLegTranslate.Y = 0;
        RightLegTranslate.Y = 0;

        var standArmL = new DoubleAnimation(LeftArmRotate.Angle, 10.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = easeOut };
        var standArmR = new DoubleAnimation(RightArmRotate.Angle, -10.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = easeOut };
        LeftArmRotate.BeginAnimation(RotateTransform.AngleProperty, standArmL);
        RightArmRotate.BeginAnimation(RotateTransform.AngleProperty, standArmR);
    }

    /// <summary>
    /// Configures flight posture: legs streamlined back and arms tucked back aerodynamically.
    /// </summary>
    public void SetFlightPosture(bool isFlying)
    {
        if (isFlying)
        {
            var flightLeg = new DoubleAnimation(LeftLegRotate.Angle, 30.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            LeftLegRotate.BeginAnimation(RotateTransform.AngleProperty, flightLeg);
            RightLegRotate.BeginAnimation(RotateTransform.AngleProperty, flightLeg);

            var flightArmL = new DoubleAnimation(LeftArmRotate.Angle, 40.0, TimeSpan.FromMilliseconds(160));
            var flightArmR = new DoubleAnimation(RightArmRotate.Angle, -40.0, TimeSpan.FromMilliseconds(160));
            LeftArmRotate.BeginAnimation(RotateTransform.AngleProperty, flightArmL);
            RightArmRotate.BeginAnimation(RotateTransform.AngleProperty, flightArmR);
        }
        else
        {
            StandUp();
        }
    }
}
