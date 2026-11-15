using System.Windows.Threading;
using Nova.Events;

namespace Nova.Tests;

public class EventEngineTests
{
    [Fact("Verify publishing and subscribing delivers event correctly")]
    public void PublishAndSubscribe_DeliversEvent()
    {
        using var engine = new EventEngine(startWorker: false);
        NovaEvent? received = null;

        using var sub = engine.Subscribe(e => received = e, deliverOnUIThread: false);

        var testEvent = NovaEvent.Create("test.event", "TestSource", "Test Title", "Test Desc", payload: 42);
        bool published = engine.Publish(testEvent);

        Assert.True(published);
        Assert.Equal(1, engine.QueuedEventCount);

        engine.DrainAllQueued();

        Assert.NotNull(received);
        Assert.Equal(testEvent.Id, received!.Id);
        Assert.Equal("test.event", received.EventType);
        Assert.Equal("TestSource", received.Source);
        Assert.Equal(42, received.Payload);
    }

    [Fact("Verify strict priority ordering: Critical > High > Normal > Low")]
    public void PriorityOrdering_DeliversCriticalBeforeHighBeforeNormalBeforeLow()
    {
        using var engine = new EventEngine(startWorker: false);
        var deliveryOrder = new List<string>();

        using var sub = engine.Subscribe(e => deliveryOrder.Add(e.EventType), deliverOnUIThread: false);

        // Publish in mixed reverse order: Low, Normal, High, Critical
        engine.Publish(NovaEvent.Create("evt.low", "Src", "Low Priority", priority: EventPriority.Low));
        engine.Publish(NovaEvent.Create("evt.normal", "Src", "Normal Priority", priority: EventPriority.Normal));
        engine.Publish(NovaEvent.Create("evt.high", "Src", "High Priority", priority: EventPriority.High));
        engine.Publish(NovaEvent.Create("evt.critical", "Src", "Critical Priority", priority: EventPriority.Critical));

        Assert.Equal(4, engine.QueuedEventCount);

        // Drain the queue
        engine.DrainAllQueued();

        // Must be delivered strictly Critical -> High -> Normal -> Low
        Assert.Equal(4, deliveryOrder.Count);
        Assert.Equal("evt.critical", deliveryOrder[0]);
        Assert.Equal("evt.high", deliveryOrder[1]);
        Assert.Equal("evt.normal", deliveryOrder[2]);
        Assert.Equal("evt.low", deliveryOrder[3]);
    }

    [Fact("Verify deterministic FIFO ordering for equal priority events")]
    public void FIFOOrdering_WithinEqualPriority_PreservesArrivalOrder()
    {
        using var engine = new EventEngine(startWorker: false);
        var deliveryOrder = new List<string>();

        using var sub = engine.Subscribe(e => deliveryOrder.Add(e.Title), deliverOnUIThread: false);

        // Publish 5 Normal priority events in order
        for (int i = 1; i <= 5; i++)
        {
            engine.Publish(NovaEvent.Create("evt.same_priority", "Src", $"Item {i}", priority: EventPriority.Normal));
        }

        engine.DrainAllQueued();

        Assert.Equal(5, deliveryOrder.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal($"Item {i + 1}", deliveryOrder[i]);
        }
    }

    [Fact("Verify filter delivers only matching events")]
    public void Filter_DeliversOnlyMatchingEvents()
    {
        using var engine = new EventEngine(startWorker: false);
        int matchedCount = 0;
        int unmatchedCount = 0;

        using var subMatched = engine.Subscribe("target.event", e => matchedCount++, deliverOnUIThread: false);
        using var subCategory = engine.SubscribeCategory(EventCategory.System, e => unmatchedCount++, deliverOnUIThread: false);

        engine.Publish(NovaEvent.Create("target.event", "SourceA", "TitleA", category: EventCategory.UserAction));
        engine.Publish(NovaEvent.Create("other.event", "SourceB", "TitleB", category: EventCategory.UserAction));

        engine.DrainAllQueued();

        Assert.Equal(1, matchedCount);
        Assert.Equal(0, unmatchedCount);
    }

    [Fact("Verify deduplication suppresses duplicate events within time window")]
    public void Deduplication_SuppressesDuplicateEvents()
    {
        using var engine = new EventEngine(startWorker: false);
        int receivedCount = 0;

        using var sub = engine.Subscribe(e => receivedCount++, deliverOnUIThread: false);

        var event1 = NovaEvent.Create("dedup.event", "Source", "Title", deduplicationKey: "KEY-123");
        var event2 = NovaEvent.Create("dedup.event", "Source", "Title", deduplicationKey: "KEY-123");
        var event3 = NovaEvent.Create("dedup.event", "Source", "Title", deduplicationKey: "KEY-456");

        bool pub1 = engine.Publish(event1);
        bool pub2 = engine.Publish(event2);
        bool pub3 = engine.Publish(event3);

        Assert.True(pub1, "First event with KEY-123 should publish.");
        Assert.False(pub2, "Duplicate event with KEY-123 should be suppressed.");
        Assert.True(pub3, "Event with new KEY-456 should publish.");

        engine.DrainAllQueued();
        Assert.Equal(2, receivedCount);
    }

    [Fact("Verify expired events are dropped and not delivered")]
    public void Expiry_ExpiredEventsAreNotDelivered()
    {
        using var engine = new EventEngine(startWorker: false);
        int receivedCount = 0;

        using var sub = engine.Subscribe(e => receivedCount++, deliverOnUIThread: false);

        var pastTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var expiredEvent = new NovaEvent
        {
            EventType = "expired.event",
            Source = "TestSource",
            Title = "Expired",
            ExpiresAt = pastTime
        };

        bool published = engine.Publish(expiredEvent);

        Assert.False(published, "Expired event should not be published.");
        engine.DrainAllQueued();
        Assert.Equal(0, receivedCount);
    }

    [Fact("Verify event priority is preserved")]
    public void Priority_EventsRetainPriority()
    {
        using var engine = new EventEngine(startWorker: false);
        NovaEvent? received = null;

        using var sub = engine.Subscribe(e => received = e, deliverOnUIThread: false);

        engine.Publish(NovaEvent.Create("critical.event", "Source", "Critical Event", priority: EventPriority.Critical));
        engine.DrainAllQueued();

        Assert.NotNull(received);
        Assert.Equal(EventPriority.Critical, received!.Priority);
    }

    [Fact("Verify disposing subscription stops delivery and cleans up memory")]
    public void SubscriptionDisposal_StopsDelivering()
    {
        using var engine = new EventEngine(startWorker: false);
        int count = 0;

        var sub = engine.Subscribe(e => count++, deliverOnUIThread: false);
        Assert.Equal(1, engine.ActiveSubscriptionCount);

        engine.Publish(NovaEvent.Create("test.event", "Source", "Title 1"));
        engine.DrainAllQueued();
        Assert.Equal(1, count);

        sub.Dispose();
        Assert.Equal(0, engine.ActiveSubscriptionCount);

        engine.Publish(NovaEvent.Create("test.event", "Source", "Title 2"));
        engine.DrainAllQueued();
        Assert.Equal(1, count);
    }

    [Fact("Verify faulty subscriber throwing exception does not break other subscribers")]
    public void ErrorHandling_FaultySubscriberDoesNotBreakEngine()
    {
        using var engine = new EventEngine(startWorker: false);
        bool secondSubscriberExecuted = false;

        using var badSub = engine.Subscribe(e => throw new InvalidOperationException("Subscriber failure simulation"), deliverOnUIThread: false);
        using var goodSub = engine.Subscribe(e => secondSubscriberExecuted = true, deliverOnUIThread: false);

        bool published = engine.Publish(NovaEvent.Create("error.test", "Source", "Error Test"));
        engine.DrainAllQueued();

        Assert.True(published);
        Assert.True(secondSubscriberExecuted, "Good subscriber should still receive event despite bad subscriber error.");
    }

    [Fact("Verify EventHistory records recent events and bounds capacity")]
    public void EventHistory_RecordsAndBoundsEvents()
    {
        var history = new EventHistory(capacity: 5);

        for (int i = 1; i <= 10; i++)
        {
            history.Add(NovaEvent.Create($"event.{i}", "Source", $"Title {i}"));
        }

        Assert.Equal(5, history.Count);

        var recent = history.GetRecent(5);
        Assert.Equal(5, recent.Count);
        Assert.Equal("event.10", recent[0].EventType);
        Assert.Equal("event.6", recent[4].EventType);
    }

    [Fact("Verify thread-safe concurrent publishing with background worker")]
    public void ConcurrentPublishing_ThreadSafeDelivery()
    {
        using var engine = new EventEngine(historyCapacity: 500, startWorker: true);
        int totalReceived = 0;

        using var sub = engine.Subscribe(e => Interlocked.Increment(ref totalReceived), deliverOnUIThread: false);

        int threadCount = 8;
        int eventsPerThread = 50;
        var threads = new Thread[threadCount];

        for (int t = 0; t < threadCount; t++)
        {
            int threadId = t;
            threads[t] = new Thread(() =>
            {
                for (int i = 0; i < eventsPerThread; i++)
                {
                    engine.Publish(NovaEvent.Create($"concurrent.{threadId}.{i}", $"Thread-{threadId}", $"Item {i}"));
                }
            });
            threads[t].Start();
        }

        foreach (var t in threads)
        {
            t.Join(5000);
        }

        // Allow worker thread to drain
        int waitMs = 0;
        while (totalReceived < threadCount * eventsPerThread && waitMs < 3000)
        {
            Thread.Sleep(20);
            waitMs += 20;
        }

        Assert.Equal(threadCount * eventsPerThread, totalReceived);
        Assert.Equal(threadCount * eventsPerThread, engine.History.Count);
    }

    [Fact("Verify UI subscriber is NEVER invoked on background thread when no Dispatcher is available")]
    public void UIDispatch_NeverInvokesOnBackgroundThread_WithoutDispatcher()
    {
        using var engine = new EventEngine(startWorker: false);
        engine.UIDispatcher = null; // Explicitly no dispatcher available
        bool invokedOnBackgroundThread = false;

        using var sub = engine.Subscribe(e =>
        {
            invokedOnBackgroundThread = true;
        }, deliverOnUIThread: true);

        engine.Publish(NovaEvent.Create("ui.test", "Source", "Title"));
        engine.DrainAllQueued();

        // Must NOT invoke on background thread
        Assert.False(invokedOnBackgroundThread, "UI subscriber must never be called on background thread when no UI Dispatcher exists.");
    }

    [Fact("Verify UI dispatch marshals safely to STA Dispatcher thread")]
    public void UIDispatch_MarshalsToDispatcherThread_WhenAvailable()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var staDispatcher = Dispatcher.CurrentDispatcher;
                using var engine = new EventEngine(startWorker: false);
                engine.UIDispatcher = staDispatcher;

                int executingThreadId = -1;
                using var sub = engine.Subscribe(e =>
                {
                    executingThreadId = Environment.CurrentManagedThreadId;
                }, deliverOnUIThread: true);

                // Publish from background thread
                var bgThread = new Thread(() =>
                {
                    engine.Publish(NovaEvent.Create("bg.event", "Background", "Title"));
                    engine.DrainAllQueued();
                });
                bgThread.Start();
                bgThread.Join(2000);

                // Process pending operations on the STA dispatcher
                var frame = new DispatcherFrame();
                staDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
                Dispatcher.PushFrame(frame);

                Assert.Equal(Environment.CurrentManagedThreadId, executingThreadId);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (threadException != null)
        {
            throw new InvalidOperationException($"STA thread failed: {threadException.Message}", threadException);
        }
    }

    [Fact("Verify EventEngine shutdown and disposal lifecycle")]
    public void Shutdown_DisposalCleansUpAndRejectsPublishing()
    {
        var engine = new EventEngine(startWorker: true);

        int received = 0;
        var sub = engine.Subscribe(e => received++, deliverOnUIThread: false);
        Assert.Equal(1, engine.ActiveSubscriptionCount);
        Assert.False(engine.IsDisposed);

        // Publish before dispose
        bool pub1 = engine.Publish(NovaEvent.Create("pre.dispose", "Src", "Title"));
        Assert.True(pub1);

        // Dispose engine
        engine.Dispose();
        Assert.True(engine.IsDisposed);
        Assert.Equal(0, engine.ActiveSubscriptionCount);
        Assert.True(sub.IsDisposed);

        // Publishing after disposal returns false without crashing
        bool pub2 = engine.Publish(NovaEvent.Create("post.dispose", "Src", "Title"));
        Assert.False(pub2, "Publish after disposal must return false.");

        // Subscribing after disposal throws ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() =>
        {
            engine.Subscribe(e => { }, deliverOnUIThread: false);
        });
    }
}
