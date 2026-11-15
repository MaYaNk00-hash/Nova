using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using Nova.Core;

namespace Nova.Events;

/// <summary>
/// Thread-safe central event engine with priority queueing, FIFO ordering within priority bands,
/// deduplication, expiration filtering, safe UI dispatch, and clean lifecycle management.
/// </summary>
public sealed class EventEngine : IDisposable
{
    private sealed record QueuedEventItem(NovaEvent Event, long SequenceNumber);

    private readonly ConcurrentQueue<QueuedEventItem> _criticalQueue = new();
    private readonly ConcurrentQueue<QueuedEventItem> _highQueue = new();
    private readonly ConcurrentQueue<QueuedEventItem> _normalQueue = new();
    private readonly ConcurrentQueue<QueuedEventItem> _lowQueue = new();

    private readonly List<EventSubscription> _subscriptions = new();
    private readonly object _subLock = new();

    private readonly ConcurrentDictionary<string, DateTimeOffset> _deduplicationCache = new();
    private readonly EventHistory _history;
    private readonly TimeSpan _defaultDeduplicationWindow = TimeSpan.FromSeconds(3);

    private readonly AutoResetEvent _queueSignal = new(false);
    private readonly Thread _workerThread;
    private readonly CancellationTokenSource _cts = new();

    private long _sequenceCounter = 0;
    private volatile bool _disposed = false;
    private Dispatcher? _customUIDispatcher;

    public EventHistory History => _history;

    /// <summary>
    /// Explicit UI Dispatcher for marshaling events to the UI thread.
    /// Defaults to Application.Current.Dispatcher when available.
    /// </summary>
    public Dispatcher? UIDispatcher
    {
        get => _customUIDispatcher ?? Application.Current?.Dispatcher;
        set => _customUIDispatcher = value;
    }

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

    public int QueuedEventCount =>
        _criticalQueue.Count + _highQueue.Count + _normalQueue.Count + _lowQueue.Count;

    public bool IsDisposed => _disposed;

    public EventEngine(int historyCapacity = 100, bool startWorker = true)
    {
        _history = new EventHistory(historyCapacity);

        if (startWorker)
        {
            _workerThread = new Thread(ProcessQueueLoop)
            {
                Name = "Nova.EventEngine.Worker",
                IsBackground = true
            };
            _workerThread.Start();
        }
        else
        {
            _workerThread = null!;
        }

        AppLogger.Info("EventEngine initialized with priority-aware queues and worker pump.");
    }

    /// <summary>
    /// Publishes an event to the priority queues with deduplication and expiration validation.
    /// Returns true if accepted and enqueued; false if expired, duplicate, or disposed.
    /// </summary>
    public bool Publish(NovaEvent novaEvent)
    {
        ArgumentNullException.ThrowIfNull(novaEvent);

        if (_disposed)
        {
            AppLogger.Warning($"Publish rejected: EventEngine is disposed. EventType='{novaEvent.EventType}'.");
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

        // 3. Record to History immediately on acceptance
        _history.Add(novaEvent);

        // 4. Assign Monotonic Sequence Number and Enqueue by Priority
        long seq = Interlocked.Increment(ref _sequenceCounter);
        var item = new QueuedEventItem(novaEvent, seq);

        switch (novaEvent.Priority)
        {
            case EventPriority.Critical:
                _criticalQueue.Enqueue(item);
                break;
            case EventPriority.High:
                _highQueue.Enqueue(item);
                break;
            case EventPriority.Normal:
                _normalQueue.Enqueue(item);
                break;
            case EventPriority.Low:
            default:
                _lowQueue.Enqueue(item);
                break;
        }

        AppLogger.Debug($"Enqueued event [{novaEvent.Priority}] '{novaEvent.EventType}' (Seq={seq}).");

        // 5. Wake up background worker pump
        _queueSignal.Set();
        return true;
    }

    private void ProcessQueueLoop()
    {
        while (!_cts.IsCancellationRequested && !_disposed)
        {
            try
            {
                bool processedAny = DrainNextEvent();
                if (!processedAny)
                {
                    _queueSignal.WaitOne(50);
                }
            }
            catch (Exception ex)
            {
                if (!_disposed && !_cts.IsCancellationRequested)
                {
                    AppLogger.Error("Unexpected error in EventEngine queue loop", ex);
                }
            }
        }
    }

    /// <summary>
    /// Dequeues the single highest priority event in strict priority order:
    /// Critical > High > Normal > Low. Equal priority events are processed FIFO.
    /// </summary>
    private bool DrainNextEvent()
    {
        QueuedEventItem? item = null;

        if (_criticalQueue.TryDequeue(out item)) { }
        else if (_highQueue.TryDequeue(out item)) { }
        else if (_normalQueue.TryDequeue(out item)) { }
        else if (_lowQueue.TryDequeue(out item)) { }

        if (item == null) return false;

        DispatchToSubscribers(item.Event);
        return true;
    }

    /// <summary>
    /// Synchronously drains all currently queued events in strict priority order.
    /// Useful for deterministic unit testing and shutdown flushing.
    /// </summary>
    public int DrainAllQueued()
    {
        int count = 0;
        while (DrainNextEvent())
        {
            count++;
        }
        return count;
    }

    private void DispatchToSubscribers(NovaEvent novaEvent)
    {
        List<EventSubscription> targets;
        lock (_subLock)
        {
            targets = _subscriptions.Where(s => s.Matches(novaEvent)).ToList();
        }

        foreach (var sub in targets)
        {
            DeliverToSubscriber(sub, novaEvent);
        }
    }

    private void DeliverToSubscriber(EventSubscription subscription, NovaEvent novaEvent)
    {
        if (subscription.IsDisposed || _disposed) return;

        if (subscription.DeliverOnUIThread)
        {
            var dispatcher = UIDispatcher;
            if (dispatcher != null)
            {
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    AppLogger.Debug($"Skipped UI event '{novaEvent.EventType}': Dispatcher has already shut down.");
                    return;
                }

                if (dispatcher.CheckAccess())
                {
                    SafeInvoke(subscription, novaEvent);
                }
                else
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
                    {
                        if (!subscription.IsDisposed && !_disposed)
                        {
                            SafeInvoke(subscription, novaEvent);
                        }
                    });
                }
            }
            else
            {
                // CRITICAL SAFETY REQUIREMENT:
                // When DeliverOnUIThread is true but no WPF Dispatcher is available (e.g. headless or shutdown),
                // we MUST NEVER invoke UI handlers on arbitrary background threads to avoid cross-thread UI violations.
                AppLogger.Warning($"Cannot deliver event '{novaEvent.EventType}' to UI subscriber {subscription.Id}: No active WPF UI Dispatcher is available.");
            }
            return;
        }

        // Non-UI subscriber: invoke directly
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
            AppLogger.Error($"Subscriber {subscription.Id} threw unhandled exception while processing event '{novaEvent.EventType}'", ex);
        }
    }

    public EventSubscription Subscribe(
        Action<NovaEvent> handler,
        Func<NovaEvent, bool>? filter = null,
        bool deliverOnUIThread = true)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EventEngine), "Cannot subscribe to a disposed EventEngine.");
        }

        var subscription = new EventSubscription(this, handler, filter, deliverOnUIThread);
        lock (_subLock)
        {
            _subscriptions.Add(subscription);
        }

        AppLogger.Debug($"Created event subscription: {subscription.Id}. Active subscriptions: {ActiveSubscriptionCount}.");
        return subscription;
    }

    public EventSubscription Subscribe(
        string eventType,
        Action<NovaEvent> handler,
        bool deliverOnUIThread = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return Subscribe(handler, e => string.Equals(e.EventType, eventType, StringComparison.OrdinalIgnoreCase), deliverOnUIThread);
    }

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
        if (_disposed) return;

        lock (_subLock)
        {
            if (_disposed) return;
            _disposed = true;

            AppLogger.Info("EventEngine disposing: cancelling worker and clearing subscriptions.");

            _cts.Cancel();
            _queueSignal.Set();

            // Clear subscriptions cleanly
            foreach (var sub in _subscriptions.ToArray())
            {
                sub.Dispose();
            }
            _subscriptions.Clear();

            // Clear queues
            _criticalQueue.Clear();
            _highQueue.Clear();
            _normalQueue.Clear();
            _lowQueue.Clear();

            _deduplicationCache.Clear();
            _history.Clear();

            _cts.Dispose();
            _queueSignal.Dispose();

            AppLogger.Info("EventEngine disposal complete.");
        }
    }
}
