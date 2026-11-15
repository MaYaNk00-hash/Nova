using System.Windows;
using System.Windows.Media.Animation;
using Nova.Core;
using Nova.UI;

namespace Nova.Tests;

public class IslandWindowTests
{
    [Fact("Verify IslandDimensions constants")]
    public void IslandDimensions_ValuesAreSensible()
    {
        Assert.True(IslandDimensions.CompactWidth > 150 && IslandDimensions.CompactWidth < 300, "CompactWidth should be reasonable pill width.");
        Assert.True(IslandDimensions.CompactHeight > 30 && IslandDimensions.CompactHeight < 60, "CompactHeight should be sleek pill height.");
        Assert.True(IslandDimensions.ExpandedWidth > IslandDimensions.CompactWidth, "Expanded width must be larger than compact width.");
        Assert.True(IslandDimensions.ExpandedHeight > IslandDimensions.CompactHeight, "Expanded height must be larger than compact height.");
        Assert.Equal(0.0, IslandDimensions.TopMargin);
    }

    [Fact("Verify ScreenHelper top-center calculation")]
    public void ScreenHelper_CalculateTopCenter_CalculatesCorrectCoordinates()
    {
        var workArea = new RectDip(0, 0, 1920, 1080);
        double windowWidth = 460;
        double marginTop = 12;

        var point = ScreenHelper.CalculateTopCenter(workArea, windowWidth, marginTop);

        Assert.Equal(730.0, point.X, "Window X should be centered (1920 - 460) / 2 = 730.");
        Assert.Equal(12.0, point.Y, "Window Y should match marginTop.");
    }

    [Fact("Verify ScreenHelper clamps coordinates within work area")]
    public void ScreenHelper_ClampToWorkArea_PreventsOffscreenPlacement()
    {
        var workArea = new RectDip(0, 0, 1920, 1080);
        double width = 460;
        double height = 250;

        // Off the left edge
        var clamped1 = ScreenHelper.ClampToWorkArea(workArea, -100, 50, width, height);
        Assert.Equal(0.0, clamped1.X, "Left should be clamped to 0.");
        Assert.Equal(50.0, clamped1.Y);

        // Off the right edge
        var clamped2 = ScreenHelper.ClampToWorkArea(workArea, 2000, 50, width, height);
        Assert.Equal(1460.0, clamped2.X, "Right should be clamped to workArea.Right - width (1920 - 460 = 1460).");

        // Off the bottom edge
        var clamped3 = ScreenHelper.ClampToWorkArea(workArea, 500, 1200, width, height);
        Assert.Equal(500.0, clamped3.X);
        Assert.Equal(830.0, clamped3.Y, "Bottom should be clamped to workArea.Bottom - height (1080 - 250 = 830).");
    }

    [Fact("Verify IslandWindow instantiation and default properties")]
    public void IslandWindow_InstantiatesWithDefaultProperties()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new IslandWindow();
                Assert.NotNull(window);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.True(window.AllowsTransparency, "AllowsTransparency must be true for floating pill.");
                Assert.False(window.ShowInTaskbar, "ShowInTaskbar must be false for companion island.");
                Assert.True(window.Topmost, "Window should be Topmost by default.");
                Assert.Equal(IslandState.Compact, window.CurrentState);
                Assert.False(window.IsManuallyPositioned, "Should not be manually positioned initially.");
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

    [Fact("Verify IslandWindow state transitions")]
    public void IslandWindow_StateTransitions_UpdateStateCorrectly()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new IslandWindow();

                Assert.Equal(IslandState.Compact, window.CurrentState);

                window.TransitionTo(IslandState.Expanded);
                Assert.Equal(IslandState.Expanded, window.CurrentState);

                window.TransitionTo(IslandState.Compact);
                Assert.Equal(IslandState.Compact, window.CurrentState);

                window.ToggleAlwaysOnTop();
                Assert.False(window.Topmost, "Always on top should toggle to false.");
                window.ToggleAlwaysOnTop();
                Assert.True(window.Topmost, "Always on top should toggle back to true.");

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

    [Fact("Verify CornerRadiusAnimation interpolates values")]
    public void CornerRadiusAnimation_InterpolatesValues()
    {
        var anim = new CornerRadiusAnimation(new CornerRadius(10), new CornerRadius(30), new Duration(TimeSpan.FromSeconds(1)));
        Assert.Equal(typeof(CornerRadius), anim.TargetPropertyType);
        Assert.Equal(new CornerRadius(10), anim.From);
        Assert.Equal(new CornerRadius(30), anim.To);
    }

    [Fact("Verify IslandWindow Media UI updates dynamically when media session states change")]
    public void IslandWindow_MediaUI_UpdatesOnMediaSessionState()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var engine = new Events.EventEngine(startWorker: false);
                using var mockMedia = new Media.MockMediaService(engine);
                var window = new IslandWindow(mockMedia, engine);

                // Initial state without media
                Assert.Equal(Visibility.Visible, window.CompactDefaultPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.CompactMediaPanel.Visibility);
                Assert.Equal("No Media Playing", window.MediaTitleText.Text);
                Assert.False(window.MediaPlayPauseBtn.IsEnabled);
                Assert.False(window.MediaNextBtn.IsEnabled);

                // Active playing session
                var activeSnapshot = new Media.MediaSessionSnapshot
                {
                    SourceAppId = "spotify.exe",
                    SourceDisplayName = "Spotify",
                    PlaybackStatus = Media.MediaPlaybackStatus.Playing,
                    HasActiveSession = true,
                    TrackInfo = new Media.MediaTrackInfo
                    {
                        Title = "Resonance",
                        Artist = "HOME",
                        AlbumTitle = "Odyssey"
                    },
                    Capabilities = Media.MediaCapabilities.All
                };

                window.UpdateMediaUI(activeSnapshot);

                Assert.Equal(Visibility.Collapsed, window.CompactDefaultPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.CompactMediaPanel.Visibility);
                Assert.Equal("Resonance • HOME", window.CompactMediaText.Text);
                Assert.Equal("Resonance", window.MediaTitleText.Text);
                Assert.True(window.MediaPlayPauseBtn.IsEnabled);
                Assert.True(window.MediaNextBtn.IsEnabled);
                Assert.True(window.MediaPrevBtn.IsEnabled);
                Assert.Equal("⏸", window.MediaPlayPauseBtn.Content);

                // Paused state
                var pausedSnapshot = activeSnapshot with { PlaybackStatus = Media.MediaPlaybackStatus.Paused };
                window.UpdateMediaUI(pausedSnapshot);
                Assert.Equal("▶", window.MediaPlayPauseBtn.Content);
                Assert.Equal("▶", window.CompactPlayPauseBtn.Content);

                // Disconnected session
                window.UpdateMediaUI(Media.MediaSessionSnapshot.Empty);
                Assert.Equal(Visibility.Visible, window.CompactDefaultPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.CompactMediaPanel.Visibility);
                Assert.Equal("No Media Playing", window.MediaTitleText.Text);
                Assert.False(window.MediaPlayPauseBtn.IsEnabled);

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

    [Fact("Verify IslandWindow Notification UI updates dynamically when notifications arrive and dismiss")]
    public void IslandWindow_NotificationUI_UpdatesOnNotificationEvents()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var engine = new Events.EventEngine(startWorker: false);
                using var mockMedia = new Media.MockMediaService(engine);
                using var mockNotif = new Notifications.MockNotificationService(engine);
                var window = new IslandWindow(mockMedia, mockNotif, engine);

                // Initial state without notifications:
                Assert.Equal(Visibility.Collapsed, window.CompactNotificationPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.CompactDefaultPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.ExpandedNotificationBanner.Visibility);

                // 1. Post notification
                var testNotif = new Notifications.NovaNotification
                {
                    Id = "ui-test-1",
                    SourceDisplayName = "Slack",
                    Title = "Meeting Reminder",
                    Message = "Design sync in 5 minutes",
                    Priority = Events.EventPriority.High
                };
                mockNotif.PostNotification(testNotif);
                window.UpdateNotificationUI();

                // Compact view shows notification banner
                Assert.Equal(Visibility.Visible, window.CompactNotificationPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.CompactDefaultPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.CompactMediaPanel.Visibility);
                Assert.Equal("Slack: Meeting Reminder", window.CompactNotificationText.Text);

                // Expanded view shows amber notification banner
                Assert.Equal(Visibility.Visible, window.ExpandedNotificationBanner.Visibility);
                Assert.Equal("SLACK", window.NotificationSourceBadge.Text);
                Assert.Equal("Meeting Reminder", window.NotificationTitleText.Text);
                Assert.Equal("Design sync in 5 minutes", window.NotificationMessageText.Text);

                // 2. Dismiss notification
                mockNotif.TryDismissAsync(testNotif.Id).GetAwaiter().GetResult();
                window.UpdateNotificationUI();

                // Reverts to default state
                Assert.Equal(Visibility.Collapsed, window.CompactNotificationPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.CompactDefaultPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.ExpandedNotificationBanner.Visibility);

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
}

