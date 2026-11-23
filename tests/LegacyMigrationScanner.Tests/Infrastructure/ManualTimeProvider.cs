namespace LegacyMigrationScanner.Tests.Infrastructure;

/// <summary>Controllable clock; delays complete immediately so retry tests do not sleep.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // Fire asynchronously right away (used by Task.Delay with this provider), never synchronously
        // inside CreateTimer: the caller may not have stored the timer yet.
        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new ImmediateTimer();
    }

    private sealed class ImmediateTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
