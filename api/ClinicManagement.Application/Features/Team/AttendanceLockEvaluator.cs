using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Team;

/// <summary>
/// Gathers <see cref="AttendanceLockFacts"/> for one account and asks <see cref="AttendanceLockRules.IsLocked"/> — for
/// the 423 gate on every save and for « Pointez votre arrivée ». Cheapest fact first, and the rule is asked after each:
/// every fact can only lift the lock, so the first one that does ends the read (an admin's save costs no query).
/// </summary>
public sealed class AttendanceLockEvaluator
{
    private readonly IClinicRepository _clinics;
    private readonly IAttendanceEntryRepository _entries;
    private readonly IAttendanceTerminalRepository _terminals;
    private readonly IStaffLeaveRequestRepository _leaves;
    private readonly IRelayLocalStatus _relayLocal;
    private readonly IClinicRelayRepository _relays;
    private readonly ISubscriptionPolicy _subscriptionPolicy;
    private readonly IClinicSubscriptionRepository _subscriptions;

    public AttendanceLockEvaluator(
        IClinicRepository clinics, IAttendanceEntryRepository entries, IAttendanceTerminalRepository terminals,
        IStaffLeaveRequestRepository leaves, IRelayLocalStatus relayLocal, IClinicRelayRepository relays,
        ISubscriptionPolicy subscriptionPolicy, IClinicSubscriptionRepository subscriptions)
    {
        _clinics = clinics;
        _entries = entries;
        _terminals = terminals;
        _leaves = leaves;
        _relayLocal = relayLocal;
        _relays = relays;
        _subscriptionPolicy = subscriptionPolicy;
        _subscriptions = subscriptions;
    }

    /// <summary>
    /// A tracked account, never an admin's: the switch that lifts the lock and « Ajouter un pointage » (AC-5) are hers,
    /// so a locked admin with a broken borne could never get back in.
    /// </summary>
    public static bool IsLockable(User user) => user.IsActive && user.IsAttendanceTracked && !user.IsAdmin();

    public Task<bool> IsLockedAsync(User user, DateTime nowUtc, CancellationToken cancellationToken) =>
        IsLockedAsync(user, nowUtc, alreadyRead: null, cancellationToken);

    /// <summary>The same answer, reusing the two reads « clock-status » has already made instead of repeating them.</summary>
    public async Task<bool> IsLockedAsync(
        User user, DateTime nowUtc, AttendanceLockReads? alreadyRead, CancellationToken cancellationToken) =>
        AttendanceLockRules.IsLocked(await FactsAsync(user, nowUtc, alreadyRead, cancellationToken));

    /// <summary>Only the facts read until one lifted the lock are real; the rest keep their « still locked » value.</summary>
    private async Task<AttendanceLockFacts> FactsAsync(
        User user, DateTime nowUtc, AttendanceLockReads? alreadyRead, CancellationToken cancellationToken)
    {
        var facts = new AttendanceLockFacts(
            AttendanceEnabled: true, LockEnabled: true, Tracked: IsLockable(user),
            InsideOpeningHours: true, HasLineToday: false, OnLeaveToday: false, OnPcDeSecours: _relayLocal.IsPcDeSecours,
            MayRecordWork: true, HasActiveBorne: true, LockStarted: true);
        if (!AttendanceLockRules.IsLocked(facts))
            return facts;

        var switches = alreadyRead is not null
            ? alreadyRead.Switches
            : await _clinics.GetAttendanceSwitchesAsync(user.ClinicId, cancellationToken);
        facts = facts with
        {
            AttendanceEnabled = switches?.AttendanceEnabled == true,
            LockEnabled = switches?.LockEnabled == true,
            InsideOpeningHours = AttendanceLockRules.IsInsideOpeningHours(switches?.WorkingHoursJson, nowUtc),
        };
        if (!AttendanceLockRules.IsLocked(facts))
            return facts;

        var today = AttendanceRules.Today(nowUtc);
        facts = facts with
        {
            HasLineToday = alreadyRead?.HasLineToday
                           ?? await _entries.HasLineOnAsync(user.ClinicId, user.Id, today, cancellationToken),
        };
        if (!AttendanceLockRules.IsLocked(facts))
            return facts;

        facts = facts with { OnLeaveToday = await _leaves.HasApprovedCoveringAsync(user.ClinicId, user.Id, today, cancellationToken) };
        if (!AttendanceLockRules.IsLocked(facts))
            return facts;

        var bornes = await _terminals.GetLiveBornesAsync(user.ClinicId, cancellationToken);
        facts = facts with
        {
            HasActiveBorne = bornes.Any,
            LockStarted = AttendanceLockRules.HasLockStarted(
                AttendanceLockRules.LockStartsAfterUtc(switches?.LockArmedAtUtc, bornes.FirstActivatedAtUtc),
                switches?.WorkingHoursJson, nowUtc),
        };
        if (!AttendanceLockRules.IsLocked(facts))
            return facts;

        facts = facts with { MayRecordWork = await MayRecordWorkAsync(user.ClinicId, nowUtc, cancellationToken) };
        if (!AttendanceLockRules.IsLocked(facts))
            return facts;

        // On the cloud, a cut held by the cabinet's PC de secours: its people work on the PC, where no borne reaches.
        var relay = await _relays.GetCurrentForClinicAsync(user.ClinicId, cancellationToken);
        return facts with { OnPcDeSecours = ClinicWriteLease.IsCloudFenced(relay, nowUtc) };
    }

    /// <summary>The borne asks the same reader before a punch, so a lapsed cabinet's lock would be one nobody can lift.</summary>
    private async Task<bool> MayRecordWorkAsync(Guid clinicId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (!_subscriptionPolicy.RequiresSubscription)
            return true;
        var subscription = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);
        return subscription is not null
               && SubscriptionStateReader.Read(subscription, ClinicClock.ClinicToday(nowUtc)).AllowsWrites;
    }
}

/// <summary>What « clock-status » has already read for the caller: the cabinet's switches and whether she has a line today.</summary>
public sealed record AttendanceLockReads(ClinicAttendanceSwitches? Switches, bool HasLineToday);
