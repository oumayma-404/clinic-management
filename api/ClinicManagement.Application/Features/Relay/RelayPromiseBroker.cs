using System.Collections.Concurrent;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>
/// D16's channel between a save on the cloud and its PC de secours. The PC sits behind the cabinet's box, so the cloud
/// cannot call it: the PC keeps a long poll open (<c>POST /api/relay/promises</c>) and this hands it the numbers a save is
/// about to make final, then waits for the PC to say it kept them — in the same poll, which it re-opens at once.
/// </summary>
public interface IRelayPromiseBroker
{
    /// <summary>The cloud's half: true when the PC kept every promise within <paramref name="timeout"/>.</summary>
    Task<bool> PromiseAsync(
        Guid relayId, IReadOnlyList<RelayNumberPromiseDto> promises, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>The PC's long poll: acknowledges what it kept, then returns what is waiting — or nothing after <paramref name="wait"/>.</summary>
    Task<IReadOnlyList<RelayNumberPromiseDto>> ExchangeAsync(
        Guid relayId, IReadOnlyCollection<Guid> acks, TimeSpan wait, CancellationToken cancellationToken);
}

/// <summary>
/// In memory, per PC. ⚠️ One API instance: a hosted deployment runs one (a second would need the promise and the poll to
/// meet on the same instance, like any in-memory channel). A promise nobody took within its timeout is withdrawn, so a
/// PC that reconnects later is never handed a number the cloud has already refused.
/// </summary>
public sealed class RelayPromiseBroker : IRelayPromiseBroker
{
    private sealed class Channel
    {
        public readonly object Gate = new();
        public readonly List<RelayNumberPromiseDto> Queue = new();
        public readonly Dictionary<Guid, TaskCompletionSource<bool>> Waiting = new();
        public TaskCompletionSource<bool> Signal = NewSignal();
    }

    private readonly ConcurrentDictionary<Guid, Channel> _channels = new();

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<bool> PromiseAsync(
        Guid relayId, IReadOnlyList<RelayNumberPromiseDto> promises, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (promises.Count == 0)
        {
            return true;
        }

        var channel = _channels.GetOrAdd(relayId, _ => new Channel());
        var kept = new List<Task<bool>>();
        lock (channel.Gate)
        {
            foreach (var promise in promises)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                channel.Waiting[promise.Id] = completion;
                channel.Queue.Add(promise);
                kept.Add(completion.Task);
            }

            channel.Signal.TrySetResult(true);
        }

        var all = Task.WhenAll(kept);
        var finished = await Task.WhenAny(all, Task.Delay(timeout, cancellationToken));
        if (finished == all)
        {
            return true;
        }

        lock (channel.Gate)
        {
            foreach (var promise in promises)
            {
                channel.Waiting.Remove(promise.Id);
                channel.Queue.RemoveAll(p => p.Id == promise.Id);
            }
        }

        return false;
    }

    public async Task<IReadOnlyList<RelayNumberPromiseDto>> ExchangeAsync(
        Guid relayId, IReadOnlyCollection<Guid> acks, TimeSpan wait, CancellationToken cancellationToken)
    {
        var channel = _channels.GetOrAdd(relayId, _ => new Channel());
        lock (channel.Gate)
        {
            foreach (var id in acks)
            {
                if (channel.Waiting.Remove(id, out var completion))
                {
                    completion.TrySetResult(true);
                }
            }
        }

        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            Task signal;
            lock (channel.Gate)
            {
                if (channel.Queue.Count > 0)
                {
                    var taken = channel.Queue.ToList();
                    channel.Queue.Clear();
                    return taken;
                }

                if (channel.Signal.Task.IsCompleted)
                {
                    channel.Signal = NewSignal();
                }

                signal = channel.Signal.Task;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || cancellationToken.IsCancellationRequested)
            {
                return Array.Empty<RelayNumberPromiseDto>();
            }

            try
            {
                await Task.WhenAny(signal, Task.Delay(remaining, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<RelayNumberPromiseDto>();
            }
        }
    }
}
