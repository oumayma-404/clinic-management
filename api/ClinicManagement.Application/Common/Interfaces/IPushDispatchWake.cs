namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Asks for the push dispatcher to run at a given instant rather than at its next minutely tick (staff-chat): a chat
/// message's phone notification would otherwise leave 30–90 s after its grace. Best-effort — the minutely tick still
/// sends anything a wake misses.
/// </summary>
public interface IPushDispatchWake
{
    void Request(DateTime dueUtc);
}

/// <summary>Where nothing hosts the dispatcher (console verbs, tests): the minutely tick is the only sender.</summary>
public sealed class NoPushDispatchWake : IPushDispatchWake
{
    public void Request(DateTime dueUtc)
    {
    }
}
