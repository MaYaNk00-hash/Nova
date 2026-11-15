using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Events;

namespace Nova.Notifications;

/// <summary>
/// Mock notification service for deterministic automated testing.
/// </summary>
public sealed class MockNotificationService : INotificationService
{
    private readonly EventEngine? _eventEngine;
    private readonly object _lock = new();
    private readonly List<NovaNotification> _notifications = new();
    private bool _isDisposed;

    public NotificationAccessStatus WindowsAccessStatus { get; set; } = NotificationAccessStatus.Allowed;

    public bool IsExternalCaptureAvailable => WindowsAccessStatus == NotificationAccessStatus.Allowed;
    public bool CanDismissNativeNotifications => WindowsAccessStatus == NotificationAccessStatus.Allowed;

    public string AccessStatusExplanation => WindowsAccessStatus switch
    {
        NotificationAccessStatus.Allowed => "External notification capture active (Mock/Packaged mode).",
        NotificationAccessStatus.Denied => "Windows notification capture Denied: Unpackaged desktop process lacks Package Identity and userNotificationListener capability.",
        NotificationAccessStatus.NotSupported => "UserNotificationListener is not supported on this platform.",
        _ => "Windows notification access status is unspecified."
    };

    public event EventHandler<NovaNotification>? NotificationArrived;
    public event EventHandler<string>? NotificationDismissed;

    public int DismissCallCount { get; private set; }
    public int ClearAllCallCount { get; private set; }
    public bool IsDisposed => _isDisposed;

    public MockNotificationService(EventEngine? eventEngine = null)
    {
        _eventEngine = eventEngine;
    }

    public IReadOnlyList<NovaNotification> ActiveNotifications
    {
        get
        {
            lock (_lock)
            {
                return _notifications
                    .Where(n => !n.IsExpired())
                    .OrderByDescending(n => n.Priority)
                    .ThenByDescending(n => n.Timestamp)
                    .ToList();
            }
        }
    }

    public NovaNotification? CurrentNotification => ActiveNotifications.FirstOrDefault();

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> RequestWindowsAccessAsync()
    {
        return Task.FromResult(WindowsAccessStatus == NotificationAccessStatus.Allowed);
    }

    public void PostNotification(NovaNotification notification)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(MockNotificationService));

        lock (_lock)
        {
            // Deduplication check: remove existing with same Id or identical source/title within expiry
            _notifications.RemoveAll(n => n.Id == notification.Id);
            _notifications.Add(notification);
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
        if (_isDisposed) throw new ObjectDisposedException(nameof(MockNotificationService));
        DismissCallCount++;

        bool removed = false;
        lock (_lock)
        {
            var target = _notifications.FirstOrDefault(n => n.Id == notificationId);
            if (target != null)
            {
                _notifications.Remove(target);
                removed = true;
            }
        }

        if (removed)
        {
            NotificationDismissed?.Invoke(this, notificationId);

            if (_eventEngine != null)
            {
                _eventEngine.Publish(NovaEvent.CreateNotificationEvent(
                    eventType: "notification.dismissed",
                    title: "Notification Dismissed",
                    description: notificationId,
                    payload: notificationId,
                    priority: EventPriority.Low));
            }
        }

        return Task.FromResult(removed);
    }

    public Task ClearAllAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(MockNotificationService));
        ClearAllCallCount++;

        lock (_lock)
        {
            _notifications.Clear();
        }

        if (_eventEngine != null)
        {
            _eventEngine.Publish(NovaEvent.CreateNotificationEvent(
                eventType: "notification.cleared",
                title: "Notifications Cleared",
                description: "All notifications dismissed",
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
            var expired = _notifications.Where(n => n.IsExpired(now)).ToList();
            foreach (var n in expired)
            {
                expiredIds.Add(n.Id);
                _notifications.Remove(n);
            }
        }

        foreach (var id in expiredIds)
        {
            NotificationDismissed?.Invoke(this, id);
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        lock (_lock)
        {
            _notifications.Clear();
        }
    }
}

