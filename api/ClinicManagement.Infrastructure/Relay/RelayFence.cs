using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// What each side may still write for a cabinet whose saves it does not hold (<c>clinic-pc-copy</c> D15) — declared
/// once, by table, and read by the change capture's net. The gate (<c>RelayLeaseGateMiddleware</c>) answers HTTP with
/// the sentence; this is what holds for every other path.
/// </summary>
public static class RelayFence
{
    /// <summary>
    /// FR-11: the carried tables the cloud keeps writing for a fenced cabinet. Every other carried table is refused —
    /// the PC may be recording the same rows. <c>RelayFenceTableCoverageTests</c> holds it against the model.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedOnFencedCloud =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(User)] = "signing in, and an administrator's account changes (FR-11)",
            [nameof(UserRecoveryCode)] = "a recovery code spent signing in, or a new set issued (FR-11)",
            [nameof(Doctor)] = "creating a doctor's account, or changing a role, writes the practitioner record (FR-11)",
            [nameof(ClinicSubscription)] = "the vendor's suspension, and the entitlement's fold (FR-11)",
            [nameof(SubscriptionPeriod)] = "the vendor records a payment or cancels one (FR-11)",
            [nameof(StaffNotification)] = "the cloud's own bell rows: account notices, warnings, the PC de secours card (D15)",
        };

    /// <summary>
    /// On a PC de secours that does not hold the saves, a sign-in is the one write left: its traces are each side's own
    /// (<see cref="ClinicRelayScope.PerSideColumns"/>), plus the row's stamp and the hash's format upgrade a sign-in may
    /// make — the hourly check repairs the latter from the cloud.
    /// </summary>
    public static readonly IReadOnlySet<string> SignInColumns = ClinicRelayScope.PerSideColumns[nameof(User)]
        .Concat(new[] { nameof(User.UpdatedAt), nameof(User.PasswordHash) })
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>A sign-in's own write on a <see cref="User"/> row — the only save a following PC accepts.</summary>
    public static bool IsSignInTrace(EntityEntry entry) =>
        entry.Entity is User
        && entry.State == EntityState.Modified
        && entry.Properties.Where(p => p.IsModified).All(p => SignInColumns.Contains(p.Metadata.Name));

    /// <summary>The cloud's half: the cabinet's current PC may hold its saves.</summary>
    public static bool CloudRefuses(ClinicRelay? relay, DateTime nowUtc) => ClinicWriteLease.IsCloudFenced(relay, nowUtc);
}

/// <summary><see cref="IClinicWriteFence"/> for the deployment this process is: the cloud asks the lease, a PC its holding.</summary>
public sealed class ClinicWriteFence : IClinicWriteFence
{
    private readonly DeploymentProfile _profile;
    private readonly IClinicRelayRepository _relays;
    private readonly IRelayLocalStatus _local;
    private readonly Dictionary<Guid, bool> _answered = new();

    public ClinicWriteFence(DeploymentProfile profile, IClinicRelayRepository relays, IRelayLocalStatus local)
    {
        _profile = profile;
        _relays = relays;
        _local = local;
    }

    /// <summary>Answered once per cabinet per scope — a job's tick; a fence that closes mid-tick is the net's to catch.</summary>
    public async Task<bool> RefusesAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        if (_profile.MirrorsCloudClinic)
        {
            return !_local.IsHolding;
        }

        if (!_profile.PublishesChangeFeed)
        {
            return false;
        }

        if (_answered.TryGetValue(clinicId, out var known))
        {
            return known;
        }

        var refuses = RelayFence.CloudRefuses(await _relays.GetCurrentForClinicAsync(clinicId, cancellationToken), DateTime.UtcNow);
        _answered[clinicId] = refuses;
        return refuses;
    }
}
