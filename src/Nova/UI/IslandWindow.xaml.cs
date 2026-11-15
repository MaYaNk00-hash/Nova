using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nova.Core;
using Nova.Events;
using Nova.UI.Robot;

namespace Nova.UI;

public partial class IslandWindow : Window
{
    private readonly TrayManager _trayManager;
    private readonly DispatcherTimer _clockTimer;
    private readonly RobotController _robotController;
    private readonly EventEngine _eventEngine;
    private readonly List<IDisposable> _subscriptions = new();
    private bool _isManuallyPositioned = false;
    private bool _isTransitioning = false;

    public IslandState CurrentState { get; private set; } = IslandState.Compact;
    public bool IsAlwaysOnTop => Topmost;
    public bool IsManuallyPositioned => _isManuallyPositioned;
    public RobotController RobotController => _robotController;
    public EventEngine EventEngine => _eventEngine;

    public IslandWindow()
    {
        InitializeComponent();

        _trayManager = new TrayManager(this);
        _robotController = new RobotController();
        _eventEngine = new EventEngine();

        // Low-frequency timer for clock & status updates (1 update per second)
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += ClockTimer_Tick;

        AppLogger.Info("IslandWindow initialized.");
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _trayManager.TrayContextMenu = (ContextMenu)Resources["IslandContextMenu"];
        _trayManager.Initialize(hwnd);

        // Register robots with controller
        _robotController.RegisterCompanion(CompactRobot);
        _robotController.RegisterCompanion(ExpandedRobot);

        // Setup EventEngine subscriptions
        SetupEventSubscriptions();

        // Position window top-center
        ResetToTopCenter(animate: false);

        // Start clock timer
        UpdateClock();
        _clockTimer.Start();

        AppLogger.Info($"IslandWindow loaded. Initial position: Left={Left}, Top={Top}.");
    }

    private void SetupEventSubscriptions()
    {
        // 1. Companion reaction subscriptions
        _subscriptions.Add(_eventEngine.Subscribe("companion.excited", e => _robotController.TriggerExcitedTestEvent()));
        _subscriptions.Add(_eventEngine.Subscribe("companion.confused", e => _robotController.TriggerConfusedTestEvent()));
        _subscriptions.Add(_eventEngine.Subscribe("companion.nod", e => _robotController.TriggerNodTestEvent()));
        _subscriptions.Add(_eventEngine.Subscribe("user.interaction", e => _robotController.OnUserInteraction()));

        // 2. Island state subscriptions
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

        AppLogger.Info($"EventEngine subscriptions established. Count: {_subscriptions.Count}.");
    }

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        UpdateClock();
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm");
        var workArea = ScreenHelper.GetWorkingArea(this);
        MonitorInfoText.Text = $"{workArea.Width:F0}x{workArea.Height:F0} WorkArea";
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

        // Publish state change event through the Core Event Engine
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
            };
            ExpandedPanel.BeginAnimation(OpacityProperty, fadeExpanded);
        }
        else // Expanded
        {
            targetPillWidth = IslandDimensions.ExpandedWidth;
            targetPillHeight = IslandDimensions.ExpandedHeight;
            targetCornerRadius = IslandDimensions.ExpandedCornerRadius;

            var fadeCompact = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(120));
            fadeCompact.Completed += (s, e) =>
            {
                CompactPanel.Visibility = Visibility.Collapsed;
                ExpandedPanel.Visibility = Visibility.Visible;
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
        var animCorner = new CornerRadiusAnimation(PillBorder.CornerRadius, new CornerRadius(targetCornerRadius), duration) { EasingFunction = ease };

        animWidth.Completed += (s, e) =>
        {
            _isTransitioning = false;
        };

        PillBorder.BeginAnimation(WidthProperty, animWidth);
        PillBorder.BeginAnimation(HeightProperty, animHeight);
        PillBorder.BeginAnimation(Border.CornerRadiusProperty, animCorner);

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
        // Publish user interaction event to the Core Event Engine
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
        Top = clamped.Y;
    }

    // UI Click Handlers
    private void ExpandButton_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Expanded);
    private void CompactButton_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Compact);
    private void HideButton_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Hidden);
    private void PinButton_Click(object sender, RoutedEventArgs e) => ToggleAlwaysOnTop();

    // Robot Interactive Test Buttons - Publish via EventEngine
    private void BtnExcited_Click(object sender, RoutedEventArgs e)
    {
        _eventEngine.Publish(NovaEvent.CreateCompanionEvent("companion.excited", "Companion Excited", "Triggered from UI test button", EventPriority.High));
    }

    private void BtnConfused_Click(object sender, RoutedEventArgs e)
    {
        _eventEngine.Publish(NovaEvent.CreateCompanionEvent("companion.confused", "Companion Confused", "Triggered from UI test button", EventPriority.Normal));
    }

    private void BtnNod_Click(object sender, RoutedEventArgs e)
    {
        _eventEngine.Publish(NovaEvent.CreateCompanionEvent("companion.nod", "Companion Nod", "Triggered from UI test button", EventPriority.Low));
    }

    // Context Menu Handlers
    private void MenuExpand_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Expanded);
    private void MenuCompact_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Compact);
    private void MenuResetPosition_Click(object sender, RoutedEventArgs e) => ResetToTopCenter(animate: true);
    private void MenuAlwaysOnTop_Click(object sender, RoutedEventArgs e) => ToggleAlwaysOnTop();
    private void MenuHide_Click(object sender, RoutedEventArgs e) => TransitionTo(IslandState.Hidden);
    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("Exit requested by user via context menu.");
        Close();
    }

    // Robot Context Menu Handlers - Publish via EventEngine
    private void MenuTestExcited_Click(object sender, RoutedEventArgs e)
    {
        _eventEngine.Publish(NovaEvent.CreateCompanionEvent("companion.excited", "Companion Excited", "Triggered from Context Menu", EventPriority.High));
    }

    private void MenuTestConfused_Click(object sender, RoutedEventArgs e)
    {
        _eventEngine.Publish(NovaEvent.CreateCompanionEvent("companion.confused", "Companion Confused", "Triggered from Context Menu", EventPriority.Normal));
    }

    private void MenuTestNod_Click(object sender, RoutedEventArgs e)
    {
        _eventEngine.Publish(NovaEvent.CreateCompanionEvent("companion.nod", "Companion Nod", "Triggered from Context Menu", EventPriority.Low));
    }

    private void MenuPersonalityCalm_Click(object sender, RoutedEventArgs e)
    {
        _robotController.SetPersonality(RobotPersonality.Calm);
        RobotStatusText.Text = "COMPANION • CALM";
    }

    private void MenuPersonalityPlayful_Click(object sender, RoutedEventArgs e)
    {
        _robotController.SetPersonality(RobotPersonality.Playful);
        RobotStatusText.Text = "COMPANION • PLAYFUL";
    }

    private void MenuPersonalityMinimal_Click(object sender, RoutedEventArgs e)
    {
        _robotController.SetPersonality(RobotPersonality.Minimal);
        RobotStatusText.Text = "COMPANION • MINIMAL";
    }

    private void MenuBatterySaver_Click(object sender, RoutedEventArgs e)
    {
        var item = (MenuItem)sender;
        _robotController.SetBatterySaver(item.IsChecked);
        RobotStatusText.Text = item.IsChecked ? "COMPANION • BATTERY SAVER" : "COMPANION • ACTIVE";
    }

    private void MenuReducedMotion_Click(object sender, RoutedEventArgs e)
    {
        var item = (MenuItem)sender;
        _robotController.SetReducedMotion(item.IsChecked);
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _clockTimer.Stop();
        _trayManager.Dispose();

        // Dispose all subscriptions and the engine
        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();
        _eventEngine.Dispose();

        AppLogger.Info("IslandWindow closed and EventEngine disposed.");
    }
}
