using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using Nova.Core;

namespace Nova.Events;

public sealed class EventEngine : IDisposable
{
    private readonly List<EventSubscription> _subscriptions = new();
    private readonly object _subLock = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _deduplicationCache = new();
    private readonly EventHistory _history;
    private readonly TimeSpan _defaultDeduplicationWindow = TimeSpan.FromSeconds(3);
    private bool _disposed = false;

    public EventHistory History => _history;
    public int ActiveSubscriptionCount
    {
        get
        {
            lock (_subLock)
            {
                return _subscriptions.Count;
            }
        }
    }

    public EventEngine(int historyCapacity = 100)
    {
        _history = new EventHistory(historyCapacity);
        AppLogger.Info("EventEngine initialized.");
    }

    /// <summary>
    /// Publishes an event to all matching subscribers with deduplication, expiration, and priority handling.
    /// </summary>
    public bool Publish(NovaEvent novaEvent)
    {
        ArgumentNullException.ThrowIfNull(novaEvent);

        if (_disposed)
        {
            AppLogger.Warning($"Cannot publish event '{novaEvent.EventType}' after EventEngine is disposed.");
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        // 1. Expiration Check
        if (novaEvent.IsExpired(now))
        {
            AppLogger.Debug($"Dropped expired event '{novaEvent.EventType}' (Expired at: {novaEvent.ExpiresAt}).");
            return false;
        }

        // 2. Deduplication Check
        if (!string.IsNullOrWhiteSpace(novaEvent.DeduplicationKey))
        {
            if (_deduplicationCache.TryGetValue(novaEvent.DeduplicationKey, out var existingExpiry))
            {
                if (now < existingExpiry)
                {
                    AppLogger.Debug($"Suppressed duplicate event with key '{novaEvent.DeduplicationKey}' (EventType: {novaEvent.EventType}).");
                    return false;
                }
            }

            var window = novaEvent.ExpiresAt.HasValue
                ? novaEvent.ExpiresAt.Value - now
                : _defaultDeduplicationWindow;

            _deduplicationCache[novaEvent.DeduplicationKey] = now.Add(window);
            PruneDeduplicationCache(now);
        }

        // 3. Record to History
        _history.Add(novaEvent);
        AppLogger.Debug($"Event published: [{novaEvent.Priority}] {novaEvent.EventType} from {novaEvent.Source} (Id: {novaEvent.Id})");

        // 4. Capture Matching Subscribers (Thread-safe snapshot)
        List<EventSubscription> targets;
        lock (_subLock)
        {
            targets = _subscriptions.Where(s => s.Matches(novaEvent)).ToList();
        }

        // 5. Dispatch Event
        foreach (var sub in targets)
        {
            DeliverToSubscriber(sub, novaEvent);
        }

        return true;
    }

    private void DeliverToSubscriber(EventSubscription subscription, NovaEvent novaEvent)
    {
        if (subscription.IsDisposed) return;

        if (subscription.DeliverOnUIThread)
        {
            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.FromThread(Thread.CurrentThread);
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(DispatcherPriority.Normal, () => SafeInvoke(subscription, novaEvent));
                return;
            }
        }

        SafeInvoke(subscription, novaEvent);
    }

    private static void SafeInvoke(EventSubscription subscription, NovaEvent novaEvent)
    {
        try
        {
            subscription.Handler(novaEvent);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Subscriber threw unhandled exception while processing event '{novaEvent.EventType}'", ex);
        }
    }

    /// <summary>
    /// Subscribes to events with an optional filter predicate.
    /// </summary>
    public EventSubscription Subscribe(
        Action<NovaEvent> handler,
        Func<NovaEvent, bool>? filter = null,
        bool deliverOnUIThread = true)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new EventSubscription(this, handler, filter, deliverOnUIThread);
        lock (_subLock)
        {
            _subscriptions.Add(subscription);
        }

        AppLogger.Debug($"Created event subscription: {subscription.Id}. Active subscriptions: {ActiveSubscriptionCount}.");
        return subscription;
    }

    /// <summary>
    /// Subscribes specifically to an exact EventType string.
    /// </summary>
    public EventSubscription Subscribe(
        string eventType,
        Action<NovaEvent> handler,
        bool deliverOnUIThread = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return Subscribe(handler, e => string.Equals(e.EventType, eventType, StringComparison.OrdinalIgnoreCase), deliverOnUIThread);
    }

    /// <summary>
    /// Subscribes specifically to an EventCategory.
    /// </summary>
    public EventSubscription SubscribeCategory(
        EventCategory category,
        Action<NovaEvent> handler,
        bool deliverOnUIThread = true)
    {
        return Subscribe(handler, e => e.Category == category, deliverOnUIThread);
    }

    internal void Unsubscribe(EventSubscription subscription)
    {
        lock (_subLock)
        {
            _subscriptions.Remove(subscription);
        }
        AppLogger.Debug($"Removed event subscription: {subscription.Id}. Remaining: {ActiveSubscriptionCount}.");
    }

    private void PruneDeduplicationCache(DateTimeOffset now)
    {
        // Low-frequency prune when cache exceeds 50 items
        if (_deduplicationCache.Count > 50)
        {
            foreach (var kvp in _deduplicationCache)
            {
                if (now >= kvp.Value)
                {
                    _deduplicationCache.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_subLock)
        {
            if (!_disposed)
            {
                _disposed = true;
                foreach (var sub in _subscriptions.ToArray())
                {
                    sub.Dispose();
                }
                _subscriptions.Clear();
                _deduplicationCache.Clear();
                _history.Clear();
                AppLogger.Info("EventEngine disposed and all subscriptions cleared.");
            }
        }
    }
}
