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

    public double CompanionSize
    {
        get => (double)GetValue(CompanionSizeProperty);
        set => SetValue(CompanionSizeProperty, value);
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
}
