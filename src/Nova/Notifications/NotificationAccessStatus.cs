namespace Nova.Notifications;

/// <summary>
/// Status of Windows User Notification Listener privacy permissions.
/// </summary>
public enum NotificationAccessStatus
{
    /// <summary>
    /// User or policy has granted permission to read Windows notifications.
    /// </summary>
    Allowed,

    /// <summary>
    /// Windows privacy settings have denied access to notification content.
    /// </summary>
    Denied,

    /// <summary>
    /// Permission has not been determined or prompt is required.
    /// </summary>
    Unspecified,

    /// <summary>
    /// Operating system platform does not support UserNotificationListener.
    /// </summary>
    NotSupported
}

