namespace Nova.Events;

public sealed class EventHistory
{
    private readonly LinkedList<NovaEvent> _events = new();
    private readonly object _lock = new();
    private readonly int _capacity;

    public int Capacity => _capacity;
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _events.Count;
            }
        }
    }

    public EventHistory(int capacity = 100)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        _capacity = capacity;
    }

    public void Add(NovaEvent novaEvent)
    {
        ArgumentNullException.ThrowIfNull(novaEvent);

        lock (_lock)
        {
            while (_events.Count >= _capacity)
            {
                _events.RemoveFirst();
            }
            _events.AddLast(novaEvent);
        }
    }

    public IReadOnlyList<NovaEvent> GetRecent(int count = 50)
    {
        lock (_lock)
        {
            var takeCount = Math.Min(count, _events.Count);
            var result = new List<NovaEvent>(takeCount);
            var node = _events.Last;
            while (node != null && result.Count < takeCount)
            {
                result.Add(node.Value);
                node = node.Previous;
            }
            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }
}
