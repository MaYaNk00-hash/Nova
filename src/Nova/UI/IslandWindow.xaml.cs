using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Nova.Core;
using Nova.Events;
using Nova.Media;
using Nova.Notifications;
using Nova.Storage;
using Nova.SystemMonitor;
using Nova.UI.Robot;

namespace Nova.UI;

public partial class IslandWindow : Window
{
    private enum SlideView
    {
        Hud,
        Notes,
        Todos,
        Tools,
        Mascot
    }

    private readonly TrayManager _trayManager;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _notesSaveTimer;
    private readonly DispatcherTimer _focusTimer;
    private readonly RobotController _robotController;
    private readonly EventEngine _eventEngine;
    private readonly IMediaService _mediaService;
    private readonly INotificationService _notificationService;
    private readonly SystemMonitorService _systemMonitor = new();
    private readonly List<IDisposable> _subscriptions = new();

    private MediaSessionSnapshot? _lastMediaSnapshot;
    private bool _isManuallyPositioned = false;
    private bool _isTransitioning = false;
    private bool _isPinned = false;
    private SlideView _currentSlide = SlideView.Hud;

    private readonly DispatcherTimer _equalizerTimer;
    private static readonly string[] WaveFrames = new[] { " ▂▃", "▃▅▃", "▂▅▇", "▇▅▃", "▅▃▂", "▃▂ " };
    private int _waveIndex = 0;
    private TimeSpan _playbackElapsed = TimeSpan.FromSeconds(41);
    private TimeSpan _playbackTotal = TimeSpan.FromSeconds(184);
    private int _currentVolume = 100;
    private bool _isMuted = false;
    private bool _isScrubbingTimeline = false;
    private DateTime _lastSeekUtc = DateTime.MinValue;

    private List<TodoItem> _todos = new();
    private string _todoFilter = "All";
    private TimeSpan _focusRemaining = TimeSpan.FromMinutes(25);
    private TimeSpan _focusPreset = TimeSpan.FromMinutes(25);
    private bool _isFocusTimerRunning = false;

    private TextBlock[] _calendarDayLabels = Array.Empty<TextBlock>();
    private TextBlock[] _calendarNumLabels = Array.Empty<TextBlock>();
    private Border[] _calendarNumBorders = Array.Empty<Border>();

    public IslandState CurrentState { get; private set; } = IslandState.Compact;
    public PillTransparencyMode TransparencyMode { get; private set; } = PillTransparencyMode.TranslucentGlass;
    public bool IsAlwaysOnTop => Topmost;
    public bool IsManuallyPositioned => _isManuallyPositioned;
    public RobotController RobotController => _robotController;
    public EventEngine EventEngine => _eventEngine;
    public IMediaService MediaService => _mediaService;
    public INotificationService NotificationService => _notificationService;

    public IslandWindow(
        IMediaService? mediaService = null, 
        INotificationService? notificationService = null, 
        EventEngine? eventEngine = null)
    {
        InitializeComponent();

        _eventEngine = eventEngine ?? new EventEngine();
        _trayManager = new TrayManager(this);
        _robotController = new RobotController();
        _mediaService = mediaService ?? new WindowsMediaService(_eventEngine);
        _notificationService = notificationService ?? new WindowsNotificationService(_eventEngine);

        // Low-frequency timer for clock & status updates (1 update per second)
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += ClockTimer_Tick;

        // Equalizer wave animation timer (active only during media playback)
        _equalizerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _equalizerTimer.Tick += EqualizerTimer_Tick;

        // Debounced notes save timer
        _notesSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _notesSaveTimer.Tick += NotesSaveTimer_Tick;

        // Focus session timer
        _focusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _focusTimer.Tick += FocusTimer_Tick;

        ApplyPillStyleForState(CurrentState);

        AppLogger.Info("IslandWindow initialized.");
    }

    public IslandWindow(IMediaService? mediaService, EventEngine? eventEngine)
        : this(mediaService, null, eventEngine)
    {
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _trayManager.TrayContextMenu = (ContextMenu)Resources["IslandContextMenu"];
        _trayManager.Initialize(hwnd);

        // Register robots with controller
        _robotController.RegisterCompanion(CompactRobot);
        _robotController.RegisterCompanion(ExpandedRobot);
        _robotController.RegisterCompanion(CircleRobot);

        // Cache calendar elements
        _calendarDayLabels = new[] { DayLabel0, DayLabel1, DayLabel2, DayLabel3, DayLabel4, DayLabel5, DayLabel6 };
        _calendarNumLabels = new[] { DayNum0, DayNum1, DayNum2, DayNum3, DayNum4, DayNum5, DayNum6 };
        _calendarNumBorders = new[] { DayBorder0, DayBorder1, DayBorder2, DayBorder3, DayBorder4, DayBorder5, DayBorder6 };

        // Setup EventEngine subscriptions
        SetupEventSubscriptions();

        // Load persistent data
        try
        {
            NotesTextBox.Text = NotesStorage.LoadNotes();
            UpdateNotesCounts();
            _todos = TodoStorage.LoadTodos();
            RefreshTodosUI();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to load persistent storage: {ex.Message}");
        }

        // Position window top-center
        ResetToTopCenter(animate: false);
        ApplyPillStyleForState(CurrentState);
        SyncTransparencyMenuChecks(TransparencyMode);

        try
        {
            var menu = (ContextMenu)Resources["IslandContextMenu"];
            foreach (var item in menu.Items)
            {
                if (item is MenuItem mi && mi.Header?.ToString()?.Contains("Launch with Windows") == true)
                {
                    mi.IsChecked = StartupManager.IsStartupLaunchEnabled();
                    break;
                }
            }
        }
        catch { }

        // Initial updates
        UpdateClock();
        UpdateCalendar();
        UpdateSystemMonitor();
        _clockTimer.Start();

        // Initialize media session monitoring
        try
        {
            await _mediaService.InitializeAsync();
            if (_mediaService.CurrentSnapshot.HasActiveSession)
            {
                UpdateMediaUI(_mediaService.CurrentSnapshot);
            }
            else if (_lastMediaSnapshot?.HasActiveSession == true)
            {
                UpdateMediaUI(_lastMediaSnapshot);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to initialize media service in window: {ex.Message}");
        }

        // Initialize notification service
        try
        {
            await _notificationService.InitializeAsync();
            UpdateNotificationUI();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to initialize notification service in window: {ex.Message}");
        }

        AppLogger.Info($"IslandWindow loaded. Initial position: Left={Left}, Top={Top}.");
    }

    private void SetupEventSubscriptions()
    {
        // 1. Robot expression test events
        _subscriptions.Add(_eventEngine.Subscribe("test.robot.expression", e =>
        {
            if (e.Payload is string expression)
            {
                switch (expression.ToLowerInvariant())
                {
                    case "excited":
                        _robotController.TriggerExcitedTestEvent();
                        break;
                    case "confused":
                        _robotController.TriggerConfusedTestEvent();
                        break;
                    case "nod":
                        _robotController.TriggerNodTestEvent();
                        break;
                }
            }
        }));

        // 2. Island state changes
        _subscriptions.Add(_eventEngine.Subscribe("island.state_changed", e =>
        {
            if (e.Payload is IslandState state)
            {
                _robotController.OnIslandStateChanged(state);
            }
        }));

        // 3. Telemetry/UI Status subscriber
        _subscriptions.Add(_eventEngine.Subscribe(e =>
        {
            if (EventStatusText != null)
            {
                EventStatusText.Text = $"BUS: {e.EventType.ToUpperInvariant()} • {e.Priority}";
            }
        }));

        // 4. Media event subscribers
        _subscriptions.Add(_eventEngine.Subscribe(e =>
        {
            if (e.Category == EventCategory.Media && e.Payload is MediaSessionSnapshot snap)
            {
                UpdateMediaUI(snap);

                if (e.EventType == "media.track_changed" || (e.EventType == "media.playback_changed" && snap.IsPlaying))
                {
                    _robotController.TriggerNodTestEvent();
                }
            }
        }));

        // 5. Notification event subscribers
        _subscriptions.Add(_eventEngine.Subscribe(e =>
        {
            if (e.Category == EventCategory.Notification)
            {
                UpdateNotificationUI();

                if (e.EventType == "notification.arrived")
                {
                    if (e.Priority >= EventPriority.High)
                    {
                        _robotController.TriggerExcitedTestEvent();
                    }
                    else
                    {
                        _robotController.TriggerNodTestEvent();
                    }
                }
            }
        }));

        AppLogger.Info($"EventEngine subscriptions established. Count: {_subscriptions.Count}.");
    }

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        if (CurrentState == IslandState.Hidden)
        {
            _notificationService.PruneExpired();
            return;
        }

        UpdateClock();
        UpdateCalendar();

        if (CurrentState == IslandState.Expanded && _currentSlide == SlideView.Hud)
        {
            UpdateSystemMonitor();
        }

        _notificationService.PruneExpired();
        UpdateNotificationUI();

        bool isRecentSeek = (DateTime.UtcNow - _lastSeekUtc).TotalMilliseconds < 1500;

        // Advance live track timeline if media is playing and not actively scrubbing or recovering from seek
        if (_lastMediaSnapshot?.IsPlaying == true && !_isScrubbingTimeline)
        {
            _playbackElapsed = _playbackElapsed.Add(TimeSpan.FromSeconds(1));
            if (_playbackTotal > TimeSpan.Zero && _playbackElapsed > _playbackTotal)
            {
                _playbackElapsed = _playbackTotal;
            }
            UpdateTimelineUI();
        }

        // Periodically refresh active media session timeline to stay in sync with external browser/player
        if (_lastMediaSnapshot?.HasActiveSession == true && CurrentState == IslandState.Expanded && !_isScrubbingTimeline && !isRecentSeek)
        {
            _ = _mediaService.RefreshAsync();
        }
    }

    private void EqualizerTimer_Tick(object? sender, EventArgs e)
    {
        _waveIndex = (_waveIndex + 1) % WaveFrames.Length;
        var wave = WaveFrames[_waveIndex];

        if (CompactMediaVisualizer != null && CompactMediaVisualizer.Visibility == Visibility.Visible)
        {
            CompactMediaVisualizer.Text = wave;
        }

        if (MediaVisualizerText != null && MediaVisualizerText.Visibility == Visibility.Visible)
        {
            MediaVisualizerText.Text = wave;
        }
    }

    public static string FormatMediaTime(TimeSpan time, TimeSpan referenceTotal)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        if (double.IsNaN(time.TotalSeconds) || double.IsInfinity(time.TotalSeconds)) return "00:00";
        bool showHours = referenceTotal.TotalHours >= 1.0 || time.TotalHours >= 1.0;
        if (showHours)
        {
            return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
        }
        return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";
    }

    private void UpdateTimelineUI()
    {
        if (HudProgressElapsedText != null)
        {
            HudProgressElapsedText.Text = FormatMediaTime(_playbackElapsed, _playbackTotal);
        }
        if (HudProgressTotalText != null)
        {
            HudProgressTotalText.Text = _playbackTotal > TimeSpan.Zero 
                ? FormatMediaTime(_playbackTotal, _playbackTotal) 
                : "--:--";
        }
        if (HudProgressBar != null && HudTimelineTrack != null)
        {
            double trackWidth = HudTimelineTrack.ActualWidth > 0 ? HudTimelineTrack.ActualWidth : 120.0;
            double totalSec = Math.Max(1.0, _playbackTotal.TotalSeconds);
            double fraction = Math.Clamp(_playbackElapsed.TotalSeconds / totalSec, 0.0, 1.0);
            double targetWidth = Math.Clamp(fraction * trackWidth, 0.0, trackWidth);
            HudProgressBar.Width = targetWidth;

            if (HudScrubberThumb != null)
            {
                double thumbLeft = Math.Clamp(targetWidth - 6.0, 0.0, Math.Max(0.0, trackWidth - 12.0));
                if (HudScrubberThumbTransform != null)
                {
                    HudScrubberThumbTransform.X = thumbLeft;
                    HudScrubberThumb.Margin = new Thickness(0);
                }
                else
                {
                    HudScrubberThumb.Margin = new Thickness(thumbLeft, 0, 0, 0);
                }
                HudScrubberThumb.Visibility = targetWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void UpdateClock()
    {
        if (ClockText != null)
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm");
        }
    }

    private void UpdateCalendar()
    {
        var now = DateTime.Now;
        if (HudMonthText != null)
        {
            HudMonthText.Text = now.ToString("MMM");
        }

        if (HudClockText != null)
        {
            HudClockText.Text = now.ToString("HH:mm");
        }

        // Determine current week's Monday
        int delta = ((int)now.DayOfWeek - 1 + 7) % 7;
        var monday = now.Date.AddDays(-delta);

        for (int i = 0; i < 7; i++)
        {
            if (i >= _calendarDayLabels.Length || _calendarDayLabels[i] == null) continue;

            var dayDate = monday.AddDays(i);
            bool isToday = dayDate == now.Date;
            bool isWeekend = i >= 5;

            var dayLabel = _calendarDayLabels[i];
            var numLabel = _calendarNumLabels[i];
            var numBorder = _calendarNumBorders[i];

            if (isToday)
            {
                dayLabel.Text = dayDate.ToString("ddd").ToUpperInvariant();
                dayLabel.FontWeight = FontWeights.Bold;
                dayLabel.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)); // #38BDF8

                numLabel.Text = dayDate.Day.ToString();
                numLabel.FontWeight = FontWeights.Bold;
                numLabel.Foreground = new SolidColorBrush(Color.FromRgb(2, 8, 23));

                numBorder.Background = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            }
            else if (isWeekend)
            {
                dayLabel.Text = dayDate.ToString("ddd").ToUpperInvariant().Substring(0, 1);
                dayLabel.FontWeight = FontWeights.Normal;
                dayLabel.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // #EF4444

                numLabel.Text = dayDate.Day.ToString();
                numLabel.FontWeight = FontWeights.Bold;
                numLabel.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));

                numBorder.Background = Brushes.Transparent;
            }
            else
            {
                dayLabel.Text = dayDate.ToString("ddd").ToUpperInvariant().Substring(0, 1);
                dayLabel.FontWeight = FontWeights.Normal;
                dayLabel.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));

                numLabel.Text = dayDate.Day.ToString();
                numLabel.FontWeight = FontWeights.Medium;
                numLabel.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));

                numBorder.Background = Brushes.Transparent;
            }
        }
    }

    private void UpdateSystemMonitor()
    {
        try
        {
            var snap = _systemMonitor.GetCurrentSnapshot();

            if (HudCpuPercentText != null) HudCpuPercentText.Text = $"{snap.CpuPercent:0}%";
            if (HudRamPercentText != null) HudRamPercentText.Text = $"{snap.RamPercent:0}%";
            if (HudRamUsageText != null) HudRamUsageText.Text = $"{snap.RamUsedGb:0.0}/{snap.RamTotalGb:0}GB";
            if (HudDiskPercentText != null) HudDiskPercentText.Text = $"{snap.DiskPercent:0}%";
            if (HudDiskUsageText != null) HudDiskUsageText.Text = $"{snap.DiskUsedGb:0}/{snap.DiskTotalGb:0}GB";
            if (HudOverallLoadBadge != null) HudOverallLoadBadge.Text = $"{snap.CpuPercent:0}%";

            AnimateBarWidth(HudCpuBar, snap.CpuPercent, 80.0);
            AnimateBarWidth(HudRamBar, snap.RamPercent, 80.0);
            AnimateBarWidth(HudDiskBar, snap.DiskPercent, 80.0);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error updating system monitor UI: {ex.Message}");
        }
    }

    private void AnimateBarWidth(Border? bar, double percent, double maxBarWidth)
    {
        if (bar == null) return;
        double targetWidth = Math.Clamp(percent / 100.0 * maxBarWidth, 2.0, maxBarWidth);
        double currentWidth = bar.Width > 0 ? bar.Width : targetWidth;
        var anim = new DoubleAnimation(currentWidth, targetWidth, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        bar.BeginAnimation(WidthProperty, anim);
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        _robotController.OnCursorMoved(pos, new Size(Width, Height));
    }

    public void TransitionTo(IslandState targetState)
    {
        if (_isTransitioning && CurrentState == targetState) return;

        var previousState = CurrentState;
        AppLogger.Info($"Transitioning Island state from {previousState} to {targetState}.");
        _isTransitioning = true;
        CurrentState = targetState;

        _eventEngine.Publish(NovaEvent.CreateSystemEvent("island.state_changed", $"Island state changed to {targetState}", targetState, EventPriority.Normal));

        var duration = TimeSpan.FromMilliseconds(IslandDimensions.AnimationDurationMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (targetState == IslandState.Hidden)
        {
            StopCirclePulse();
            var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };
            fadeOut.Completed += (s, e) =>
            {
                Visibility = Visibility.Hidden;
                _isTransitioning = false;
            };
            BeginAnimation(OpacityProperty, fadeOut);
            return;
        }

        if (previousState == IslandState.Hidden)
        {
            Visibility = Visibility.Visible;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease });
        }

        double targetPillWidth;
        double targetPillHeight;
        CornerRadius targetCornerRadius;
        Thickness targetBorderThickness;

        if (targetState == IslandState.Circle)
        {
            targetPillWidth = IslandDimensions.CircleWidth;
            targetPillHeight = IslandDimensions.CircleHeight;
            targetCornerRadius = new CornerRadius(IslandDimensions.CircleCornerRadius);
            targetBorderThickness = new Thickness(1.2);

            StartCirclePulse();
            ApplyBubbleMotion(isExpanding: false);

            if (ExpandedPanel != null) ExpandedPanel.Visibility = Visibility.Collapsed;
            if (CompactPanel != null) CompactPanel.Visibility = Visibility.Collapsed;

            if (CirclePanel != null)
            {
                CirclePanel.Visibility = Visibility.Visible;
                CirclePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(140)));
            }
        }
        else if (targetState == IslandState.Compact)
        {
            StopCirclePulse();
            bool hasMedia = _lastMediaSnapshot?.HasActiveSession == true;
            targetPillWidth = hasMedia ? IslandDimensions.CompactMediaWidth : IslandDimensions.CompactWidth;
            targetPillHeight = hasMedia ? IslandDimensions.CompactMediaHeight : IslandDimensions.CompactHeight;
            double cr = hasMedia ? IslandDimensions.CompactMediaCornerRadius : IslandDimensions.CompactCornerRadius;
            targetCornerRadius = new CornerRadius(0, 0, cr, cr);
            targetBorderThickness = new Thickness(1.2, 0, 1.2, 1.2);

            ApplyBubbleMotion(isExpanding: previousState == IslandState.Circle);

            double targetWinWidth = hasMedia ? IslandDimensions.CompactMediaWindowWidth : IslandDimensions.CompactWindowWidth;
            double targetWinHeight = hasMedia ? IslandDimensions.CompactMediaWindowHeight : IslandDimensions.CompactWindowHeight;

            if (previousState == IslandState.Circle || hasMedia)
            {
                // Expanding to Compact or showing media: adjust window bounds immediately
                SetWindowDimensionsAndCenter(targetWinWidth, targetWinHeight);
            }

            if (ExpandedPanel != null) ExpandedPanel.Visibility = Visibility.Collapsed;
            if (CirclePanel != null) CirclePanel.Visibility = Visibility.Collapsed;

            if (CompactPanel != null)
            {
                CompactPanel.Visibility = Visibility.Visible;
                CompactPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(140)));
            }

            if (CompactMediaPanel != null)
            {
                CompactMediaPanel.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        else // Expanded
        {
            StopCirclePulse();
            targetPillWidth = IslandDimensions.ExpandedWidth;
            targetPillHeight = _currentSlide == SlideView.Hud ? IslandDimensions.ExpandedHeight : IslandDimensions.ExpandedWorkspaceHeight;
            targetCornerRadius = new CornerRadius(0, 0, IslandDimensions.ExpandedCornerRadius, IslandDimensions.ExpandedCornerRadius);
            targetBorderThickness = new Thickness(1.2, 0, 1.2, 1.2);

            ApplyBubbleMotion(isExpanding: true);

            if (HeaderTabsRow != null)
            {
                HeaderTabsRow.Visibility = _currentSlide == SlideView.Hud ? Visibility.Collapsed : Visibility.Visible;
            }

            double targetWindowHeight = _currentSlide == SlideView.Hud ? IslandDimensions.ExpandedHudWindowHeight : IslandDimensions.ExpandedWindowHeight;
            SetWindowDimensionsAndCenter(IslandDimensions.ExpandedWindowWidth, targetWindowHeight);

            if (CirclePanel != null) CirclePanel.Visibility = Visibility.Collapsed;
            if (CompactPanel != null) CompactPanel.Visibility = Visibility.Collapsed;

            if (ExpandedPanel != null)
            {
                ExpandedPanel.Visibility = Visibility.Visible;
                if (HeaderTabsRow != null)
                {
                    HeaderTabsRow.Visibility = _currentSlide == SlideView.Hud ? Visibility.Collapsed : Visibility.Visible;
                }
                ExpandedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(140)));
                if (_currentSlide == SlideView.Hud)
                {
                    UpdateSystemMonitor();
                }
            }
        }

        PillBorder.BorderThickness = targetBorderThickness;
        ApplyPillStyleForState(targetState);

        // Safely determine start dimensions
        double currentWidth = PillBorder.ActualWidth > 0 ? PillBorder.ActualWidth :
            (!double.IsNaN(PillBorder.Width) && PillBorder.Width > 0 ? PillBorder.Width :
            (previousState == IslandState.Circle ? IslandDimensions.CircleWidth :
            (previousState == IslandState.Compact ? IslandDimensions.CompactWidth : IslandDimensions.ExpandedWidth)));

        double currentHeight = PillBorder.ActualHeight > 0 ? PillBorder.ActualHeight :
            (!double.IsNaN(PillBorder.Height) && PillBorder.Height > 0 ? PillBorder.Height :
            (previousState == IslandState.Circle ? IslandDimensions.CircleHeight :
            (previousState == IslandState.Compact ? IslandDimensions.CompactHeight : IslandDimensions.ExpandedHeight)));

        var animWidth = new DoubleAnimation(currentWidth, targetPillWidth, duration) { EasingFunction = ease };
        var animHeight = new DoubleAnimation(currentHeight, targetPillHeight, duration) { EasingFunction = ease };
        var animCorner = new CornerRadiusAnimation(PillBorder.CornerRadius, targetCornerRadius, duration) { EasingFunction = ease };

        animWidth.Completed += (s, e) =>
        {
            if (targetState == IslandState.Circle)
            {
                SetWindowDimensionsAndCenter(IslandDimensions.CircleWindowWidth, IslandDimensions.CircleWindowHeight);
            }
            else if (targetState == IslandState.Compact)
            {
                bool hasMedia = _lastMediaSnapshot?.HasActiveSession == true;
                double winWidth = hasMedia ? IslandDimensions.CompactMediaWindowWidth : IslandDimensions.CompactWindowWidth;
                double winHeight = hasMedia ? IslandDimensions.CompactMediaWindowHeight : IslandDimensions.CompactWindowHeight;
                SetWindowDimensionsAndCenter(winWidth, winHeight);
            }
            _isTransitioning = false;
        };

        PillBorder.BeginAnimation(WidthProperty, animWidth);
        PillBorder.BeginAnimation(HeightProperty, animHeight);
        PillBorder.BeginAnimation(Border.CornerRadiusProperty, animCorner);

        if (PillGlossShine != null)
        {
            var animShine = new DoubleAnimation(0.2, 0.75, TimeSpan.FromMilliseconds(180)) { AutoReverse = true };
            PillGlossShine.BeginAnimation(OpacityProperty, animShine);
        }

        EnsureCentered();
    }

    private void SetWindowDimensionsAndCenter(double width, double height)
    {
        Width = width;
        Height = height;
        EnsureCentered();
    }

    private void EnsureCentered()
    {
        var workArea = ScreenHelper.GetWorkingArea(this);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Left = workArea.Left + (workArea.Width - Width) / 2.0;
        Top = workArea.Top + IslandDimensions.TopMargin;
    }

    private static void FadeOutPanel(UIElement? panel)
    {
        if (panel == null || panel.Visibility != Visibility.Visible) return;
        var fade = new DoubleAnimation(panel.Opacity, 0.0, TimeSpan.FromMilliseconds(90));
        fade.Completed += (s, e) => panel.Visibility = Visibility.Collapsed;
        panel.BeginAnimation(OpacityProperty, fade);
    }

    private void ApplyBubbleMotion(bool isExpanding)
    {
        if (PillScaleTransform == null) return;

        var animX = new DoubleAnimationUsingKeyFrames();
        animX.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animX.KeyFrames.Add(new SplineDoubleKeyFrame(isExpanding ? 1.12 : 0.88, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)), new KeySpline(0.2, 0.8, 0.2, 1.0)));
        animX.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)), new KeySpline(0.2, 0.8, 0.2, 1.0)));

        var animY = new DoubleAnimationUsingKeyFrames();
        animY.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animY.KeyFrames.Add(new SplineDoubleKeyFrame(isExpanding ? 0.88 : 1.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)), new KeySpline(0.2, 0.8, 0.2, 1.0)));
        animY.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)), new KeySpline(0.2, 0.8, 0.2, 1.0)));

        PillScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
        PillScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
    }

    private void StartCirclePulse()
    {
        if (CirclePulseScale == null || CirclePulseRing == null) return;

        var pulseScale = new DoubleAnimation(0.9, 1.15, TimeSpan.FromMilliseconds(1500))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        CirclePulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseScale);
        CirclePulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseScale);

        var pulseOpacity = new DoubleAnimation(0.2, 0.6, TimeSpan.FromMilliseconds(1500))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        CirclePulseRing.BeginAnimation(OpacityProperty, pulseOpacity);
    }

    private void StopCirclePulse()
    {
        CirclePulseScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CirclePulseScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CirclePulseRing?.BeginAnimation(OpacityProperty, null);
    }

    public void ResetToTopCenter(bool animate = false)
    {
        _isManuallyPositioned = false;
        EnsureCentered();
        AppLogger.Info($"Reset Island to Top-Center. Left={Left}, Top={Top}.");
    }

    public void ToggleAlwaysOnTop()
    {
        Topmost = !Topmost;
        _isPinned = Topmost;
        var menu = (ContextMenu)Resources["IslandContextMenu"];
        if (menu.Items.Count > 3 && menu.Items[3] is MenuItem alwaysOnTopItem)
        {
            alwaysOnTopItem.IsChecked = Topmost;
        }
        if (PinButton != null)
        {
            PinButton.Foreground = _isPinned 
                ? new SolidColorBrush(Color.FromRgb(0, 242, 254)) 
                : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            PinButton.ToolTip = _isPinned ? "Pinned: Will stay expanded & on top" : "Unpinned: Auto-collapses on click away";
        }
        AppLogger.Info($"Always On Top and Pin state set to: {Topmost}.");
    }

    private void Pill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _eventEngine.Publish(NovaEvent.Create("user.interaction", "Nova.UI", "User Clicked Island", category: EventCategory.UserAction));

        if (e.ClickCount == 2)
        {
            ResetToTopCenter(animate: false);
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is Button)
        {
            return;
        }

        if (CurrentState == IslandState.Circle)
        {
            TransitionTo(IslandState.Compact);
            e.Handled = true;
            return;
        }

        if (CurrentState == IslandState.Compact)
        {
            TransitionTo(IslandState.Expanded);
            e.Handled = true;
            return;
        }
    }

    private void Pill_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            TransitionTo(CurrentState == IslandState.Circle ? IslandState.Compact : IslandState.Circle);
            e.Handled = true;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Window is permanently docked/centered on the top edge.
        // DragMove is disabled to guarantee it sticks firmly to the middle upper screen.
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var contextMenu = (ContextMenu)Resources["IslandContextMenu"];
        contextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void ClampPosition()
    {
        EnsureCentered();
    }

    // SLIDE SWITCHING LOGIC WITH SMOOTH MOTIONS
    private void TabBtnHud_Click(object sender, RoutedEventArgs e) => SwitchToSlide(SlideView.Hud);
    private void TabBtnNotes_Click(object sender, RoutedEventArgs e) => SwitchToSlide(SlideView.Notes);
    private void TabBtnTodos_Click(object sender, RoutedEventArgs e) => SwitchToSlide(SlideView.Todos);
    private void TabBtnTools_Click(object sender, RoutedEventArgs e) => SwitchToSlide(SlideView.Tools);
    private void TabBtnMascot_Click(object sender, RoutedEventArgs e) => SwitchToSlide(SlideView.Mascot);

    public void NavigateToSlide(string slideName)
    {
        switch (slideName.ToLowerInvariant())
        {
            case "notes":
                SwitchToSlide(SlideView.Notes);
                break;
            case "todos":
            case "tasks":
                SwitchToSlide(SlideView.Todos);
                break;
            case "tools":
                SwitchToSlide(SlideView.Tools);
                break;
            case "mascot":
                SwitchToSlide(SlideView.Mascot);
                break;
            default:
                SwitchToSlide(SlideView.Hud);
                break;
        }
    }

    private void SwitchToSlide(SlideView targetSlide)
    {
        _currentSlide = targetSlide;
        UpdateTabButtons(targetSlide);

        if (HeaderTabsRow != null)
        {
            HeaderTabsRow.Visibility = targetSlide == SlideView.Hud ? Visibility.Collapsed : Visibility.Visible;
        }

        double targetHeight = targetSlide == SlideView.Hud ? IslandDimensions.ExpandedHeight : IslandDimensions.ExpandedWorkspaceHeight;
        Height = targetSlide == SlideView.Hud ? IslandDimensions.ExpandedHudWindowHeight : IslandDimensions.ExpandedWindowHeight;
        var animHeight = new DoubleAnimation(PillBorder.ActualHeight, targetHeight, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PillBorder.BeginAnimation(HeightProperty, animHeight);

        switch (targetSlide)
        {
            case SlideView.Hud:
                TransitionSlide(SlideHudPanel, SlideHudTransform);
                UpdateSystemMonitor();
                break;
            case SlideView.Notes:
                TransitionSlide(SlideNotesPanel, SlideNotesTransform);
                break;
            case SlideView.Todos:
                TransitionSlide(SlideTodosPanel, SlideTodosTransform);
                break;
            case SlideView.Tools:
                TransitionSlide(SlideToolsPanel, SlideToolsTransform);
                break;
            case SlideView.Mascot:
                TransitionSlide(SlideMascotPanel, SlideMascotTransform);
                break;
        }
    }

    private void BtnHudMascot_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSlide == SlideView.Hud)
        {
            SwitchToSlide(SlideView.Mascot);
        }
        else
        {
            SwitchToSlide(SlideView.Hud);
        }
    }

    private void UpdateTabButtons(SlideView active)
    {
        SetTabActive(TabBtnHud, active == SlideView.Hud);
        SetTabActive(TabBtnNotes, active == SlideView.Notes);
        SetTabActive(TabBtnTodos, active == SlideView.Todos);
        SetTabActive(TabBtnTools, active == SlideView.Tools);
        SetTabActive(TabBtnMascot, active == SlideView.Mascot);
    }

    private void SetTabActive(Button? btn, bool isActive)
    {
        if (btn == null) return;
        if (isActive)
        {
            btn.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)); // #0284C7
            btn.Foreground = Brushes.White;
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)); // #38BDF8
        }
        else
        {
            btn.Background = new SolidColorBrush(Color.FromRgb(17, 22, 36));
            btn.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        }
    }

    private void TransitionSlide(FrameworkElement targetPanel, TranslateTransform? targetTransform)
    {
        SlideHudPanel.Visibility = Visibility.Collapsed;
        SlideNotesPanel.Visibility = Visibility.Collapsed;
        SlideTodosPanel.Visibility = Visibility.Collapsed;
        SlideToolsPanel.Visibility = Visibility.Collapsed;
        SlideMascotPanel.Visibility = Visibility.Collapsed;

        targetPanel.Visibility = Visibility.Visible;
        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        targetPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease });

        if (targetTransform != null)
        {
            targetTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(24.0, 0.0, duration) { EasingFunction = ease });
        }
    }

    private void HandleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CurrentState == IslandState.Expanded)
        {
            if (_currentSlide != SlideView.Hud)
            {
                SwitchToSlide(SlideView.Hud);
            }
            else
            {
                TransitionTo(IslandState.Compact);
            }
        }
        else
        {
            TransitionTo(IslandState.Expanded);
        }
        e.Handled = true;
    }

    private void VolumeControl_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        int delta = e.Delta > 0 ? 5 : -5;
        SetVolume(_currentVolume + delta);
        e.Handled = true;
    }

    private void SetVolume(int newVol, bool toggleMute = false)
    {
        if (toggleMute)
        {
            _isMuted = !_isMuted;
        }
        else
        {
            _isMuted = false;
            _currentVolume = Math.Clamp(newVol, 0, 100);
            if (_currentVolume == 0) _isMuted = true;
        }

        if (HudVolumeLevelText != null)
        {
            if (_isMuted)
            {
                HudVolumeLevelText.Text = "MUTE";
                HudVolumeLevelText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }
            else
            {
                HudVolumeLevelText.Text = $"{_currentVolume}";
                HudVolumeLevelText.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            }
        }

        if (HudVolumePill != null)
        {
            double maxH = 46.0;
            double targetH = _isMuted ? 8.0 : Math.Max(8.0, (_currentVolume / 100.0) * maxH);
            var animH = new DoubleAnimation(HudVolumePill.Height, targetH, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            HudVolumePill.BeginAnimation(HeightProperty, animH);
            HudVolumePill.Opacity = _isMuted ? 0.35 : 1.0;
        }
    }

    private void VolumePill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SetVolume(_currentVolume, toggleMute: true);
        e.Handled = true;
    }

    private void AlbumArtwork_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (MediaPlayPauseBtn != null && MediaPlayPauseBtn.IsEnabled)
        {
            MediaPlayPause_Click(sender, e);
        }
        else
        {
            OpenYouTubeMusic_Click(sender, e);
        }
        _robotController.TriggerNodTestEvent();
        e.Handled = true;
    }

    private void TimelineTrack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement track && track.ActualWidth > 0)
        {
            track.CaptureMouse();
            _isScrubbingTimeline = true;
            _lastSeekUtc = DateTime.UtcNow;

            var pos = e.GetPosition(track);
            double fraction = Math.Clamp(pos.X / track.ActualWidth, 0.0, 1.0);
            var targetTime = TimeSpan.FromSeconds(fraction * Math.Max(1.0, _playbackTotal.TotalSeconds));
            _playbackElapsed = targetTime;
            UpdateTimelineUI();
        }
        e.Handled = true;
    }

    private void TimelineTrack_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement track && track.ActualWidth > 0)
        {
            var pos = e.GetPosition(track);
            double fraction = Math.Clamp(pos.X / track.ActualWidth, 0.0, 1.0);
            var hoverTime = TimeSpan.FromSeconds(fraction * Math.Max(1.0, _playbackTotal.TotalSeconds));
            track.ToolTip = $"Seek to {FormatMediaTime(hoverTime, _playbackTotal)}";

            if (_isScrubbingTimeline && e.LeftButton == MouseButtonState.Pressed)
            {
                _playbackElapsed = hoverTime;
                UpdateTimelineUI();
            }
        }
    }

    private async void TimelineTrack_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement track && _isScrubbingTimeline)
        {
            track.ReleaseMouseCapture();
            _isScrubbingTimeline = false;
            _lastSeekUtc = DateTime.UtcNow;

            var pos = e.GetPosition(track);
            double fraction = Math.Clamp(pos.X / track.ActualWidth, 0.0, 1.0);
            var targetTime = TimeSpan.FromSeconds(fraction * Math.Max(1.0, _playbackTotal.TotalSeconds));
            _playbackElapsed = targetTime;
            UpdateTimelineUI();
            await _mediaService.TrySeekAsync(targetTime);
            _robotController.TriggerNodTestEvent();

            if (!track.IsMouseOver)
            {
                var animHeight = new DoubleAnimation(5.0, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                HudGrooveBar?.BeginAnimation(HeightProperty, animHeight);
                HudProgressBar?.BeginAnimation(HeightProperty, animHeight);

                var animThumb = new DoubleAnimation(12.0, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                HudScrubberThumb?.BeginAnimation(WidthProperty, animThumb);
                HudScrubberThumb?.BeginAnimation(HeightProperty, animThumb);
            }
        }
        e.Handled = true;
    }

    private void TimelineTrack_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isScrubbingTimeline)
        {
            _isScrubbingTimeline = false;
            _lastSeekUtc = DateTime.UtcNow;
        }
    }

    private void TimelineTrack_MouseEnter(object sender, MouseEventArgs e)
    {
        var animHeight = new DoubleAnimation(7.0, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        HudGrooveBar?.BeginAnimation(HeightProperty, animHeight);
        HudProgressBar?.BeginAnimation(HeightProperty, animHeight);

        var animThumb = new DoubleAnimation(14.0, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        HudScrubberThumb?.BeginAnimation(WidthProperty, animThumb);
        HudScrubberThumb?.BeginAnimation(HeightProperty, animThumb);
    }

    private void TimelineTrack_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isScrubbingTimeline)
        {
            var animHeight = new DoubleAnimation(5.0, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            HudGrooveBar?.BeginAnimation(HeightProperty, animHeight);
            HudProgressBar?.BeginAnimation(HeightProperty, animHeight);

            var animThumb = new DoubleAnimation(12.0, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            HudScrubberThumb?.BeginAnimation(WidthProperty, animThumb);
            HudScrubberThumb?.BeginAnimation(HeightProperty, animThumb);
        }
    }

    private void PillBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        if (CurrentState == IslandState.Compact || CurrentState == IslandState.Circle)
        {
            double targetScale = CurrentState == IslandState.Circle ? 1.08 : 1.03;
            var animScale = new DoubleAnimation(1.0, targetScale, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            PillScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animScale);
            PillScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animScale);

            if (PillBorder.Effect is DropShadowEffect shadow)
            {
                double targetOpacity = CurrentState == IslandState.Circle ? 0.75 : (TransparencyMode == PillTransparencyMode.UltraClear ? 0.35 : 0.50);
                var animGlow = new DoubleAnimation(shadow.Opacity, targetOpacity, TimeSpan.FromMilliseconds(160));
                shadow.BeginAnimation(DropShadowEffect.OpacityProperty, animGlow);
            }
        }
    }

    private void PillBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        if (CurrentState == IslandState.Compact || CurrentState == IslandState.Circle)
        {
            var animScale = new DoubleAnimation(PillScaleTransform.ScaleX, 1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            PillScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animScale);
            PillScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animScale);

            if (PillBorder.Effect is DropShadowEffect shadow)
            {
                double targetOpacity = GetDefaultShadowOpacity(CurrentState);
                var animGlow = new DoubleAnimation(shadow.Opacity, targetOpacity, TimeSpan.FromMilliseconds(200));
                shadow.BeginAnimation(DropShadowEffect.OpacityProperty, animGlow);
            }
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        var contextMenu = (ContextMenu)Resources["IslandContextMenu"];
        if (contextMenu.IsOpen) return;

        // Auto-collapse to compact notch when user clicks away to another application (unless pinned)
        if (!_isPinned && CurrentState == IslandState.Expanded && !_isTransitioning)
        {
            if (_currentSlide != SlideView.Hud)
            {
                SwitchToSlide(SlideView.Hud);
            }
            TransitionTo(IslandState.Compact);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (CurrentState == IslandState.Expanded)
            {
                if (_currentSlide != SlideView.Hud)
                {
                    SwitchToSlide(SlideView.Hud);
                }
                else
                {
                    TransitionTo(IslandState.Compact);
                }
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Space && CurrentState == IslandState.Expanded && _currentSlide == SlideView.Hud)
        {
            if (MediaPlayPauseBtn != null && MediaPlayPauseBtn.IsEnabled)
            {
                MediaPlayPause_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }

    // QUICK NOTES LOGIC
    private void NotesTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateNotesCounts();
        _notesSaveTimer.Stop();
        _notesSaveTimer.Start();
    }

    private void NotesSaveTimer_Tick(object? sender, EventArgs e)
    {
        _notesSaveTimer.Stop();
        NotesStorage.SaveNotes(NotesTextBox.Text);
    }

    private void UpdateNotesCounts()
    {
        if (NotesCharCountText != null && NotesWordCountText != null && NotesTextBox != null)
        {
            var text = NotesTextBox.Text;
            NotesCharCountText.Text = $"{text.Length} chars";
            var words = string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            NotesWordCountText.Text = $"{words} words";
        }
    }

    private async void NotesCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(NotesTextBox.Text);
            if (NotesCopyFeedbackText != null)
            {
                NotesCopyFeedbackText.Text = "✓ Copied to clipboard!";
                await Task.Delay(2000);
                NotesCopyFeedbackText.Text = "";
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Clipboard copy failed: {ex.Message}");
        }
    }

    private void NotesClear_Click(object sender, RoutedEventArgs e)
    {
        NotesTextBox.Text = string.Empty;
        NotesStorage.SaveNotes(string.Empty);
    }

    // TO-DO LIST LOGIC
    private void TodoAdd_Click(object sender, RoutedEventArgs e)
    {
        var text = TodoInputBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            _todos.Add(new TodoItem { Title = text, IsCompleted = false });
            TodoInputBox.Text = string.Empty;
            TodoStorage.SaveTodos(_todos);
            RefreshTodosUI();
        }
    }

    private void TodoInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TodoAdd_Click(sender, e);
        }
    }

    private void TodoFilterAll_Click(object sender, RoutedEventArgs e)
    {
        _todoFilter = "All";
        RefreshTodosUI();
    }

    private void TodoFilterActive_Click(object sender, RoutedEventArgs e)
    {
        _todoFilter = "Active";
        RefreshTodosUI();
    }

    private void TodoFilterDone_Click(object sender, RoutedEventArgs e)
    {
        _todoFilter = "Done";
        RefreshTodosUI();
    }

    private void TodoClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        _todos.RemoveAll(t => t.IsCompleted);
        TodoStorage.SaveTodos(_todos);
        RefreshTodosUI();
    }

    private void RefreshTodosUI()
    {
        if (TodoItemsContainer == null) return;
        TodoItemsContainer.Children.Clear();

        var filtered = _todoFilter switch
        {
            "Active" => _todos.Where(t => !t.IsCompleted).ToList(),
            "Done" => _todos.Where(t => t.IsCompleted).ToList(),
            _ => _todos.ToList()
        };

        int activeCount = _todos.Count(t => !t.IsCompleted);
        if (TodoCountText != null)
        {
            TodoCountText.Text = $"{activeCount} active task{(activeCount == 1 ? "" : "s")}";
        }

        foreach (var item in filtered)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chk = new CheckBox
            {
                IsChecked = item.IsCompleted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            chk.Click += (s, e) =>
            {
                item.IsCompleted = chk.IsChecked == true;
                TodoStorage.SaveTodos(_todos);
                RefreshTodosUI();
            };

            var tb = new TextBlock
            {
                Text = item.Title,
                FontSize = 10.5,
                Foreground = item.IsCompleted ? new SolidColorBrush(Color.FromRgb(100, 116, 139)) : new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            var delBtn = new Button
            {
                Content = "✕",
                FontSize = 8.5,
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(0)
            };
            delBtn.Click += (s, e) =>
            {
                _todos.Remove(item);
                TodoStorage.SaveTodos(_todos);
                RefreshTodosUI();
            };

            Grid.SetColumn(chk, 0);
            Grid.SetColumn(tb, 1);
            Grid.SetColumn(delBtn, 2);

            row.Children.Add(chk);
            row.Children.Add(tb);
            row.Children.Add(delBtn);

            TodoItemsContainer.Children.Add(row);
        }
    }

    // FOCUS TIMER LOGIC
    private void FocusTimer_Tick(object? sender, EventArgs e)
    {
        if (_focusRemaining > TimeSpan.Zero)
        {
            _focusRemaining = _focusRemaining.Subtract(TimeSpan.FromSeconds(1));
            UpdateTimerDisplay();
        }
        else
        {
            _focusTimer.Stop();
            _isFocusTimerRunning = false;
            TimerStartPauseBtn.Content = "▶ Start";
            _robotController.TriggerNodTestEvent();
            _eventEngine.Publish(NovaEvent.Create("system.focus_completed", "FocusTimer", "Focus Session Completed!", category: EventCategory.System));
        }
    }

    private void UpdateTimerDisplay()
    {
        if (TimerDisplayText != null)
        {
            TimerDisplayText.Text = $"{_focusRemaining:mm\\:ss}";
        }
    }

    private void TimerStartPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isFocusTimerRunning)
        {
            _focusTimer.Stop();
            _isFocusTimerRunning = false;
            TimerStartPauseBtn.Content = "▶ Start";
        }
        else
        {
            _focusTimer.Start();
            _isFocusTimerRunning = true;
            TimerStartPauseBtn.Content = "⏸ Pause";
        }
    }

    private void TimerReset_Click(object sender, RoutedEventArgs e)
    {
        _focusTimer.Stop();
        _isFocusTimerRunning = false;
        _focusRemaining = _focusPreset;
        TimerStartPauseBtn.Content = "▶ Start";
        UpdateTimerDisplay();
    }

    private void TimerPreset25_Click(object sender, RoutedEventArgs e) => SetTimerPreset(TimeSpan.FromMinutes(25));
    private void TimerPreset5_Click(object sender, RoutedEventArgs e) => SetTimerPreset(TimeSpan.FromMinutes(5));
    private void TimerPreset15_Click(object sender, RoutedEventArgs e) => SetTimerPreset(TimeSpan.FromMinutes(15));

    private void SetTimerPreset(TimeSpan preset)
    {
        _focusPreset = preset;
        TimerReset_Click(this, new RoutedEventArgs());
    }

    // QUICK LAUNCHERS
    private void LaunchCalc_Click(object sender, RoutedEventArgs e) => SafeLaunchApp("calc.exe");
    private void LaunchNotepad_Click(object sender, RoutedEventArgs e) => SafeLaunchApp("notepad.exe");
    private void LaunchSnip_Click(object sender, RoutedEventArgs e) => SafeLaunchApp("snippingtool.exe");
    private void LaunchTaskMgr_Click(object sender, RoutedEventArgs e) => SafeLaunchApp("taskmgr.exe");

    private void SafeLaunchApp(string exe)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to launch {exe}: {ex.Message}");
        }
    }

    // UI Click Handlers
    private void ExpandButton_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Expanded);
    private void CompactButton_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Compact);
    private void CircleButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        TransitionTo(IslandState.Circle);
    }
    private void HideButton_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Hidden);
    private void PinButton_Click(object sender, RoutedEventArgs e) => ToggleAlwaysOnTop();

    private void MenuExpand_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Expanded);
    private void MenuCompact_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Compact);
    private void MenuCircle_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Circle);
    private void MenuResetPosition_Click(object sender, RoutedEventArgs e) => ResetToTopCenter(animate: true);
    private void MenuAlwaysOnTop_Click(object sender, RoutedEventArgs e) => ToggleAlwaysOnTop();
    private void MenuLaunchAtStartup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            bool enabled = menuItem.IsChecked;
            StartupManager.SetStartupLaunch(enabled);
            AppLogger.Info($"Launch at Windows startup toggled: {enabled}");
        }
    }
    private void MenuHide_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Hidden);
    private void MenuExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    public void SetTransparencyMode(PillTransparencyMode mode)
    {
        TransparencyMode = mode;
        SyncTransparencyMenuChecks(mode);
        ApplyPillStyleForState(CurrentState);
        AppLogger.Info($"Pill transparency mode changed to: {mode}.");
    }

    private void MenuTranslucentGlass_Click(object sender, RoutedEventArgs e) => SetTransparencyMode(PillTransparencyMode.TranslucentGlass);
    private void MenuUltraClear_Click(object sender, RoutedEventArgs e) => SetTransparencyMode(PillTransparencyMode.UltraClear);
    private void MenuFrostedGlass_Click(object sender, RoutedEventArgs e) => SetTransparencyMode(PillTransparencyMode.FrostedGlass);
    private void MenuSolidDark_Click(object sender, RoutedEventArgs e) => SetTransparencyMode(PillTransparencyMode.SolidDark);

    private void SyncTransparencyMenuChecks(PillTransparencyMode mode)
    {
        try
        {
            var menu = (ContextMenu)Resources["IslandContextMenu"];
            foreach (var item in menu.Items)
            {
                if (item is MenuItem mi && mi.Header?.ToString()?.Contains("Pill Transparency") == true)
                {
                    foreach (var subItem in mi.Items)
                    {
                        if (subItem is MenuItem subMi)
                        {
                            var header = subMi.Header?.ToString() ?? "";
                            subMi.IsChecked = mode switch
                            {
                                PillTransparencyMode.TranslucentGlass => header.Contains("Translucent"),
                                PillTransparencyMode.UltraClear => header.Contains("Ultra Clear"),
                                PillTransparencyMode.FrostedGlass => header.Contains("Frosted"),
                                PillTransparencyMode.SolidDark => header.Contains("Solid"),
                                _ => false
                            };
                        }
                    }
                }
            }
        }
        catch { }
    }

    public void ApplyPillStyleForState(IslandState state)
    {
        if (PillBorder == null) return;

        if (state == IslandState.Circle)
        {
            if (TransparencyMode == PillTransparencyMode.UltraClear)
            {
                PillBorder.Background = new SolidColorBrush(Color.FromArgb(0x04, 0, 0, 0));
            }
            else if (TransparencyMode == PillTransparencyMode.SolidDark)
            {
                PillBorder.Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(11, 15, 25), 0.0),
                        new GradientStop(Color.FromRgb(6, 9, 16), 0.5),
                        new GradientStop(Color.FromRgb(2, 4, 8), 1.0)
                    },
                    new Point(0, 0),
                    new Point(0, 1));
            }
            else
            {
                byte alpha = TransparencyMode == PillTransparencyMode.FrostedGlass ? (byte)0x45 : (byte)0x18;
                PillBorder.Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(alpha, 0, 242, 254), 0.0),
                        new GradientStop(Color.FromArgb(alpha, 6, 12, 24), 1.0)
                    },
                    new Point(0, 0),
                    new Point(1, 1));
            }

            PillBorder.BorderBrush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0xC0, 0, 242, 254), 0.0),
                    new GradientStop(Color.FromArgb(0x80, 56, 189, 248), 0.5),
                    new GradientStop(Color.FromArgb(0xC0, 0, 242, 254), 1.0)
                },
                new Point(0, 0),
                new Point(1, 1));

            PillBorder.BorderThickness = new Thickness(1.2);

            PillBorder.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0, 242, 254),
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.45
            };

            if (PillGlossShine != null)
            {
                PillGlossShine.Visibility = Visibility.Collapsed;
            }
        }
        else if (state == IslandState.Compact)
        {
            if (TransparencyMode == PillTransparencyMode.UltraClear)
            {
                PillBorder.Background = new SolidColorBrush(Color.FromArgb(0x06, 0, 0, 0));
                PillBorder.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0, 0, 0),
                    BlurRadius = 10,
                    ShadowDepth = 1,
                    Direction = 270,
                    Opacity = 0.15
                };
            }
            else if (TransparencyMode == PillTransparencyMode.SolidDark)
            {
                PillBorder.Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(11, 15, 25), 0.0),
                        new GradientStop(Color.FromRgb(6, 9, 16), 0.5),
                        new GradientStop(Color.FromRgb(2, 4, 8), 1.0)
                    },
                    new Point(0, 0),
                    new Point(0, 1));
                PillBorder.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0, 0, 0),
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Direction = 270,
                    Opacity = 0.85
                };
            }
            else
            {
                byte a1 = TransparencyMode == PillTransparencyMode.FrostedGlass ? (byte)0x65 : (byte)0x32;
                byte a2 = TransparencyMode == PillTransparencyMode.FrostedGlass ? (byte)0x50 : (byte)0x22;
                byte a3 = TransparencyMode == PillTransparencyMode.FrostedGlass ? (byte)0x40 : (byte)0x18;

                PillBorder.Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(a1, 11, 17, 28), 0.0),
                        new GradientStop(Color.FromArgb(a2, 6, 9, 16), 0.5),
                        new GradientStop(Color.FromArgb(a3, 2, 4, 8), 1.0)
                    },
                    new Point(0, 0),
                    new Point(0, 1));

                PillBorder.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0, 0, 0),
                    BlurRadius = 14,
                    ShadowDepth = 2,
                    Direction = 270,
                    Opacity = 0.25
                };
            }

            PillBorder.BorderBrush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x50, 30, 41, 59), 0.0),
                    new GradientStop(Color.FromArgb(0x95, 56, 189, 248), 0.25),
                    new GradientStop(Color.FromArgb(0x50, 30, 41, 59), 0.75),
                    new GradientStop(Color.FromArgb(0x30, 15, 23, 42), 1.0)
                },
                new Point(0, 0),
                new Point(1, 1));

            PillBorder.BorderThickness = new Thickness(1.2, 0, 1.2, 1.2);

            if (PillGlossShine != null)
            {
                PillGlossShine.Visibility = Visibility.Visible;
                PillGlossShine.Opacity = 0.12;
            }
        }
        else // Expanded
        {
            PillBorder.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0xF4, 11, 15, 25), 0.0),
                    new GradientStop(Color.FromArgb(0xF8, 6, 9, 16), 0.5),
                    new GradientStop(Color.FromArgb(0xFC, 2, 4, 8), 1.0)
                },
                new Point(0, 0),
                new Point(0, 1));

            PillBorder.BorderBrush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(30, 41, 59), 0.0),
                    new GradientStop(Color.FromRgb(56, 189, 248), 0.25),
                    new GradientStop(Color.FromRgb(30, 41, 59), 0.75),
                    new GradientStop(Color.FromRgb(15, 23, 42), 1.0)
                },
                new Point(0, 0),
                new Point(1, 1));

            PillBorder.BorderThickness = new Thickness(1.2, 0, 1.2, 1.2);

            PillBorder.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0, 0, 0),
                BlurRadius = 28,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = 0.85
            };

            if (PillGlossShine != null)
            {
                PillGlossShine.Visibility = Visibility.Visible;
                PillGlossShine.Opacity = 0.45;
            }
        }
    }

    private double GetDefaultShadowOpacity(IslandState state)
    {
        return state switch
        {
            IslandState.Circle => 0.45,
            IslandState.Compact => TransparencyMode switch
            {
                PillTransparencyMode.UltraClear => 0.15,
                PillTransparencyMode.SolidDark => 0.85,
                _ => 0.25
            },
            _ => 0.85
        };
    }

    private void MenuTestExcited_Click(object sender, RoutedEventArgs e) => _eventEngine.Publish(NovaEvent.Create("test.robot.expression", "Menu", "excited"));
    private void MenuTestConfused_Click(object sender, RoutedEventArgs e) => _eventEngine.Publish(NovaEvent.Create("test.robot.expression", "Menu", "confused"));
    private void MenuTestNod_Click(object sender, RoutedEventArgs e) => _eventEngine.Publish(NovaEvent.Create("test.robot.expression", "Menu", "nod"));

    private void BtnExcited_Click(object sender, RoutedEventArgs e) => _eventEngine.Publish(NovaEvent.Create("test.robot.expression", "UI", "excited"));
    private void BtnConfused_Click(object sender, RoutedEventArgs e) => _eventEngine.Publish(NovaEvent.Create("test.robot.expression", "UI", "confused"));
    private void BtnNod_Click(object sender, RoutedEventArgs e) => _eventEngine.Publish(NovaEvent.Create("test.robot.expression", "UI", "nod"));

    private void MenuPersonalityCalm_Click(object sender, RoutedEventArgs e) => _robotController.SetPersonality(RobotPersonality.Calm);
    private void MenuPersonalityPlayful_Click(object sender, RoutedEventArgs e) => _robotController.SetPersonality(RobotPersonality.Playful);
    private void MenuPersonalityMinimal_Click(object sender, RoutedEventArgs e) => _robotController.SetPersonality(RobotPersonality.Minimal);

    private void MenuBatterySaver_Click(object sender, RoutedEventArgs e)
    {
        var item = (MenuItem)sender;
        _robotController.SetBatterySaver(item.IsChecked);
    }

    private void MenuReducedMotion_Click(object sender, RoutedEventArgs e)
    {
        var item = (MenuItem)sender;
        _robotController.SetReducedMotion(item.IsChecked);
    }

    private void MenuFollowCursor_Click(object sender, RoutedEventArgs e)
    {
        bool isChecked = true;
        if (sender is MenuItem menuItem)
        {
            isChecked = menuItem.IsChecked;
        }

        _robotController.SetFollowCursor(isChecked);
        SyncFollowCursorMenuChecks(isChecked);
        AppLogger.Info($"Eye Tracking (Follow Cursor) set to: {isChecked}.");
    }

    private void BtnToggleEyeTracking_Click(object sender, RoutedEventArgs e)
    {
        bool newState = !_robotController.Settings.FollowCursor;
        _robotController.SetFollowCursor(newState);
        SyncFollowCursorMenuChecks(newState);
        AppLogger.Info($"Eye Tracking toggled via Mascot Slide button to: {newState}.");
    }

    private void SyncFollowCursorMenuChecks(bool isChecked)
    {
        try
        {
            var menu = (ContextMenu)Resources["IslandContextMenu"];
            foreach (var item in menu.Items)
            {
                if (item is MenuItem mi && mi.Header?.ToString()?.Contains("Eye Tracking") == true)
                {
                    mi.IsChecked = isChecked;
                }
                else if (item is MenuItem subMenu && subMenu.HasItems)
                {
                    foreach (var subItem in subMenu.Items)
                    {
                        if (subItem is MenuItem subMi && subMi.Header?.ToString()?.Contains("Eye Tracking") == true)
                        {
                            subMi.IsChecked = isChecked;
                        }
                    }
                }
            }

            if (BtnToggleEyeTracking != null)
            {
                BtnToggleEyeTracking.Content = isChecked ? "👀 Eye Tracking: ON" : "👀 Eye Tracking: OFF";
            }
        }
        catch { }
    }

    public void UpdateMediaUI(MediaSessionSnapshot snapshot)
    {
        _lastMediaSnapshot = snapshot;
        if (snapshot.HasActiveSession)
        {
            // Compact View
            if (CompactMediaPanel != null && CompactDefaultPanel != null)
            {
                CompactDefaultPanel.Visibility = Visibility.Collapsed;
                CompactMediaPanel.Visibility = Visibility.Visible;
                CompactMediaText.Text = snapshot.TrackInfo.DisplayString;
                CompactPlayPauseBtn.Content = snapshot.IsPlaying ? "⏸" : "▶";
                CompactPlayPauseBtn.IsEnabled = snapshot.Capabilities.CanTogglePlayPause || snapshot.Capabilities.CanPlay || snapshot.Capabilities.CanPause;

                if (CompactMediaVisualizer != null)
                {
                    CompactMediaVisualizer.Visibility = snapshot.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
                    CompactMediaVisualizer.Foreground = snapshot.IsYouTubeMusic
                        ? new SolidColorBrush(Color.FromRgb(255, 68, 68))
                        : new SolidColorBrush(Color.FromRgb(56, 189, 248));
                }
            }

            // Expanded View (HUD Bar)
            if (MediaTitleText != null)
            {
                MediaTitleText.Text = !string.IsNullOrWhiteSpace(snapshot.TrackInfo.Title) 
                    ? snapshot.TrackInfo.Title 
                    : "Active Audio Stream";
            }

            if (MediaArtistText != null)
            {
                var artistAlbum = !string.IsNullOrWhiteSpace(snapshot.TrackInfo.Artist)
                    ? snapshot.TrackInfo.Artist
                    : snapshot.SourceDisplayName;
                if (!string.IsNullOrWhiteSpace(snapshot.TrackInfo.AlbumTitle))
                {
                    artistAlbum += $" • {snapshot.TrackInfo.AlbumTitle}";
                }
                MediaArtistText.Text = artistAlbum;
            }

            if (MediaBadgeText != null)
            {
                if (snapshot.IsYouTubeMusic)
                {
                    MediaBadgeText.Text = $"YOUTUBE MUSIC • {snapshot.PlaybackStatus.ToString().ToUpperInvariant()}";
                    MediaBadgeText.Foreground = snapshot.IsPlaying 
                        ? new SolidColorBrush(Color.FromRgb(255, 68, 68)) 
                        : new SolidColorBrush(Color.FromRgb(148, 163, 184));
                }
                else
                {
                    MediaBadgeText.Text = $"{snapshot.SourceDisplayName.ToUpperInvariant()} • {snapshot.PlaybackStatus.ToString().ToUpperInvariant()}";
                    MediaBadgeText.Foreground = snapshot.IsPlaying 
                        ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) 
                        : new SolidColorBrush(Color.FromRgb(148, 163, 184));
                }
            }

            if (MediaVisualizerText != null)
            {
                MediaVisualizerText.Visibility = snapshot.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
                MediaVisualizerText.Foreground = snapshot.IsYouTubeMusic
                    ? new SolidColorBrush(Color.FromRgb(255, 68, 68))
                    : new SolidColorBrush(Color.FromRgb(56, 189, 248));
            }

            // Artwork
            if (MediaArtworkImage != null && MediaPlaceholderIcon != null)
            {
                if (snapshot.TrackInfo.HasArtwork)
                {
                    var bitmap = ImageHelper.CreateBitmapSourceFromBytes(snapshot.TrackInfo.ArtworkBytes);
                    if (bitmap != null)
                    {
                        MediaArtworkImage.Source = bitmap;
                        MediaArtworkImage.Visibility = Visibility.Visible;
                        MediaPlaceholderIcon.Visibility = Visibility.Collapsed;
                        if (HudAlbumColorBack != null) HudAlbumColorBack.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        MediaArtworkImage.Visibility = Visibility.Collapsed;
                        MediaPlaceholderIcon.Visibility = Visibility.Visible;
                        if (HudAlbumColorBack != null) HudAlbumColorBack.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    MediaArtworkImage.Visibility = Visibility.Collapsed;
                    MediaPlaceholderIcon.Visibility = Visibility.Visible;
                    if (HudAlbumColorBack != null) HudAlbumColorBack.Visibility = Visibility.Visible;
                }
            }

            // Transport Buttons
            if (MediaPlayPauseBtn != null)
            {
                MediaPlayPauseBtn.IsEnabled = snapshot.Capabilities.CanTogglePlayPause || snapshot.Capabilities.CanPlay || snapshot.Capabilities.CanPause;
                MediaPlayPauseBtn.Content = snapshot.IsPlaying ? "⏸" : "▶";
            }

            if (MediaNextBtn != null)
            {
                MediaNextBtn.IsEnabled = snapshot.Capabilities.CanNext;
            }

            if (MediaPrevBtn != null)
            {
                MediaPrevBtn.IsEnabled = snapshot.Capabilities.CanPrevious;
            }

            if (snapshot.Duration > TimeSpan.Zero)
            {
                _playbackTotal = snapshot.Duration;
            }
            bool isRecentSeek = (DateTime.UtcNow - _lastSeekUtc).TotalMilliseconds < 1500;
            if (!_isScrubbingTimeline && !isRecentSeek)
            {
                _playbackElapsed = snapshot.Position;
            }
            UpdateTimelineUI();

            if (snapshot.IsPlaying)
            {
                if (!_equalizerTimer.IsEnabled) _equalizerTimer.Start();
            }
            else
            {
                if (_equalizerTimer.IsEnabled) _equalizerTimer.Stop();
            }

            if (CurrentState == IslandState.Compact)
            {
                UpdateCompactPillSizeForMedia(true);
            }
        }
        else
        {
            if (_equalizerTimer.IsEnabled) _equalizerTimer.Stop();
            // Compact View - Revert to default
            if (CompactMediaPanel != null && CompactDefaultPanel != null)
            {
                CompactMediaPanel.Visibility = Visibility.Collapsed;
                CompactDefaultPanel.Visibility = Visibility.Visible;
            }

            if (CurrentState == IslandState.Compact)
            {
                UpdateCompactPillSizeForMedia(false);
            }

            // Expanded View - Revert to Standby
            if (MediaTitleText != null)
            {
                MediaTitleText.Text = "No Media Playing";
            }

            if (MediaArtistText != null)
            {
                MediaArtistText.Text = "Awaiting audio session";
            }

            if (MediaBadgeText != null)
            {
                MediaBadgeText.Text = "MEDIA • STANDBY";
                MediaBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }

            if (MediaVisualizerText != null)
            {
                MediaVisualizerText.Visibility = Visibility.Collapsed;
            }

            if (MediaArtworkImage != null && MediaPlaceholderIcon != null)
            {
                MediaArtworkImage.Visibility = Visibility.Collapsed;
                MediaPlaceholderIcon.Visibility = Visibility.Visible;
                if (HudAlbumColorBack != null) HudAlbumColorBack.Visibility = Visibility.Visible;
            }

            if (MediaPlayPauseBtn != null)
            {
                MediaPlayPauseBtn.IsEnabled = false;
                MediaPlayPauseBtn.Content = "▶";
            }

            if (MediaNextBtn != null)
            {
                MediaNextBtn.IsEnabled = false;
            }

            if (MediaPrevBtn != null)
            {
                MediaPrevBtn.IsEnabled = false;
            }

            if (HudProgressBar != null)
            {
                HudProgressBar.Width = 0;
            }
        }
    }

    /// <summary>
    /// Dynamically expands or shrinks the Compact notch when media starts or stops,
    /// increasing height and width to showcase the active track neatly below the robot face.
    /// </summary>
    private void UpdateCompactPillSizeForMedia(bool hasMedia)
    {
        if (CurrentState != IslandState.Compact || _isTransitioning) return;

        double targetWidth = hasMedia ? IslandDimensions.CompactMediaWidth : IslandDimensions.CompactWidth;
        double targetHeight = hasMedia ? IslandDimensions.CompactMediaHeight : IslandDimensions.CompactHeight;
        double cr = hasMedia ? IslandDimensions.CompactMediaCornerRadius : IslandDimensions.CompactCornerRadius;
        var targetCorner = new CornerRadius(0, 0, cr, cr);

        double winWidth = hasMedia ? IslandDimensions.CompactMediaWindowWidth : IslandDimensions.CompactWindowWidth;
        double winHeight = hasMedia ? IslandDimensions.CompactMediaWindowHeight : IslandDimensions.CompactWindowHeight;

        // If expanding to show media, widen window bounds immediately so pill is not clipped
        if (hasMedia)
        {
            SetWindowDimensionsAndCenter(winWidth, winHeight);
        }

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        double curW = PillBorder.ActualWidth > 0 ? PillBorder.ActualWidth : 
            (!double.IsNaN(PillBorder.Width) && PillBorder.Width > 0 ? PillBorder.Width : IslandDimensions.CompactWidth);
        double curH = PillBorder.ActualHeight > 0 ? PillBorder.ActualHeight : 
            (!double.IsNaN(PillBorder.Height) && PillBorder.Height > 0 ? PillBorder.Height : IslandDimensions.CompactHeight);

        if (Math.Abs(curW - targetWidth) > 1.0 || Math.Abs(curH - targetHeight) > 1.0)
        {
            var animW = new DoubleAnimation(curW, targetWidth, duration) { EasingFunction = ease };
            var animH = new DoubleAnimation(curH, targetHeight, duration) { EasingFunction = ease };
            var animCr = new CornerRadiusAnimation(PillBorder.CornerRadius, targetCorner, duration) { EasingFunction = ease };

            if (!hasMedia)
            {
                animH.Completed += (s, e) =>
                {
                    if (CurrentState == IslandState.Compact && (_lastMediaSnapshot == null || !_lastMediaSnapshot.HasActiveSession))
                    {
                        SetWindowDimensionsAndCenter(winWidth, winHeight);
                    }
                };
            }

            PillBorder.BeginAnimation(WidthProperty, animW);
            PillBorder.BeginAnimation(HeightProperty, animH);
            PillBorder.BeginAnimation(Border.CornerRadiusProperty, animCr);
        }
    }

    public void UpdateNotificationUI()
    {
        var current = _notificationService.CurrentNotification;

        if (current != null)
        {
            // Compact View: Notification has high visual urgency
            if (CompactNotificationPanel != null && CompactMediaPanel != null && CompactDefaultPanel != null)
            {
                CompactDefaultPanel.Visibility = Visibility.Collapsed;
                CompactMediaPanel.Visibility = Visibility.Collapsed;
                CompactNotificationPanel.Visibility = Visibility.Visible;
                CompactNotificationText.Text = current.CompactSummary;
                if (CompactDismissNotificationBtn != null)
                {
                    CompactDismissNotificationBtn.ToolTip = current.DismissTooltip;
                }
            }

            // Expanded View: Notification Banner
            if (ExpandedNotificationBanner != null)
            {
                ExpandedNotificationBanner.Visibility = Visibility.Visible;
                if (NotificationSourceBadge != null)
                    NotificationSourceBadge.Text = current.SourceDisplayName.ToUpperInvariant();
                if (NotificationTitleText != null)
                    NotificationTitleText.Text = current.Title;
                if (NotificationMessageText != null)
                    NotificationMessageText.Text = current.Message;
                if (ExpandedDismissNotificationBtn != null)
                {
                    ExpandedDismissNotificationBtn.Content = current.CanDismissFromActionCenter ? "✕ Dismiss" : "✕ Clear";
                    ExpandedDismissNotificationBtn.ToolTip = current.DismissTooltip;
                }
                if (ExpandedClearAllNotificationsBtn != null)
                {
                    ExpandedClearAllNotificationsBtn.ToolTip = _notificationService.CanDismissNativeNotifications
                        ? "Clear all notifications from NOVA Island and native Windows Action Center"
                        : "Clear all notifications from NOVA Island queue";
                }
            }
        }
        else
        {
            // No active notification: revert to media or default
            if (CompactNotificationPanel != null && CompactMediaPanel != null && CompactDefaultPanel != null)
            {
                CompactNotificationPanel.Visibility = Visibility.Collapsed;

                var activeSnapshot = (_lastMediaSnapshot?.HasActiveSession == true) ? _lastMediaSnapshot : _mediaService.CurrentSnapshot;
                if (activeSnapshot.HasActiveSession)
                {
                    CompactMediaPanel.Visibility = Visibility.Visible;
                    CompactDefaultPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    CompactMediaPanel.Visibility = Visibility.Collapsed;
                    CompactDefaultPanel.Visibility = Visibility.Visible;
                }
            }

            if (ExpandedNotificationBanner != null)
            {
                ExpandedNotificationBanner.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async void MediaPlayPause_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _mediaService.TryTogglePlayPauseAsync();
            _robotController.TriggerNodTestEvent();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Play/Pause click failed: {ex.Message}");
        }
    }

    private async void MediaNext_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _mediaService.TryNextAsync();
            _robotController.TriggerNodTestEvent();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Next Track click failed: {ex.Message}");
        }
    }

    private async void MediaPrev_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _mediaService.TryPreviousAsync();
            _robotController.TriggerNodTestEvent();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Previous Track click failed: {ex.Message}");
        }
    }

    private void OpenYouTubeMusic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://music.youtube.com/")
            {
                UseShellExecute = true
            });
            AppLogger.Info("Opened https://music.youtube.com/ via user request.");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to open YouTube Music in browser: {ex.Message}");
        }
    }

    private void MenuOpenYouTubeMusic_Click(object sender, RoutedEventArgs e)
    {
        OpenYouTubeMusic_Click(sender, e);
    }

    private async void DismissCurrentNotification_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var current = _notificationService.CurrentNotification;
            if (current != null)
            {
                await _notificationService.TryDismissAsync(current.Id);
                UpdateNotificationUI();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to dismiss current notification: {ex.Message}");
        }
    }

    private async void ClearAllNotifications_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _notificationService.ClearAllAsync();
            UpdateNotificationUI();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to clear all notifications: {ex.Message}");
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var contextMenu = (ContextMenu)Resources["IslandContextMenu"];
        contextMenu.IsOpen = true;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _clockTimer.Stop();
        _equalizerTimer.Stop();
        _notesSaveTimer.Stop();
        _focusTimer.Stop();

        // Flush persistent user data so nothing vanishes after restart
        try
        {
            if (_todos != null) TodoStorage.SaveTodos(_todos);
            if (NotesTextBox != null) NotesStorage.SaveNotes(NotesTextBox.Text);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error saving state during Window_Closed: {ex.Message}");
        }

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();

        _mediaService.Dispose();
        _notificationService.Dispose();
        _trayManager.Dispose();
        _eventEngine.Dispose();
        _robotController.Dispose();

        AppLogger.Info("IslandWindow closed and all resources cleanly disposed.");
    }
}
