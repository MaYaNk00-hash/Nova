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
    private SlideView _currentSlide = SlideView.Hud;

    private List<TodoItem> _todos = new();
    private string _todoFilter = "All";
    private TimeSpan _focusRemaining = TimeSpan.FromMinutes(25);
    private TimeSpan _focusPreset = TimeSpan.FromMinutes(25);
    private bool _isFocusTimerRunning = false;

    private TextBlock[] _calendarDayLabels = Array.Empty<TextBlock>();
    private TextBlock[] _calendarNumLabels = Array.Empty<TextBlock>();
    private Border[] _calendarNumBorders = Array.Empty<Border>();

    public IslandState CurrentState { get; private set; } = IslandState.Compact;
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
        UpdateClock();
        UpdateCalendar();
        UpdateSystemMonitor();
        _notificationService.PruneExpired();
        UpdateNotificationUI();
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

        AppLogger.Info($"Transitioning Island state from {CurrentState} to {targetState}.");
        _isTransitioning = true;
        CurrentState = targetState;

        _eventEngine.Publish(NovaEvent.CreateSystemEvent("island.state_changed", $"Island state changed to {targetState}", targetState, EventPriority.Normal));

        var duration = TimeSpan.FromMilliseconds(IslandDimensions.AnimationDurationMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (targetState == IslandState.Hidden)
        {
            var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };
            fadeOut.Completed += (s, e) =>
            {
                Visibility = Visibility.Hidden;
                _isTransitioning = false;
            };
            BeginAnimation(OpacityProperty, fadeOut);
            return;
        }

        if (CurrentState == IslandState.Hidden)
        {
            Visibility = Visibility.Visible;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease });
        }

        double targetPillWidth;
        double targetPillHeight;
        double targetCornerRadius;

        if (targetState == IslandState.Compact)
        {
            targetPillWidth = IslandDimensions.CompactWidth;
            targetPillHeight = IslandDimensions.CompactHeight;
            targetCornerRadius = IslandDimensions.CompactCornerRadius;

            var fadeExpanded = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(120));
            fadeExpanded.Completed += (s, e) =>
            {
                ExpandedPanel.Visibility = Visibility.Collapsed;
                CompactPanel.Visibility = Visibility.Visible;
                CompactPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160)));

                Width = IslandDimensions.CompactWindowWidth;
                Height = IslandDimensions.CompactWindowHeight;
                var workArea = ScreenHelper.GetWorkingArea(this);
                Top = workArea.Top + IslandDimensions.TopMargin;
                if (!_isManuallyPositioned)
                {
                    Left = workArea.Left + (workArea.Width - Width) / 2.0;
                }
            };
            ExpandedPanel.BeginAnimation(OpacityProperty, fadeExpanded);
        }
        else // Expanded
        {
            targetPillWidth = IslandDimensions.ExpandedWidth;
            targetPillHeight = _currentSlide == SlideView.Hud ? IslandDimensions.ExpandedHeight : IslandDimensions.ExpandedWorkspaceHeight;
            targetCornerRadius = IslandDimensions.ExpandedCornerRadius;

            if (HeaderTabsRow != null)
            {
                HeaderTabsRow.Visibility = _currentSlide == SlideView.Hud ? Visibility.Collapsed : Visibility.Visible;
            }

            Width = IslandDimensions.ExpandedWindowWidth;
            Height = IslandDimensions.ExpandedWindowHeight;
            var workArea = ScreenHelper.GetWorkingArea(this);
            Top = workArea.Top + IslandDimensions.TopMargin;
            if (!_isManuallyPositioned)
            {
                Left = workArea.Left + (workArea.Width - Width) / 2.0;
            }

            var fadeCompact = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(120));
            fadeCompact.Completed += (s, e) =>
            {
                CompactPanel.Visibility = Visibility.Collapsed;
                ExpandedPanel.Visibility = Visibility.Visible;
                if (HeaderTabsRow != null)
                {
                    HeaderTabsRow.Visibility = _currentSlide == SlideView.Hud ? Visibility.Collapsed : Visibility.Visible;
                }
                ExpandedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160)));
            };
            CompactPanel.BeginAnimation(OpacityProperty, fadeCompact);
        }

        // Safely determine start dimensions
        double currentWidth = PillBorder.ActualWidth > 0 ? PillBorder.ActualWidth :
            (!double.IsNaN(PillBorder.Width) && PillBorder.Width > 0 ? PillBorder.Width :
            (CurrentState == IslandState.Compact ? IslandDimensions.CompactWidth : IslandDimensions.ExpandedWidth));

        double currentHeight = PillBorder.ActualHeight > 0 ? PillBorder.ActualHeight :
            (!double.IsNaN(PillBorder.Height) && PillBorder.Height > 0 ? PillBorder.Height :
            (CurrentState == IslandState.Compact ? IslandDimensions.CompactHeight : IslandDimensions.ExpandedHeight));

        var animWidth = new DoubleAnimation(currentWidth, targetPillWidth, duration) { EasingFunction = ease };
        var animHeight = new DoubleAnimation(currentHeight, targetPillHeight, duration) { EasingFunction = ease };
        var animCorner = new CornerRadiusAnimation(PillBorder.CornerRadius, new CornerRadius(0, 0, targetCornerRadius, targetCornerRadius), duration) { EasingFunction = ease };

        animWidth.Completed += (s, e) =>
        {
            _isTransitioning = false;
        };

        PillBorder.BeginAnimation(WidthProperty, animWidth);
        PillBorder.BeginAnimation(HeightProperty, animHeight);
        PillBorder.BeginAnimation(Border.CornerRadiusProperty, animCorner);

        if (PillGlossShine != null)
        {
            var animShineCorner = new CornerRadiusAnimation(PillGlossShine.CornerRadius, new CornerRadius(0, 0, 0, 0), duration) { EasingFunction = ease };
            PillGlossShine.BeginAnimation(Border.CornerRadiusProperty, animShineCorner);
        }

        // Keep centered horizontally if not manually dragged
        if (!_isManuallyPositioned)
        {
            var workArea = ScreenHelper.GetWorkingArea(this);
            double targetLeft = workArea.Left + (workArea.Width - Width) / 2.0;
            double currentLeft = !double.IsNaN(Left) ? Left : targetLeft;
            var animLeft = new DoubleAnimation(currentLeft, targetLeft, duration) { EasingFunction = ease };
            BeginAnimation(LeftProperty, animLeft);
        }
    }

    public void ResetToTopCenter(bool animate = true)
    {
        var workArea = ScreenHelper.GetWorkingArea(this);
        var targetPoint = ScreenHelper.CalculateTopCenter(workArea, Width, IslandDimensions.TopMargin);

        _isManuallyPositioned = false;

        if (animate)
        {
            var duration = TimeSpan.FromMilliseconds(IslandDimensions.AnimationDurationMs);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(LeftProperty, new DoubleAnimation(Left, targetPoint.X, duration) { EasingFunction = ease });
            BeginAnimation(TopProperty, new DoubleAnimation(Top, targetPoint.Y, duration) { EasingFunction = ease });
        }
        else
        {
            Left = targetPoint.X;
            Top = targetPoint.Y;
        }

        AppLogger.Info($"Reset Island to Top-Center on monitor work area: {workArea.Width}x{workArea.Height}.");
    }

    public void ToggleAlwaysOnTop()
    {
        Topmost = !Topmost;
        var menu = (ContextMenu)Resources["IslandContextMenu"];
        var alwaysOnTopItem = (MenuItem)menu.Items[3];
        alwaysOnTopItem.IsChecked = Topmost;
        PinButton.Foreground = Topmost ? System.Windows.Media.Brushes.Cyan : System.Windows.Media.Brushes.Gray;
        AppLogger.Info($"Always On Top set to: {Topmost}.");
    }

    private void Pill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _eventEngine.Publish(NovaEvent.Create("user.interaction", "Nova.UI", "User Clicked Island", category: EventCategory.UserAction));

        if (e.ClickCount == 2)
        {
            ResetToTopCenter(animate: true);
            e.Handled = true;
            return;
        }

        if (CurrentState == IslandState.Compact && e.OriginalSource is not Button)
        {
            double initialLeft = Left;
            double initialTop = Top;

            try
            {
                DragMove();
            }
            catch { }

            if (Math.Abs(Left - initialLeft) > 3 || Math.Abs(Top - initialTop) > 3)
            {
                _isManuallyPositioned = true;
                ClampPosition();
            }
            else
            {
                TransitionTo(IslandState.Expanded);
            }

            e.Handled = true;
            return;
        }

        if (e.OriginalSource is not Button)
        {
            try
            {
                DragMove();
                _isManuallyPositioned = true;
                ClampPosition();
            }
            catch { }
            e.Handled = true;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not Button)
        {
            try
            {
                DragMove();
                _isManuallyPositioned = true;
                ClampPosition();
            }
            catch { }
        }
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var contextMenu = (ContextMenu)Resources["IslandContextMenu"];
        contextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void ClampPosition()
    {
        var workArea = ScreenHelper.GetWorkingArea(this);
        var clamped = ScreenHelper.ClampToWorkArea(workArea, Left, Top, Width, Height);
        Left = clamped.X;
        // Firmly fixed on the top edge rather than floating everywhere
        Top = workArea.Top + IslandDimensions.TopMargin;
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
        var animHeight = new DoubleAnimation(PillBorder.ActualHeight, targetHeight, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PillBorder.BeginAnimation(HeightProperty, animHeight);

        switch (targetSlide)
        {
            case SlideView.Hud:
                TransitionSlide(SlideHudPanel, SlideHudTransform);
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
        if (_currentSlide == SlideView.Hud)
        {
            SwitchToSlide(SlideView.Notes);
        }
        else
        {
            SwitchToSlide(SlideView.Hud);
        }
        e.Handled = true;
    }

    private void VolumePill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HudVolumeLevelText != null && HudVolumePill != null)
        {
            if (HudVolumeLevelText.Text == "100")
            {
                HudVolumeLevelText.Text = "MUTE";
                HudVolumeLevelText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                HudVolumePill.Opacity = 0.35;
            }
            else
            {
                HudVolumeLevelText.Text = "100";
                HudVolumeLevelText.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                HudVolumePill.Opacity = 1.0;
            }
        }
        e.Handled = true;
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
    private void HideButton_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Hidden);
    private void PinButton_Click(object sender, RoutedEventArgs e) => ToggleAlwaysOnTop();

    private void MenuExpand_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Expanded);
    private void MenuCompact_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Compact);
    private void MenuResetPosition_Click(object sender, RoutedEventArgs e) => ResetToTopCenter(animate: true);
    private void MenuAlwaysOnTop_Click(object sender, RoutedEventArgs e) => ToggleAlwaysOnTop();
    private void MenuHide_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Hidden);
    private void MenuExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

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

            if (HudProgressBar != null)
            {
                HudProgressBar.Width = snapshot.IsPlaying ? 55 : 20;
            }
        }
        else
        {
            // Compact View - Revert to default
            if (CompactMediaPanel != null && CompactDefaultPanel != null)
            {
                CompactMediaPanel.Visibility = Visibility.Collapsed;
                CompactDefaultPanel.Visibility = Visibility.Visible;
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
        _notesSaveTimer.Stop();
        _focusTimer.Stop();

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();

        _mediaService.Dispose();
        _notificationService.Dispose();
        _trayManager.Dispose();
        _eventEngine.Dispose();

        AppLogger.Info("IslandWindow closed and all resources cleanly disposed.");
    }
}
