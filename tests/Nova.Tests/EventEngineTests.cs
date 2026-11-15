using System.Windows.Threading;
using Nova.Events;

namespace Nova.Tests;

public class EventEngineTests
{
    [Fact("Verify publishing and subscribing delivers event correctly")]
    public void PublishAndSubscribe_DeliversEvent()
    {
        using var engine = new EventEngine();
        NovaEvent? received = null;

        using var sub = engine.Subscribe(e => received = e, deliverOnUIThread: false);

        var testEvent = NovaEvent.Create("test.event", "TestSource", "Test Title", "Test Desc", payload: 42);
        bool published = engine.Publish(testEvent);

        Assert.True(published);
        Assert.NotNull(received);
        Assert.Equal(testEvent.Id, received!.Id);
        Assert.Equal("test.event", received.EventType);
        Assert.Equal("TestSource", received.Source);
        Assert.Equal(42, received.Payload);
    }

    [Fact("Verify filter delivers only matching events")]
    public void Filter_DeliversOnlyMatchingEvents()
    {
        using var engine = new EventEngine();
        int matchedCount = 0;
        int unmatchedCount = 0;

        using var subMatched = engine.Subscribe("target.event", e => matchedCount++, deliverOnUIThread: false);
        using var subCategory = engine.SubscribeCategory(EventCategory.System, e => unmatchedCount++, deliverOnUIThread: false);

        engine.Publish(NovaEvent.Create("target.event", "SourceA", "TitleA", category: EventCategory.UserAction));
        engine.Publish(NovaEvent.Create("other.event", "SourceB", "TitleB", category: EventCategory.UserAction));

        Assert.Equal(1, matchedCount);
        Assert.Equal(0, unmatchedCount);
    }

    [Fact("Verify deduplication suppresses duplicate events within time window")]
    public void Deduplication_SuppressesDuplicateEvents()
    {
        using var engine = new EventEngine();
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
        Assert.Equal(2, receivedCount);
    }

    [Fact("Verify expired events are dropped and not delivered")]
    public void Expiry_ExpiredEventsAreNotDelivered()
    {
        using var engine = new EventEngine();
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
        Assert.Equal(0, receivedCount);
    }

    [Fact("Verify event priority is preserved")]
    public void Priority_EventsRetainPriority()
    {
        using var engine = new EventEngine();
        NovaEvent? received = null;

        using var sub = engine.Subscribe(e => received = e, deliverOnUIThread: false);

        engine.Publish(NovaEvent.Create("critical.event", "Source", "Critical Event", priority: EventPriority.Critical));

        Assert.NotNull(received);
        Assert.Equal(EventPriority.Critical, received!.Priority);
    }

    [Fact("Verify disposing subscription stops delivery and cleans up memory")]
    public void SubscriptionDisposal_StopsDelivering()
    {
        using var engine = new EventEngine();
        int count = 0;

        var sub = engine.Subscribe(e => count++, deliverOnUIThread: false);
        Assert.Equal(1, engine.ActiveSubscriptionCount);

        engine.Publish(NovaEvent.Create("test.event", "Source", "Title 1"));
        Assert.Equal(1, count);

        sub.Dispose();
        Assert.Equal(0, engine.ActiveSubscriptionCount);

        engine.Publish(NovaEvent.Create("test.event", "Source", "Title 2"));
        Assert.Equal(1, count); // No increase after dispose
    }

    [Fact("Verify faulty subscriber throwing exception does not break other subscribers")]
    public void ErrorHandling_FaultySubscriberDoesNotBreakEngine()
    {
        using var engine = new EventEngine();
        bool secondSubscriberExecuted = false;

        using var badSub = engine.Subscribe(e => throw new InvalidOperationException("Subscriber failure simulation"), deliverOnUIThread: false);
        using var goodSub = engine.Subscribe(e => secondSubscriberExecuted = true, deliverOnUIThread: false);

        bool published = engine.Publish(NovaEvent.Create("error.test", "Source", "Error Test"));

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
        // Most recent should be event.10
        Assert.Equal("event.10", recent[0].EventType);
        Assert.Equal("event.6", recent[4].EventType);
    }

    [Fact("Verify thread-safe concurrent publishing")]
    public void ConcurrentPublishing_ThreadSafeDelivery()
    {
        using var engine = new EventEngine(historyCapacity: 500);
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

        Assert.Equal(threadCount * eventsPerThread, totalReceived);
        Assert.Equal(threadCount * eventsPerThread, engine.History.Count);
    }

    [Fact("Verify UI thread dispatch safety on STA thread")]
    public void UIDispatchSafety_DeliversOnUIThread()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var engine = new EventEngine();
                bool delivered = false;

                using var sub = engine.Subscribe(e =>
                {
                    delivered = true;
                }, deliverOnUIThread: true);

                // Publish from background thread
                var bgThread = new Thread(() =>
                {
                    engine.Publish(NovaEvent.Create("bg.event", "Background", "Title"));
                });
                bgThread.Start();
                bgThread.Join(2000);

                Assert.True(delivered, "Event should be delivered.");
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
}
