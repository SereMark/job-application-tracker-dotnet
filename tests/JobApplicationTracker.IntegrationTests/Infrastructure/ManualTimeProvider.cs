namespace JobApplicationTracker.IntegrationTests.Infrastructure;

public sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Time can only move forward in integration tests.");
        }

        _utcNow = _utcNow.Add(amount);
    }
}
