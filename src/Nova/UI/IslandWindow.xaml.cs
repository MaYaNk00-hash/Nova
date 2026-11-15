using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nova.Core;
using Nova.Events;
using Nova.Media;
using Nova.Notifications;
using Nova.UI.Robot;

namespace Nova.UI;

public partial class IslandWindow : Window
{
    private readonly TrayManager _trayManager;
    private readonly DispatcherTimer _clockTimer;
    private readonly RobotController _robotController;
    private readonly EventEngine _eventEngine;
    private readonly IMediaService _mediaService;
    private readonly INotificationService _notificationService;
    private readonly List<IDisposable> _subscriptions = new();
    private bool _isManuallyPositioned = false;
    private bool _isTransitioning = false;

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

        // Setup EventEngine subscriptions
        SetupEventSubscriptions();

        // Position window top-center
        ResetToTopCenter(animate: false);

        // Start clock timer
        UpdateClock();
        _clockTimer.Start();

        // Initialize media session monitoring
        try
        {
            await _mediaService.InitializeAsync();
            UpdateMediaUI(_mediaService.CurrentSnapshot);
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

    public void UpdateMediaUI(MediaSessionSnapshot snapshot)
    {
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
            }

            // Expanded View
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
                MediaBadgeText.Text = $"{snapshot.SourceDisplayName.ToUpperInvariant()} • {snapshot.PlaybackStatus.ToString().ToUpperInvariant()}";
                MediaBadgeText.Foreground = snapshot.IsPlaying 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248)) 
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
            }

            if (MediaSourceText != null)
            {
                MediaSourceText.Text = snapshot.SourceDisplayName;
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
                    }
                    else
                    {
                        MediaArtworkImage.Visibility = Visibility.Collapsed;
                        MediaPlaceholderIcon.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    MediaArtworkImage.Visibility = Visibility.Collapsed;
                    MediaPlaceholderIcon.Visibility = Visibility.Visible;
                }
            }

            // Controls
            if (MediaPlayPauseBtn != null)
            {
                MediaPlayPauseBtn.Content = snapshot.IsPlaying ? "⏸" : "▶";
                MediaPlayPauseBtn.IsEnabled = snapshot.Capabilities.CanTogglePlayPause || snapshot.Capabilities.CanPlay || snapshot.Capabilities.CanPause;
            }

            if (MediaPrevBtn != null)
            {
                MediaPrevBtn.IsEnabled = snapshot.Capabilities.CanPrevious;
            }

            if (MediaNextBtn != null)
            {
                MediaNextBtn.IsEnabled = snapshot.Capabilities.CanNext;
            }
        }
        else
        {
            // Inactive / No media session
            if (CompactMediaPanel != null && CompactDefaultPanel != null)
            {
                CompactMediaPanel.Visibility = Visibility.Collapsed;
                CompactDefaultPanel.Visibility = Visibility.Visible;
            }

            if (MediaTitleText != null)
                MediaTitleText.Text = "No Media Playing";
            if (MediaArtistText != null)
                MediaArtistText.Text = "Awaiting audio session";
            if (MediaBadgeText != null)
            {
                MediaBadgeText.Text = "MEDIA • STANDBY";
                MediaBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));
            }
            if (MediaSourceText != null)
                MediaSourceText.Text = "";

            if (MediaArtworkImage != null && MediaPlaceholderIcon != null)
            {
                MediaArtworkImage.Source = null;
                MediaArtworkImage.Visibility = Visibility.Collapsed;
                MediaPlaceholderIcon.Visibility = Visibility.Visible;
            }

            if (MediaPlayPauseBtn != null)
            {
                MediaPlayPauseBtn.Content = "▶";
                MediaPlayPauseBtn.IsEnabled = false;
            }
            if (MediaPrevBtn != null)
                MediaPrevBtn.IsEnabled = false;
            if (MediaNextBtn != null)
                MediaNextBtn.IsEnabled = false;
        }
    }

    private async void MediaPlayPause_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _mediaService.TryTogglePlayPauseAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"MediaPlayPause_Click failed: {ex.Message}");
        }
    }

    private async void MediaPrev_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _mediaService.TryPreviousAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"MediaPrev_Click failed: {ex.Message}");
        }
    }

    private async void MediaNext_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _mediaService.TryNextAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"MediaNext_Click failed: {ex.Message}");
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
            }
        }
        else
        {
            // No active notification: revert to media or default
            if (CompactNotificationPanel != null && CompactMediaPanel != null && CompactDefaultPanel != null)
            {
                CompactNotificationPanel.Visibility = Visibility.Collapsed;

                if (_mediaService.CurrentSnapshot.HasActiveSession)
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
            AppLogger.Warning($"DismissCurrentNotification_Click failed: {ex.Message}");
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
            AppLogger.Warning($"ClearAllNotifications_Click failed: {ex.Message}");
        }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _clockTimer.Stop();
        _trayManager.Dispose();
        _mediaService.Dispose();
        _notificationService.Dispose();

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
