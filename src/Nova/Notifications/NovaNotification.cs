using System;
using System.IO;
using Nova.Events;

namespace Nova.Notifications;

/// <summary>
/// Immutable representation of a notification in the NOVA system.
/// </summary>
public sealed record NovaNotification
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public uint? NativeId { get; init; }
    public NotificationSourceType SourceType { get; init; } = NotificationSourceType.Application;
    public string SourceAppId { get; init; } = string.Empty;
    public string SourceDisplayName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public EventPriority Priority { get; init; } = EventPriority.Normal;
    public string Category { get; init; } = "General";
    public byte[]? IconBytes { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool CanDismiss { get; init; } = true;
    public bool CanDismissFromIsland => CanDismiss;
    public bool CanDismissFromActionCenter { get; init; } = false;
    public bool CanOpen { get; init; } = false;

    /// <summary>
    /// Explicit tooltip describing what dismissing this notification will actually do.
    /// Distinguishes clearing NOVA's internal queue from native Action Center dismissal.
    /// </summary>
    public string DismissTooltip => CanDismissFromActionCenter
        ? "Dismiss from NOVA Island and native Windows Action Center"
        : "Clear from NOVA Island (Native Action Center dismissal requires packaged MSIX with notificationListener capability)";

    public bool HasContent => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Message);
    public bool HasIcon => IconBytes != null && IconBytes.Length > 0;

    public bool IsExpired(DateTimeOffset? now = null)
    {
        if (ExpiresAt == null) return false;
        var checkTime = now ?? DateTimeOffset.UtcNow;
        return checkTime > ExpiresAt.Value;
    }

    /// <summary>
    /// Compact summary display string (e.g. "Slack: New message from Sarah").
    /// </summary>
    public string CompactSummary
    {
        get
        {
            var source = !string.IsNullOrWhiteSpace(SourceDisplayName) ? SourceDisplayName : "Notice";
            if (!string.IsNullOrWhiteSpace(Title))
                return $"{source}: {Title}";
            if (!string.IsNullOrWhiteSpace(Message))
                return $"{source}: {Message}";
            return source;
        }
    }

    public static NovaNotification Empty { get; } = new()
    {
        Title = string.Empty,
        Message = string.Empty,
        SourceDisplayName = "No Notifications"
    };

    /// <summary>
    /// Cleans up raw Windows Application User Model IDs or executable names into human-readable app names.
    /// </summary>
    public static string FormatSourceDisplayName(string? rawAppId)
    {
        if (string.IsNullOrWhiteSpace(rawAppId))
            return "Windows System";

        var id = rawAppId.Trim();
        if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (id.Contains("msedge", StringComparison.OrdinalIgnoreCase) || id.Contains("MicrosoftEdge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        if (id.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (id.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Mozilla Firefox";
        if (id.Contains("Slack", StringComparison.OrdinalIgnoreCase)) return "Slack";
        if (id.Contains("Discord", StringComparison.OrdinalIgnoreCase)) return "Discord";
        if (id.Contains("Teams", StringComparison.OrdinalIgnoreCase)) return "Microsoft Teams";
        if (id.Contains("Outlook", StringComparison.OrdinalIgnoreCase)) return "Outlook";
        if (id.Contains("Mail", StringComparison.OrdinalIgnoreCase)) return "Windows Mail";
        if (id.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) return "WhatsApp";
        if (id.Contains("Telegram", StringComparison.OrdinalIgnoreCase)) return "Telegram";
        if (id.Contains("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)) return "Windows System";
        if (id.Contains("powershell", StringComparison.OrdinalIgnoreCase)) return "PowerShell";
        if (id.Contains("Nova", StringComparison.OrdinalIgnoreCase)) return "NOVA";

        try
        {
            if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var withoutExt = Path.GetFileNameWithoutExtension(id);
                if (!string.IsNullOrWhiteSpace(withoutExt))
                    return char.ToUpperInvariant(withoutExt[0]) + withoutExt.Substring(1);
            }

            var bangIndex = id.IndexOf('!');
            if (bangIndex > 0 && bangIndex < id.Length - 1)
            {
                return id.Substring(bangIndex + 1);
            }
        }
        catch { }

        return id;
    }
}

