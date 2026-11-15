namespace Nova.Events;

public sealed class EventSubscription : IDisposable
{
    private readonly EventEngine _engine;
    private bool _disposed = false;
    private readonly object _lock = new();

    public Guid Id { get; } = Guid.NewGuid();
    public Func<NovaEvent, bool>? Filter { get; }
    public Action<NovaEvent> Handler { get; }
    public bool DeliverOnUIThread { get; }
    public bool IsDisposed => _disposed;

    internal EventSubscription(
        EventEngine engine,
        Action<NovaEvent> handler,
        Func<NovaEvent, bool>? filter = null,
        bool deliverOnUIThread = true)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Filter = filter;
        DeliverOnUIThread = deliverOnUIThread;
    }

    public bool Matches(NovaEvent novaEvent)
    {
        if (_disposed) return false;
        if (Filter == null) return true;
        try
        {
            return Filter(novaEvent);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                _disposed = true;
                _engine.Unsubscribe(this);
            }
        }
    }
}
