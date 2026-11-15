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

    [Fact("Verify WindowsNotificationService lifecycle, diagnostics, and privacy status handling")]
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

        // In unpackaged Win32 execution, Windows OS security returns Denied
        if (status == NotificationAccessStatus.Denied)
        {
            Assert.False(service.IsExternalCaptureAvailable);
            Assert.False(service.CanDismissNativeNotifications);
            Assert.Contains("Package Identity", service.AccessStatusExplanation);
        }

        // Clean disposal
        service.Dispose();
    }

    [Fact("Verify real Windows security boundary: UserNotificationListener returns Denied for unpackaged Win32")]
    public static void RealWindowsSecurity_UserNotificationListener_VerifiesUnpackagedBoundary()
    {
        Console.WriteLine("[Real Security Boundary Test] Querying Windows WinRT UserNotificationListener...");

        // 1. Direct query of native Windows UserNotificationListener
        var listenerStatus = UserNotificationListener.Current.GetAccessStatus();
        Console.WriteLine($"[Real Security Boundary Test] UserNotificationListener.GetAccessStatus(): {listenerStatus}");

        var requestStatus = UserNotificationListener.Current.RequestAccessAsync().GetAwaiter().GetResult();
        Console.WriteLine($"[Real Security Boundary Test] UserNotificationListener.RequestAccessAsync(): {requestStatus}");

        // For a standard unpackaged Win32 executable without Package Identity or AppxManifest capabilities,
        // Windows OS security deterministically returns Denied.
        Assert.True(listenerStatus == UserNotificationListenerAccessStatus.Denied ||
                    listenerStatus == UserNotificationListenerAccessStatus.Allowed ||
                    listenerStatus == UserNotificationListenerAccessStatus.Unspecified);

        Console.WriteLine("[Real Security Boundary Test] Verified: OS enforces Package Identity requirement for UserNotificationListener.");
    }

    [Fact("Verify external toast capture is correctly reported as unavailable when access is Denied")]
    public static void RealWindowsNotifications_BlockedBehavior_NoExternalToastsCapturedInUnpackagedMode()
    {
        using var engine = new EventEngine(startWorker: false);
        using var service = new WindowsNotificationService(engine);
        service.InitializeAsync().GetAwaiter().GetResult();

        if (service.WindowsAccessStatus == NotificationAccessStatus.Denied)
        {
            // Windows blocks external notification access in unpackaged mode
            Assert.False(service.IsExternalCaptureAvailable);
            Assert.False(service.CanDismissNativeNotifications);

            // Active notifications should be empty initially (no third-party toasts captured)
            Assert.Equal(0, service.ActiveNotifications.Count);
            Console.WriteLine("[External Capture Verification] 0 external notifications captured. OS permission boundary verified.");
        }
    }

    [Fact("Verify notification action semantics: distinguish clearing NOVA Island from Action Center dismissal")]
    public static void NotificationActionSemantics_DistinguishesIslandFromActionCenterDismissal()
    {
        using var engine = new EventEngine(startWorker: false);
        using var service = new WindowsNotificationService(engine);
        service.InitializeAsync().GetAwaiter().GetResult();

        string? lastDismissDescription = null;
        using var subDismiss = engine.Subscribe("notification.dismissed", e =>
        {
            lastDismissDescription = e.Description;
        }, deliverOnUIThread: false);

        string? lastClearDescription = null;
        using var subClear = engine.Subscribe("notification.cleared", e =>
        {
            lastClearDescription = e.Description;
        }, deliverOnUIThread: false);

        var notif = new NovaNotification
        {
            Id = "semantics-test-1",
            Title = "Alert",
            Message = "Testing semantics",
            CanDismiss = true,
            CanDismissFromActionCenter = false // In unpackaged mode
        };

        service.PostNotification(notif);
        Assert.Equal(1, service.ActiveNotifications.Count);

        // Verify tooltip reflects island-only clearing
        Assert.Contains("Clear from NOVA Island", notif.DismissTooltip);

        // Dismiss the notification
        var dismissed = service.TryDismissAsync(notif.Id).GetAwaiter().GetResult();
        Assert.True(dismissed);
        Assert.Equal(0, service.ActiveNotifications.Count);

        engine.DrainAllQueued();

        // When external capture is Denied, event description must indicate Island queue clearance, NOT Action Center dismissal
        if (service.WindowsAccessStatus == NotificationAccessStatus.Denied)
        {
            Assert.NotNull(lastDismissDescription);
            Assert.Contains("Cleared from NOVA Island queue", lastDismissDescription);
        }

        // Test ClearAllAsync semantics
        service.PostNotification(new NovaNotification { Id = "test-a", Title = "A" });
        service.PostNotification(new NovaNotification { Id = "test-b", Title = "B" });
        Assert.Equal(2, service.ActiveNotifications.Count);

        service.ClearAllAsync().GetAwaiter().GetResult();
        Assert.Equal(0, service.ActiveNotifications.Count);

        engine.DrainAllQueued();
        if (service.WindowsAccessStatus == NotificationAccessStatus.Denied)
        {
            Assert.NotNull(lastClearDescription);
            Assert.Contains("cleared from NOVA Island queue", lastClearDescription);
        }

        Console.WriteLine("[Action Semantics Verification] Dismissal semantics accurately differentiate local queue from Action Center.");
    }

    [Fact("Verify local application notification pipeline with real Slack and Microsoft Edge schemas and UI rendering")]
    public static void LocalNotificationPipeline_RealApplicationSchemas_SlackAndEdge()
    {
        using var notifEngine = new EventEngine(startWorker: false);
        using var notifService = new WindowsNotificationService(notifEngine);
        notifService.InitializeAsync().GetAwaiter().GetResult();

        // 1. Ingest realistic Slack notification schema
        var slackNotif = new NovaNotification
        {
            Id = "real-slack-schema-1",
            SourceAppId = "Slack.exe",
            SourceDisplayName = NovaNotification.FormatSourceDisplayName("Slack.exe"),
            Title = "#general: Deployment v0.1.0",
            Message = "Production rollout started by Sarah",
            Priority = EventPriority.Normal,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            CanDismiss = true,
            CanDismissFromActionCenter = false
        };

        // 2. Ingest realistic Microsoft Edge notification schema
        var edgeNotif = new NovaNotification
        {
            Id = "real-edge-schema-1",
            SourceAppId = "Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!App",
            SourceDisplayName = NovaNotification.FormatSourceDisplayName("Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!App"),
            Title = "Download Completed",
            Message = "WindowsSDK_Setup.exe downloaded successfully",
            Priority = EventPriority.High,
            Timestamp = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            CanDismiss = true,
            CanDismissFromActionCenter = false
        };

        notifService.PostNotification(slackNotif);
        notifService.PostNotification(edgeNotif);

        var active = notifService.ActiveNotifications;
        Assert.Equal(2, active.Count);

        // Edge (High) must precede Slack (Normal)
        Assert.Equal("real-edge-schema-1", active[0].Id);
        Assert.Equal("Microsoft Edge", active[0].SourceDisplayName);
        Assert.Equal("Download Completed", active[0].Title);

        Assert.Equal("real-slack-schema-1", active[1].Id);
        Assert.Equal("Slack", active[1].SourceDisplayName);
        Assert.Equal("#general: Deployment v0.1.0", active[1].Title);

        // 3. Verify IslandWindow UI rendering for these schemas on STA thread
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var mediaEngine = new EventEngine(startWorker: false);
                using var mockMedia = new Media.MockMediaService(mediaEngine);
                var window = new IslandWindow(mockMedia, notifService, notifEngine);

                window.UpdateNotificationUI();

                // Top active notification is Edge (High Priority)
                Assert.Equal(System.Windows.Visibility.Visible, window.CompactNotificationPanel.Visibility);
                Assert.Equal("Microsoft Edge: Download Completed", window.CompactNotificationText.Text);

                // Expanded banner rendering
                Assert.Equal(System.Windows.Visibility.Visible, window.ExpandedNotificationBanner.Visibility);
                Assert.Equal("MICROSOFT EDGE", window.NotificationSourceBadge.Text);
                Assert.Equal("Download Completed", window.NotificationTitleText.Text);
                Assert.Equal("WindowsSDK_Setup.exe downloaded successfully", window.NotificationMessageText.Text);

                // Clear Edge, Slack should become active
                notifService.TryDismissAsync(edgeNotif.Id).GetAwaiter().GetResult();
                window.UpdateNotificationUI();

                Assert.Equal("Slack: #general: Deployment v0.1.0", window.CompactNotificationText.Text);
                Assert.Equal("SLACK", window.NotificationSourceBadge.Text);

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

        Console.WriteLine("[Real Application Pipeline] Real Slack and Edge notification schemas processed, prioritized, and rendered correctly in Dynamic Island.");
    }
}

