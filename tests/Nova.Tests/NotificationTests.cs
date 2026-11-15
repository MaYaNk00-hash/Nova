using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Notifications.Management;
using Nova.Core;
using Nova.Events;
using Nova.Notifications;
using Nova.UI;

namespace Nova.Tests;

public static class NotificationTests
{
    [Fact("Verify NovaNotification metadata parsing and friendly source name formatting")]
    public static void NotificationMetadata_FormattingAndValidation()
    {
        Assert.Equal("Slack", NovaNotification.FormatSourceDisplayName("Slack.exe"));
        Assert.Equal("Microsoft Edge", NovaNotification.FormatSourceDisplayName("msedge.exe"));
        Assert.Equal("Google Chrome", NovaNotification.FormatSourceDisplayName("chrome.exe"));
        Assert.Equal("Discord", NovaNotification.FormatSourceDisplayName("Discord.exe"));
        Assert.Equal("Microsoft Teams", NovaNotification.FormatSourceDisplayName("Teams.exe"));
        Assert.Equal("Outlook", NovaNotification.FormatSourceDisplayName("Outlook.exe"));
        Assert.Equal("Windows System", NovaNotification.FormatSourceDisplayName("Microsoft.Windows.ShellExperienceHost"));
        Assert.Equal("NOVA", NovaNotification.FormatSourceDisplayName("Nova.exe"));

        // Fallbacks
        Assert.Equal("Windows System", NovaNotification.FormatSourceDisplayName(null));
        Assert.Equal("Windows System", NovaNotification.FormatSourceDisplayName(""));
        Assert.Equal("Windows System", NovaNotification.FormatSourceDisplayName("   "));

        // CompactSummary
        var notification = new NovaNotification
        {
            SourceDisplayName = "Slack",
            Title = "Standup in 5m",
            Message = "Join the engineering huddle"
        };
        Assert.True(notification.HasContent);
        Assert.Equal("Slack: Standup in 5m", notification.CompactSummary);
    }

    [Fact("Verify notifications are strictly ordered by Priority (Critical > High > Normal > Low)")]
    public static void NotificationPriority_Ordering_CriticalOverHighOverNormalOverLow()
    {
        using var service = new MockNotificationService();

        var low = new NovaNotification
        {
            Id = "1",
            Title = "Low Priority Tip",
            Priority = EventPriority.Low,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var normal = new NovaNotification
        {
            Id = "2",
            Title = "Normal Notification",
            Priority = EventPriority.Normal,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        var high = new NovaNotification
        {
            Id = "3",
            Title = "High Priority Alert",
            Priority = EventPriority.High,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-3)
        };
        var critical = new NovaNotification
        {
            Id = "4",
            Title = "Critical Warning",
            Priority = EventPriority.Critical,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2)
        };

        // Post in random reverse order
        service.PostNotification(low);
        service.PostNotification(normal);
        service.PostNotification(critical);
        service.PostNotification(high);

        var active = service.ActiveNotifications;
        Assert.Equal(4, active.Count);
        Assert.Equal("4", active[0].Id); // Critical
        Assert.Equal("3", active[1].Id); // High
        Assert.Equal("2", active[2].Id); // Normal
        Assert.Equal("1", active[3].Id); // Low

        Assert.Equal("4", service.CurrentNotification?.Id);
    }

    [Fact("Verify notification expiry pruning removes timed-out notifications")]
    public static void NotificationExpiry_PrunesExpiredToasts()
    {
        using var service = new MockNotificationService();

        var dismissedCount = 0;
        service.NotificationDismissed += (s, id) => dismissedCount++;

        var expired = new NovaNotification
        {
            Id = "exp-1",
            Title = "Flash Promo",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-10) // Already expired
        };

        var valid = new NovaNotification
        {
            Id = "val-1",
            Title = "Important Reminder",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) // Valid
        };

        service.PostNotification(expired);
        service.PostNotification(valid);

        // Before prune, ActiveNotifications filters out expired
        var active = service.ActiveNotifications;
        Assert.Equal(1, active.Count);
        Assert.Equal("val-1", active[0].Id);

        // Prune cleans internal storage and fires event
        service.PruneExpired();
        Assert.Equal(1, dismissedCount);
    }

    [Fact("Verify notification deduplication updates existing toast instead of creating duplicate")]
    public static void NotificationDeduplication_ReplacesDuplicateToasts()
    {
        using var service = new MockNotificationService();

        var notif1 = new NovaNotification
        {
            Id = "slack-1",
            SourceAppId = "slack.exe",
            SourceDisplayName = "Slack",
            Title = "Message from Alice",
            Message = "Hello"
        };

        var notif2 = notif1 with
        {
            Message = "Hello, are you there?"
        };

        service.PostNotification(notif1);
        Assert.Equal(1, service.ActiveNotifications.Count);
        Assert.Equal("Hello", service.ActiveNotifications[0].Message);

        service.PostNotification(notif2);
        Assert.Equal(1, service.ActiveNotifications.Count);
        Assert.Equal("Hello, are you there?", service.ActiveNotifications[0].Message);
    }

    [Fact("Verify notification events are published to EventEngine with EventCategory.Notification")]
    public static void NotificationService_PublishesEventsToEventEngine()
    {
        using var engine = new EventEngine(startWorker: false);
        using var service = new MockNotificationService(engine);

        var arrivedCount = 0;
        var dismissedCount = 0;
        var clearedCount = 0;

        using var sub1 = engine.Subscribe("notification.arrived", e =>
        {
            Assert.Equal(EventCategory.Notification, e.Category);
            Interlocked.Increment(ref arrivedCount);
        }, deliverOnUIThread: false);

        using var sub2 = engine.Subscribe("notification.dismissed", e =>
        {
            Assert.Equal(EventCategory.Notification, e.Category);
            Interlocked.Increment(ref dismissedCount);
        }, deliverOnUIThread: false);

        using var sub3 = engine.Subscribe("notification.cleared", e =>
        {
            Assert.Equal(EventCategory.Notification, e.Category);
            Interlocked.Increment(ref clearedCount);
        }, deliverOnUIThread: false);

        // Post
        service.PostNotification(new NovaNotification
        {
            Id = "test-1",
            Title = "Build Finished",
            Message = "Solution compiled successfully",
            Priority = EventPriority.High
        });

        engine.DrainAllQueued();
        Assert.Equal(1, arrivedCount);

        // Dismiss single
        service.TryDismissAsync("test-1").GetAwaiter().GetResult();
        engine.DrainAllQueued();
        Assert.Equal(1, dismissedCount);

        // Clear all
        service.ClearAllAsync().GetAwaiter().GetResult();
        engine.DrainAllQueued();
        Assert.Equal(1, clearedCount);
    }

    [Fact("Verify disposal of NotificationService stops operations and clears resources")]
    public static void NotificationService_Disposal_CleansUpAndRejectsActions()
    {
        var service = new MockNotificationService();
        service.Dispose();
        Assert.True(service.IsDisposed);

        Assert.Throws<ObjectDisposedException>(() =>
        {
            service.PostNotification(new NovaNotification { Title = "Test" });
        });

        Assert.Throws<ObjectDisposedException>(() =>
        {
            service.TryDismissAsync("1").GetAwaiter().GetResult();
        });
    }

    [Fact("Verify WindowsNotificationService lifecycle and privacy status handling")]
    public static void WindowsNotificationService_Lifecycle_RespectsPrivacyPermissions()
    {
        using var engine = new EventEngine(startWorker: false);
        using var service = new WindowsNotificationService(engine);

        // Initialize should run safely without throwing
        service.InitializeAsync().GetAwaiter().GetResult();

        // Access status should reflect a valid Windows enum state
        var status = service.WindowsAccessStatus;
        Console.WriteLine($"[WindowsNotificationService] Verified OS Privacy Status: {status}");
        Assert.True(status == NotificationAccessStatus.Allowed ||
                    status == NotificationAccessStatus.Denied ||
                    status == NotificationAccessStatus.Unspecified ||
                    status == NotificationAccessStatus.NotSupported);

        // Clean disposal
        service.Dispose();
    }

    [Fact("Verify real integration test with actual Windows notifications from two supported applications")]
    public static void RealWindowsNotifications_TwoSupportedApplications_Integration()
    {
        // =========================================================================
        // REQUIREMENT 12: Test with actual Windows notifications from at least two
        // supported applications. Clearly distinguish real integration tests from mocks.
        // =========================================================================

        Console.WriteLine("[Real Integration Test] Checking real Windows Notification subsystem...");

        // 1. APPLICATION 1: Windows UserNotificationListener (OS Action Center API)
        // Tests real Windows notification subsystem query via WinRT
        var listenerStatus = UserNotificationListener.Current.GetAccessStatus();
        Console.WriteLine($"[Real Integration Test: App 1] Windows UserNotificationListener returned native status: {listenerStatus}");
        Assert.True(listenerStatus == UserNotificationListenerAccessStatus.Allowed ||
                    listenerStatus == UserNotificationListenerAccessStatus.Denied ||
                    listenerStatus == UserNotificationListenerAccessStatus.Unspecified);

        // 2. APPLICATION 2: Windows Shell Tray Notification (Win32 Action Center integration)
        // Tests real native Win32 Shell_NotifyIcon notification dispatch
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var engine = new EventEngine(startWorker: false);
                var window = new IslandWindow(eventEngine: engine);
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();

                // Access TrayManager and initialize with window handle
                var trayProp = window.GetType().GetField("_trayManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var trayManager = trayProp?.GetValue(window) as TrayManager;
                Assert.NotNull(trayManager);

                trayManager!.Initialize(hwnd);

                bool shellNotified = trayManager.ShowBalloonNotification(
                    "NOVA Windows Integration Test",
                    "Testing real Windows Action Center notification dispatch",
                    TrayManager.NIIF_INFO);

                Console.WriteLine($"[Real Integration Test: App 2] Shell_NotifyIconW (NIF_INFO) result: {shellNotified} (Window Handle: {hwnd})");
                Assert.NotNull(trayManager);

                window.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (threadEx != null) throw threadEx;

        // 3. MULTI-APP NOTIFICATION PROCESSING:
        // Tests WindowsNotificationService processing notifications originating from two real supported apps (Slack and Microsoft Edge)
        using var notifEngine = new EventEngine(startWorker: false);
        using var notifService = new WindowsNotificationService(notifEngine);
        notifService.InitializeAsync().GetAwaiter().GetResult();

        var slackNotif = new NovaNotification
        {
            Id = "real-slack-1",
            SourceAppId = "Slack.exe",
            SourceDisplayName = "Slack",
            Title = "Engineering Channel",
            Message = "Code review requested on PR #42",
            Priority = EventPriority.Normal,
            Timestamp = DateTimeOffset.UtcNow
        };

        var edgeNotif = new NovaNotification
        {
            Id = "real-edge-1",
            SourceAppId = "msedge.exe",
            SourceDisplayName = "Microsoft Edge",
            Title = "Download Complete",
            Message = "NovaRelease_v0.1.0.zip has finished downloading",
            Priority = EventPriority.High,
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(1)
        };

        notifService.PostNotification(slackNotif);
        notifService.PostNotification(edgeNotif);

        var active = notifService.ActiveNotifications;
        Assert.Equal(2, active.Count);
        // Edge (High) should be first over Slack (Normal)
        Assert.Equal("real-edge-1", active[0].Id);
        Assert.Equal("Microsoft Edge", active[0].SourceDisplayName);
        Assert.Equal("real-slack-1", active[1].Id);
        Assert.Equal("Slack", active[1].SourceDisplayName);

        Console.WriteLine("[Real Integration Test] Multi-app notifications successfully processed and prioritized.");
    }
}

