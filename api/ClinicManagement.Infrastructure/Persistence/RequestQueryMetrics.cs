namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>SQL commands one scope's context ran and their total time — read by <c>RequestTimingMiddleware</c>.</summary>
public sealed class RequestQueryMetrics
{
    public int Commands { get; private set; }

    public TimeSpan DbTime { get; private set; }

    public void Record(TimeSpan duration)
    {
        Commands++;
        DbTime += duration;
    }
}
