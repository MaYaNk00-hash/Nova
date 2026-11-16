namespace Nova.UI.Robot;

public sealed class RobotSettings
{
    public event EventHandler? SettingsChanged;

    private bool _isEnabled = true;
    private RobotPersonality _personality = RobotPersonality.Calm;
    private AnimationIntensity _intensity = AnimationIntensity.Normal;
    private bool _reducedMotion = false;
    private bool _batterySaver = false;
    private bool _followCursor = true;
    private bool _screenRoaming = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnSettingsChanged();
            }
        }
    }

    public RobotPersonality Personality
    {
        get => _personality;
        set
        {
            if (_personality != value)
            {
                _personality = value;
                OnSettingsChanged();
            }
        }
    }

    public AnimationIntensity Intensity
    {
        get => _intensity;
        set
        {
            if (_intensity != value)
            {
                _intensity = value;
                OnSettingsChanged();
            }
        }
    }

    public bool ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            if (_reducedMotion != value)
            {
                _reducedMotion = value;
                OnSettingsChanged();
            }
        }
    }

    public bool BatterySaver
    {
        get => _batterySaver;
        set
        {
            if (_batterySaver != value)
            {
                _batterySaver = value;
                OnSettingsChanged();
            }
        }
    }

    public bool FollowCursor
    {
        get => _followCursor;
        set
        {
            if (_followCursor != value)
            {
                _followCursor = value;
                OnSettingsChanged();
            }
        }
    }

    public bool ScreenRoaming
    {
        get => _screenRoaming;
        set
        {
            if (_screenRoaming != value)
            {
                _screenRoaming = value;
                OnSettingsChanged();
            }
        }
    }

    /// <summary>
    /// Whether screen roaming (wandering and walking across screen) is allowed.
    /// </summary>
    public bool ShouldRoam => IsEnabled && ScreenRoaming && !ReducedMotion && !BatterySaver;

    /// <summary>
    /// Whether idle floating animations are allowed based on motion and power settings.
    /// </summary>
    public bool ShouldFloat => IsEnabled && !ReducedMotion && !BatterySaver && Personality != RobotPersonality.Minimal;

    /// <summary>
    /// Gets the base blink interval range in milliseconds based on personality.
    /// </summary>
    public (int MinMs, int MaxMs) GetBlinkIntervalRange()
    {
        if (BatterySaver)
        {
            return (6000, 10000); // Rare blinks in battery saver mode
        }

        return Personality switch
        {
            RobotPersonality.Playful => (2000, 3800),
            RobotPersonality.Calm => (3500, 6000),
            RobotPersonality.Minimal => (7000, 12000),
            _ => (3500, 6000)
        };
    }

    /// <summary>
    /// Multiplier for animation speed/displacement based on intensity.
    /// </summary>
    public double GetIntensityMultiplier()
    {
        return Intensity switch
        {
            AnimationIntensity.Low => 0.6,
            AnimationIntensity.Normal => 1.0,
            AnimationIntensity.High => 1.4,
            _ => 1.0
        };
    }

    private void OnSettingsChanged()
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}

