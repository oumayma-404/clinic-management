using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Runs the push dispatcher at the instant a chat message's grace ends (staff-chat) — the minutely tick alone sent
/// it 30–90 s late. In-process rather than a scheduled Hangfire job, whose storage is polled every 15 s.
///
/// <para>⚠️ <b>It calls <see cref="PushDispatchJob"/> directly</b>, so Hangfire's lock does not cover it: the job's
/// own in-process gate is what stops a wake and a tick sending the same row twice.</para>
/// </summary>
public sealed class PushDispatchWake : BackgroundService, IPushDispatchWake
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PushDispatchWake> _logger;
    private readonly object _lock = new();
    private readonly SortedSet<DateTime> _due = new();
    private readonly SemaphoreSlim _changed = new(0);

    public PushDispatchWake(IServiceScopeFactory scopes, ILogger<PushDispatchWake> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public void Request(DateTime dueUtc)
    {
        lock (_lock)
        {
            _due.Add(dueUtc);
        }

        _changed.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTime? next;
            lock (_lock)
            {
                next = _due.Count > 0 ? _due.Min : null;
            }

            var wait = next is null ? Timeout.InfiniteTimeSpan : next.Value - DateTime.UtcNow;
            if (next is null || wait > TimeSpan.Zero)
            {
                try
                {
                    // Woken by a new request (it may be earlier) or by the deadline.
                    await _changed.WaitAsync(wait, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            lock (_lock)
            {
                _due.RemoveWhere(d => d <= DateTime.UtcNow);
            }

            await RunOnceAsync();
        }
    }

    private async Task RunOnceAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var job = ActivatorUtilities.CreateInstance<PushDispatchJob>(scope.ServiceProvider);
            await job.DispatchQueuedPushes();
        }
        catch (Exception ex)
        {
            // The minutely tick sends whatever this missed.
            _logger.LogError(ex, "Immediate push dispatch failed");
        }
    }
}
