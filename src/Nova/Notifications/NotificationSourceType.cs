namespace Nova.Notifications;

/// <summary>
/// Origin or source category of a notification.
/// </summary>
public enum NotificationSourceType
{
    /// <summary>
    /// Captured from native Windows Action Center / Toast via UserNotificationListener.
    /// </summary>
    WindowsToast,

    /// <summary>
    /// Raised by Windows system, hardware, battery, or shell alert.
    /// </summary>
    SystemAlert,

    /// <summary>
    /// Raised by NOVA companion mascot.
    /// </summary>
    Companion,

    /// <summary>
    /// Sent by local applications or external processes.
    /// </summary>
    Application
}

