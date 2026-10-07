using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Services;

/// <summary>The FR-2 states of a PC de secours, as one ranked answer. Persisted nowhere: derived on every read.</summary>
public enum ClinicRelayState
{
    None = 0,
    Installing = 1,
    InstallFailed = 2,
    Abandoned = 3,
    Ready = 4,
    Late = 5,
    Off = 6,
    Updating = 7,
    DiskNearlyFull = 8,
    Mismatch = 9,
    Retired = 10,
}

/// <summary>The state plus the instant its sentence names (« depuis 08:12 », « Copie de 14:32 »).</summary>
public sealed record ClinicRelayHealthReading(ClinicRelayState State, DateTime? Since, int? SeedPercent)
{
    /// <summary>Whether this is a problem admins are told about (AC-2.2, AC-2.3).</summary>
    public bool IsProblem => State is ClinicRelayState.Late or ClinicRelayState.Off or ClinicRelayState.DiskNearlyFull
        or ClinicRelayState.Mismatch or ClinicRelayState.InstallFailed or ClinicRelayState.Abandoned;
}

/// <summary>The one FR-2 predicate, read by « Paramètres », the bell and the vendor console alike.</summary>
public static class ClinicRelayHealth
{
    /// <summary>No heartbeat for this long and the PC is « Éteint ».</summary>
    public static readonly TimeSpan SilentAfter = TimeSpan.FromMinutes(2);

    /// <summary>Not ready for longer than this and the PC is « En retard » (FR-2).</summary>
    public static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(2);

    /// <summary>Below this much free disk the PC is « Disque presque plein ».</summary>
    public const long DiskNearlyFullBytes = 5L * 1024 * 1024 * 1024;

    public static ClinicRelayHealthReading Read(ClinicRelay? relay, DateTime nowUtc)
    {
        if (relay is null)
        {
            return new(ClinicRelayState.None, null, null);
        }

        if (relay.Status == ClinicRelayStatus.Retired)
        {
            return relay.RetiredReason == ClinicRelayRetirement.Abandoned
                ? new(ClinicRelayState.Abandoned, relay.RetiredAtUtc, null)
                : new(ClinicRelayState.Retired, relay.RetiredAtUtc, null);
        }

        if (relay.IsAbandoned(nowUtc))
        {
            return new(ClinicRelayState.Abandoned, relay.LastSeenAtUtc ?? relay.PairedAtUtc, null);
        }

        if (relay.Status is ClinicRelayStatus.Pairing or ClinicRelayStatus.Seeding)
        {
            return relay.LastError is not null
                ? new(ClinicRelayState.InstallFailed, relay.LastSeenAtUtc, relay.SeedPercent)
                : new(ClinicRelayState.Installing, relay.LastSeenAtUtc, relay.SeedPercent);
        }

        var lastSeen = relay.LastSeenAtUtc ?? relay.PairedAtUtc ?? relay.CreatedAtUtc;
        if (nowUtc - lastSeen > SilentAfter)
        {
            return new(ClinicRelayState.Off, lastSeen, null);
        }

        if (relay.IsUpdating)
        {
            return new(ClinicRelayState.Updating, lastSeen, null);
        }

        if (relay.MismatchSinceUtc is { } mismatch)
        {
            return new(ClinicRelayState.Mismatch, mismatch, null);
        }

        if (relay.DiskFreeBytes is { } free && free < DiskNearlyFullBytes)
        {
            return new(ClinicRelayState.DiskNearlyFull, lastSeen, null);
        }

        var lastReady = relay.LastReadyAtUtc ?? relay.SeededAtUtc ?? lastSeen;
        if (!relay.IsCaughtUp && nowUtc - lastReady > LateAfter)
        {
            return new(ClinicRelayState.Late, lastReady, null);
        }

        return new(ClinicRelayState.Ready, relay.IsCaughtUp ? lastSeen : lastReady, null);
    }
}
