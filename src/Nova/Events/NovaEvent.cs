namespace Nova.Events;

public sealed record NovaEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Source { get; init; } = string.Empty;
    public EventCategory Category { get; init; } = EventCategory.General;
    public string EventType { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public EventPriority Priority { get; init; } = EventPriority.Normal;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public object? Payload { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? DeduplicationKey { get; init; }

    public bool IsExpired(DateTimeOffset? now = null)
    {
        if (ExpiresAt == null) return false;
        var checkTime = now ?? DateTimeOffset.UtcNow;
        return checkTime > ExpiresAt.Value;
    }

    public static NovaEvent Create(
        string eventType,
        string source,
        string title,
        string description = "",
        EventCategory category = EventCategory.General,
        EventPriority priority = EventPriority.Normal,
        object? payload = null,
        TimeSpan? timeToLive = null,
        string? deduplicationKey = null)
    {
        return new NovaEvent
        {
            EventType = eventType,
            Source = source,
            Title = title,
            Description = description,
            Category = category,
            Priority = priority,
            Payload = payload,
            ExpiresAt = timeToLive.HasValue ? DateTimeOffset.UtcNow.Add(timeToLive.Value) : null,
            DeduplicationKey = deduplicationKey
        };
    }

    public static NovaEvent CreateTestEvent(
        string eventType,
        string title,
        string description = "",
        EventPriority priority = EventPriority.Normal,
        object? payload = null,
        TimeSpan? timeToLive = null,
        string? deduplicationKey = null)
    {
        return Create(
            eventType: eventType,
            source: "Nova.Test",
            title: title,
            description: description,
            category: EventCategory.Test,
            priority: priority,
            payload: payload,
            timeToLive: timeToLive,
            deduplicationKey: deduplicationKey);
    }

    public static NovaEvent CreateCompanionEvent(
        string eventType,
        string title,
        string description = "",
        EventPriority priority = EventPriority.Normal,
        object? payload = null)
    {
        return Create(
            eventType: eventType,
            source: "Nova.Companion",
            title: title,
            description: description,
            category: EventCategory.Companion,
            priority: priority,
            payload: payload);
    }

    public static NovaEvent CreateSystemEvent(
        string eventType,
        string title,
        object? payload = null,
        EventPriority priority = EventPriority.Normal)
    {
        return Create(
            eventType: eventType,
            source: "Nova.System",
            title: title,
            category: EventCategory.System,
            priority: priority,
            payload: payload);
    }

    public static NovaEvent CreateMediaEvent(
        string eventType,
        string title,
        string description = "",
        object? payload = null,
        EventPriority priority = EventPriority.Normal,
        string? deduplicationKey = null)
    {
        return Create(
            eventType: eventType,
            source: "Nova.Media",
            title: title,
            description: description,
            category: EventCategory.Media,
            priority: priority,
            payload: payload,
            deduplicationKey: deduplicationKey);
    }

    public static NovaEvent CreateNotificationEvent(
        string eventType,
        string title,
        string description = "",
        object? payload = null,
        EventPriority priority = EventPriority.Normal,
        TimeSpan? timeToLive = null,
        string? deduplicationKey = null)
    {
        return Create(
            eventType: eventType,
            source: "Nova.Notification",
            title: title,
            description: description,
            category: EventCategory.Notification,
            priority: priority,
            payload: payload,
            timeToLive: timeToLive,
            deduplicationKey: deduplicationKey);
    }
}

