using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Notifications;

/// <summary>
/// Service abstraction for Windows notification detection, management, and Dynamic Island presentation.
/// </summary>
public interface INotificationService : IDisposable
{
    /// <summary>
    /// Current privacy permission status for Windows UserNotificationListener.
    /// </summary>
    NotificationAccessStatus WindowsAccessStatus { get; }

    /// <summary>
    /// Indicates whether external notification capture from third-party Windows apps is currently available.
    /// In classic unpackaged Win32 execution, this is false because Windows restricts UserNotificationListener to packaged applications.
    /// </summary>
    bool IsExternalCaptureAvailable { get; }

    /// <summary>
    /// Indicates whether dismissing a notification will clear it from the native Windows Action Center.
    /// </summary>
    bool CanDismissNativeNotifications { get; }

    /// <summary>
    /// Human-readable explanation of the current Windows notification access status and requirements.
    /// </summary>
    string AccessStatusExplanation { get; }

    /// <summary>
    /// Active notifications ordered by Priority (Critical > High > Normal > Low) then Timestamp.
    /// </summary>
    IReadOnlyList<NovaNotification> ActiveNotifications { get; }

    /// <summary>
    /// The highest-priority, most recent active notification, or null if none active.
    /// </summary>
    NovaNotification? CurrentNotification { get; }

    /// <summary>
    /// Fired whenever a new notification arrives.
    /// </summary>
    event EventHandler<NovaNotification>? NotificationArrived;

    /// <summary>
    /// Fired when a notification is dismissed or expired.
    /// </summary>
    event EventHandler<string>? NotificationDismissed;

    /// <summary>
    /// Initializes notification listeners and checks Windows permissions.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Prompts Windows for notification access if supported and unprompted.
    /// </summary>
    Task<bool> RequestWindowsAccessAsync();

    /// <summary>
    /// Dismisses a notification by ID from both NOVA and native Windows Action Center (if supported).
    /// </summary>
    Task<bool> TryDismissAsync(string notificationId);

    /// <summary>
    /// Clears all active notifications from NOVA and Windows Action Center (if supported).
    /// </summary>
    Task ClearAllAsync();

    /// <summary>
    /// Posts a local notification into the active queue and publishes to EventEngine.
    /// </summary>
    void PostNotification(NovaNotification notification);

    /// <summary>
    /// Forces a sweep of expired notifications.
    /// </summary>
    void PruneExpired();
}

