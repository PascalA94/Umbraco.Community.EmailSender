using Microsoft.Extensions.Time.Testing;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>
/// A fake clock that knows which timers are pending, so a test moves time only when the code under test is waiting on one.
/// Advancing on wall-clock time made results depend on machine speed (M6 QA review): a slow SDK call would see the clock jump.
/// </summary>
internal sealed class SteppingClock(DateTimeOffset start) : TimeProvider
{
    private readonly FakeTimeProvider inner = new(start);
    private readonly object gate = new();
    private readonly List<TrackedTimer> timers = [];

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override long GetTimestamp() => inner.GetTimestamp();

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public void Advance(TimeSpan by) => inner.Advance(by);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TrackedTimer(this, inner.CreateTimer(callback, state, dueTime, period));
        timer.Track(dueTime);
        lock (gate)
        {
            timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Due times still in the future, earliest first.</summary>
    public IReadOnlyList<DateTimeOffset> Pending()
    {
        var now = GetUtcNow();
        lock (gate)
        {
            return [.. timers.Select(t => t.Due).OfType<DateTimeOffset>().Where(d => d > now).Order()];
        }
    }

    /// <summary>
    /// Drives a send to completion. Time moves to the next due timer only when something is provably waiting on the clock:
    /// two pending timers (the budget plus a retry wait), or a fake server holding a request open (a hang, so the budget is
    /// what will end it). Otherwise real work is in progress and the loop just yields.
    /// </summary>
    public async Task<Exception?> DriveAsync(Task task, Func<int> requestsInFlight)
    {
        for (var i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            await Task.WhenAny(task, Task.Delay(5));
            if (task.IsCompleted)
            {
                break;
            }

            var pending = Pending();
            if (pending.Count >= 2 || (pending.Count == 1 && requestsInFlight() > 0))
            {
                inner.Advance(pending[0] - GetUtcNow());
            }
        }

        Assert.True(task.IsCompleted, "The send never finished.");
        try
        {
            await task;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private void Remove(TrackedTimer timer)
    {
        lock (gate)
        {
            timers.Remove(timer);
        }
    }

    private sealed class TrackedTimer(SteppingClock owner, ITimer timer) : ITimer
    {
        public DateTimeOffset? Due { get; private set; }

        public void Track(TimeSpan dueTime)
            => Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Track(dueTime);
            return timer.Change(dueTime, period);
        }

        public void Dispose()
        {
            owner.Remove(this);
            timer.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            owner.Remove(this);
            return timer.DisposeAsync();
        }
    }
}
