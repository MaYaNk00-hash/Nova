using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Nova.Core;
using Nova.Events;

namespace Nova.Notifications;

/// <summary>
/// Production Windows notification service integrating native Windows UserNotificationListener
/// with NOVA Dynamic Island and companion reaction system.
/// Respects Windows privacy permissions and handles access restrictions gracefully.
/// </summary>
public sealed class WindowsNotificationService : INotificationService
{
    private readonly EventEngine? _eventEngine;
    private readonly object _lock = new();
    private readonly List<NovaNotification> _activeNotifications = new();

    private UserNotificationListener? _listener;
    private NotificationAccessStatus _accessStatus = NotificationAccessStatus.Unspecified;
    private bool _isDisposed;
    private bool _isInitialized;

    public NotificationAccessStatus WindowsAccessStatus
    {
        get
        {
            lock (_lock) return _accessStatus;
        }
        private set
        {
            lock (_lock) _accessStatus = value;
        }
    }

    /// <summary>
    /// Indicates whether external notification capture from third-party Windows apps is currently available.
    /// In classic unpackaged Win32 execution, this is false because Windows restricts UserNotificationListener to packaged applications.
    /// </summary>
    public bool IsExternalCaptureAvailable => WindowsAccessStatus == NotificationAccessStatus.Allowed;

    /// <summary>
    /// Indicates whether dismissing a notification will clear it from the native Windows Action Center.
    /// </summary>
    public bool CanDismissNativeNotifications => WindowsAccessStatus == NotificationAccessStatus.Allowed;

    /// <summary>
    /// Human-readable explanation of the current Windows notification access status and requirements.
    /// </summary>
    public string AccessStatusExplanation => WindowsAccessStatus switch
    {
        NotificationAccessStatus.Allowed => "Windows notification capture is active. Toasts from supported applications are being monitored.",
        NotificationAccessStatus.Denied => "Windows notification capture is Denied by the OS. Unpackaged Win32 applications lack Package Identity and the restricted 'userNotificationListener' capability. Action Center toasts cannot be intercepted without a packaged MSIX installation. Local companion notifications remain fully functional.",
        NotificationAccessStatus.NotSupported => "UserNotificationListener API is not supported on this Windows version.",
        _ => "Windows notification access status is unspecified."
    };

    public event EventHandler<NovaNotification>? NotificationArrived;
    public event EventHandler<string>? NotificationDismissed;

    public WindowsNotificationService(EventEngine? eventEngine = null)
    {
        _eventEngine = eventEngine;
    }

    public IReadOnlyList<NovaNotification> ActiveNotifications
    {
        get
        {
            lock (_lock)
            {
                return _activeNotifications
                    .Where(n => !n.IsExpired())
                    .OrderByDescending(n => n.Priority)
                    .ThenByDescending(n => n.Timestamp)
                    .ToList();
            }
        }
    }

    public NovaNotification? CurrentNotification => ActiveNotifications.FirstOrDefault();

    public async Task InitializeAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WindowsNotificationService));

        lock (_lock)
        {
            if (_isInitialized) return;
            _isInitialized = true;
        }

        try
        {
            AppLogger.Info("Initializing WindowsNotificationService via UserNotificationListener...");

            try
            {
                _listener = UserNotificationListener.Current;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"UserNotificationListener not available on this platform: {ex.Message}");
                WindowsAccessStatus = NotificationAccessStatus.NotSupported;
                return;
            }

            if (_listener == null)
            {
                WindowsAccessStatus = NotificationAccessStatus.NotSupported;
                return;
            }

            var nativeStatus = _listener.GetAccessStatus();
            WindowsAccessStatus = MapAccessStatus(nativeStatus);

            AppLogger.Info($"Initial Windows notification access status: {WindowsAccessStatus}");

            if (WindowsAccessStatus == NotificationAccessStatus.Allowed)
            {
                HookListenerEvents();
                await SyncActiveNotificationsAsync();
            }
            else
            {
                AppLogger.Info($"Windows notification access status: {WindowsAccessStatus}. " +
                               "Classic unpackaged Win32 applications lack Package Identity and cannot declare 'uap3:userNotificationListener'. " +
                               "External Windows Action Center toast interception is unavailable. Local companion notifications remain active.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error initializing WindowsNotificationService: {ex.Message}");
            WindowsAccessStatus = NotificationAccessStatus.NotSupported;
        }
    }

    public async Task<bool> RequestWindowsAccessAsync()
    {
        if (_isDisposed || _listener == null) return false;

        try
        {
            AppLogger.Info("Requesting Windows UserNotificationListener access...");
            var nativeStatus = await _listener.RequestAccessAsync();
            WindowsAccessStatus = MapAccessStatus(nativeStatus);
            AppLogger.Info($"Updated Windows notification access status: {WindowsAccessStatus}");

            if (WindowsAccessStatus == NotificationAccessStatus.Allowed)
            {
                HookListenerEvents();
                await SyncActiveNotificationsAsync();
                return true;
            }
            else
            {
                AppLogger.Info($"RequestAccessAsync returned {WindowsAccessStatus}. Windows security blocks notification listener requests from unpackaged applications.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"RequestAccessAsync failed: {ex.Message}");
        }

        return false;
    }

    private void HookListenerEvents()
    {
        if (_listener == null) return;

        try
        {
            _listener.NotificationChanged += OnListenerNotificationChanged;
            AppLogger.Info("Subscribed to UserNotificationListener.NotificationChanged event.");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to hook NotificationChanged event: {ex.Message}");
        }
    }

    private async void OnListenerNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (_isDisposed) return;

        try
        {
            if (args.ChangeKind == UserNotificationChangedKind.Added)
            {
                var userNotification = sender.GetNotification(args.UserNotificationId);
                if (userNotification != null)
                {
                    var novaNotification = ConvertNativeNotification(userNotification);
                    if (novaNotification != null)
                    {
                        PostNotification(novaNotification);
                    }
                }
            }
            else if (args.ChangeKind == UserNotificationChangedKind.Removed)
            {
                await RemoveByNativeIdAsync(args.UserNotificationId);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error handling NotificationChanged: {ex.Message}");
        }
    }

    private async Task SyncActiveNotificationsAsync()
    {
        if (_listener == null || WindowsAccessStatus != NotificationAccessStatus.Allowed) return;

        try
        {
            var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            if (notifications != null)
            {
                foreach (var un in notifications)
                {
                    var converted = ConvertNativeNotification(un);
                    if (converted != null)
                    {
                        PostNotification(converted);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to synchronize native notifications: {ex.Message}");
        }
    }

    private static NovaNotification? ConvertNativeNotification(UserNotification native)
    {
        try
        {
            var rawAppId = native.AppInfo?.AppUserModelId ?? native.AppInfo?.PackageFamilyName ?? "Windows";
            var displayName = native.AppInfo?.DisplayInfo?.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = NovaNotification.FormatSourceDisplayName(rawAppId);
            }

            var title = string.Empty;
            var message = string.Empty;

            var binding = native.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding != null)
            {
                var textElements = binding.GetTextElements()?.ToList();
                if (textElements != null && textElements.Count > 0)
                {
                    title = textElements[0]?.Text ?? string.Empty;
                    if (textElements.Count > 1)
                    {
                        message = string.Join(Environment.NewLine, textElements.Skip(1).Select(t => t?.Text).Where(t => !string.IsNullOrEmpty(t)));
                    }
                }
            }

            // Expiry: Toasts default to 20 seconds of island visibility unless high priority
            var ttl = TimeSpan.FromSeconds(20);
            var canDismissNative = native.Id != 0;

            return new NovaNotification
            {
                NativeId = native.Id,
                SourceType = NotificationSourceType.WindowsToast,
                SourceAppId = rawAppId,
                SourceDisplayName = displayName,
                Title = title,
                Message = message,
                Timestamp = native.CreationTime,
                Priority = EventPriority.Normal,
                ExpiresAt = DateTimeOffset.UtcNow.Add(ttl),
                CanDismiss = true,
                CanDismissFromActionCenter = canDismissNative
            };
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to parse native UserNotification: {ex.Message}");
            return null;
        }
    }

    public void PostNotification(NovaNotification notification)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WindowsNotificationService));

        lock (_lock)
        {
            // Deduplicate: replace existing with same ID or identical title/source
            _activeNotifications.RemoveAll(n => n.Id == notification.Id ||
                (n.SourceAppId == notification.SourceAppId && n.Title == notification.Title && !n.IsExpired()));
            _activeNotifications.Add(notification);
        }

        NotificationArrived?.Invoke(this, notification);

        if (_eventEngine != null)
        {
            _eventEngine.Publish(NovaEvent.CreateNotificationEvent(
                eventType: "notification.arrived",
                title: notification.Title,
                description: notification.Message,
                payload: notification,
                priority: notification.Priority,
                timeToLive: notification.ExpiresAt.HasValue ? notification.ExpiresAt.Value - DateTimeOffset.UtcNow : null,
                deduplicationKey: $"notification.{notification.SourceAppId}.{notification.Title}"));
        }
    }

    public Task<bool> TryDismissAsync(string notificationId)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WindowsNotificationService));

        NovaNotification? target;
        lock (_lock)
        {
            target = _activeNotifications.FirstOrDefault(n => n.Id == notificationId);
            if (target != null)
            {
                _activeNotifications.Remove(target);
            }
        }

        if (target == null) return Task.FromResult(false);

        bool nativeDismissed = false;
        // If backed by native UserNotificationListener and supported, dismiss from Windows Action Center
        if (target.NativeId.HasValue && _listener != null && WindowsAccessStatus == NotificationAccessStatus.Allowed)
        {
            try
            {
                _listener.RemoveNotification(target.NativeId.Value);
                nativeDismissed = true;
                AppLogger.Info($"Notification {target.Id} (NativeId: {target.NativeId}) dismissed from Windows Action Center.");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Could not remove native Action Center notification {target.NativeId}: {ex.Message}");
            }
        }
        else
        {
            AppLogger.Info($"Notification '{target.Title}' dismissed from NOVA Island queue (Native Action Center dismissal skipped: WindowsAccessStatus={WindowsAccessStatus}).");
        }

        NotificationDismissed?.Invoke(this, notificationId);

        if (_eventEngine != null)
        {
            _eventEngine.Publish(NovaEvent.CreateNotificationEvent(
                eventType: "notification.dismissed",
                title: nativeDismissed ? "Action Center Notification Dismissed" : "Island Notification Cleared",
                description: nativeDismissed
                    ? $"Dismissed from NOVA Island and Windows Action Center: {notificationId}"
                    : $"Cleared from NOVA Island queue: {notificationId}",
                payload: notificationId,
                priority: EventPriority.Low));
        }

        return Task.FromResult(true);
    }

    private Task RemoveByNativeIdAsync(uint nativeId)
    {
        string? targetId = null;
        lock (_lock)
        {
            var match = _activeNotifications.FirstOrDefault(n => n.NativeId == nativeId);
            if (match != null)
            {
                targetId = match.Id;
                _activeNotifications.Remove(match);
            }
        }

        if (targetId != null)
        {
            NotificationDismissed?.Invoke(this, targetId);

            if (_eventEngine != null)
            {
                _eventEngine.Publish(NovaEvent.CreateNotificationEvent(
                    eventType: "notification.dismissed",
                    title: "Native Notification Cleared",
                    description: targetId,
                    payload: targetId,
                    priority: EventPriority.Low));
            }
        }

        return Task.CompletedTask;
    }

    public Task ClearAllAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WindowsNotificationService));

        int clearedCount;
        lock (_lock)
        {
            clearedCount = _activeNotifications.Count;
            _activeNotifications.Clear();
        }

        bool nativeCleared = false;
        if (_listener != null && WindowsAccessStatus == NotificationAccessStatus.Allowed)
        {
            try
            {
                _listener.ClearNotifications();
                nativeCleared = true;
                AppLogger.Info("All native Action Center notifications cleared successfully.");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Could not clear native notifications: {ex.Message}");
            }
        }
        else
        {
            AppLogger.Info($"Cleared {clearedCount} notifications from NOVA Island queue (Native Action Center clear skipped: WindowsAccessStatus={WindowsAccessStatus}).");
        }

        if (_eventEngine != null)
        {
            _eventEngine.Publish(NovaEvent.CreateNotificationEvent(
                eventType: "notification.cleared",
                title: nativeCleared ? "Notifications Cleared (All)" : "Island Notifications Cleared",
                description: nativeCleared ? "All notifications dismissed from NOVA and Action Center" : "All notifications cleared from NOVA Island queue",
                priority: EventPriority.Low));
        }

        return Task.CompletedTask;
    }

    public void PruneExpired()
    {
        if (_isDisposed) return;

        List<string> expiredIds = new();
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var expired = _activeNotifications.Where(n => n.IsExpired(now)).ToList();
            foreach (var n in expired)
            {
                expiredIds.Add(n.Id);
                _activeNotifications.Remove(n);
            }
        }

        foreach (var id in expiredIds)
        {
            NotificationDismissed?.Invoke(this, id);
        }
    }

    private static NotificationAccessStatus MapAccessStatus(UserNotificationListenerAccessStatus status)
    {
        return status switch
        {
            UserNotificationListenerAccessStatus.Allowed => NotificationAccessStatus.Allowed,
            UserNotificationListenerAccessStatus.Denied => NotificationAccessStatus.Denied,
            UserNotificationListenerAccessStatus.Unspecified => NotificationAccessStatus.Unspecified,
            _ => NotificationAccessStatus.Unspecified
        };
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        try
        {
            if (_listener != null)
            {
                _listener.NotificationChanged -= OnListenerNotificationChanged;
                _listener = null;
            }

            lock (_lock)
            {
                _activeNotifications.Clear();
            }

            AppLogger.Info("WindowsNotificationService disposed cleanly.");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error during WindowsNotificationService disposal: {ex.Message}");
        }
    }
}

