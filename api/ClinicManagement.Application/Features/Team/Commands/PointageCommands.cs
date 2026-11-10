using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Team.Commands;

/// <summary>
/// « Ajouter une borne »: an admin, step-up already spent by the controller, names it and picks the device. For her own
/// device the secret goes back to the controller for the cookie; for another signed-in device the borne waits, with no
/// secret, until that device claims it (<see cref="ClaimBorneCommand"/>). Only a SHA-256 of a secret is ever stored.
/// </summary>
public class RegisterAttendanceTerminalCommand : IRequest<Result<RegisteredTerminalDto>>
{
    public string? Name { get; set; }

    /// <summary>
    /// A live or waiting borne of this cabinet the new one replaces (this device's own, or a dead one), retired in the
    /// same save — whichever device the new borne is for.
    /// </summary>
    public Guid? ReplacingTerminalId { get; set; }

    /// <summary>Another signed-in device (its session) chosen from « Ajouter une borne »; absent for this device.</summary>
    public Guid? DeviceSessionId { get; set; }
}

public class RegisterAttendanceTerminalCommandHandler
    : IRequestHandler<RegisterAttendanceTerminalCommand, Result<RegisteredTerminalDto>>
{
    private readonly IAttendanceTerminalRepository _terminals;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly ISessionFamilyRepository _sessions;
    private readonly AttendanceNotifier _notifier;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterAttendanceTerminalCommandHandler(
        IAttendanceTerminalRepository terminals, IUserRepository users, IClinicRepository clinics,
        IClinicContext clinicContext, ISessionFamilyRepository sessions, AttendanceNotifier notifier, IUnitOfWork unitOfWork)
    {
        _terminals = terminals;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _sessions = sessions;
        _notifier = notifier;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RegisteredTerminalDto>> Handle(RegisterAttendanceTerminalCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: true, cancellationToken);
            if (resolved.IsFailure)
                return Result<RegisteredTerminalDto>.FailureFrom(resolved);
            var (caller, clinic) = resolved.Value;
            if (AttendanceTerminal.NameRefusalFor(request.Name) is { } refusal)
                return Result<RegisteredTerminalDto>.Failure(refusal);

            var now = DateTime.UtcNow;
            var replacing = request.ReplacingTerminalId is { } replacingId
                            && await _terminals.GetByIdAsync(replacingId, cancellationToken) is { } previous
                            && previous.ClinicId == clinic.Id && previous.IsActive
                ? previous
                : null;
            var cabinetBornes = await _terminals.ListAsync(clinic.Id, cancellationToken);

            if (request.DeviceSessionId is { } sessionId && sessionId != _clinicContext.GetSessionFamilyId())
                return await AssignToDeviceAsync(
                    request.Name!, sessionId, clinic.Id, caller.Id, replacing, cabinetBornes, now, cancellationToken);

            if (NameTaken(request.Name!, cabinetBornes, replacing?.Id) is { } taken)
                return taken;
            replacing?.Revoke(now);

            var secret = AttendanceRules.NewTerminalSecret();
            var terminal = AttendanceTerminal.Register(
                Guid.NewGuid(), clinic.Id, request.Name!, SessionCredential.Hash(secret), caller.Id, now);
            await _terminals.AddAsync(terminal, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _notifier.BorneLiveAsync(clinic.Id, terminal.Name, cancellationToken);

            return Result<RegisteredTerminalDto>.Success(new RegisteredTerminalDto
            {
                Terminal = AttendanceRules.ToDto(terminal),
                Secret = secret,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<RegisteredTerminalDto>.Failure("Erreur lors de l'enregistrement de la borne.", ex);
        }
    }

    private static Result<RegisteredTerminalDto>? NameTaken(
        string name, IEnumerable<AttendanceTerminal> cabinetBornes, params Guid?[] replaced) =>
        AttendanceRules.IsNameTaken(name, cabinetBornes, replaced.OfType<Guid>().ToList())
            ? Result<RegisteredTerminalDto>.Failure(AttendanceRules.BorneNameTaken(name), AttendanceRules.BorneNameTakenCode)
            : null;

    private async Task<Result<RegisteredTerminalDto>> AssignToDeviceAsync(
        string name, Guid sessionId, Guid clinicId, string callerId, AttendanceTerminal? replacing,
        IReadOnlyList<AttendanceTerminal> cabinetBornes, DateTime now, CancellationToken cancellationToken)
    {
        var session = await _sessions.GetByIdAsync(sessionId, cancellationToken);
        var owner = session is null ? null : await _users.GetByIdAsync(session.UserId, cancellationToken);
        if (!AttendanceRules.IsSignedIn(session, now) || owner is null || owner.ClinicId != clinicId || !owner.IsActive)
            return Result<RegisteredTerminalDto>.Failure(AttendanceRules.DeviceGone, AttendanceRules.DeviceGoneCode);

        // A tracked person's own phone as the borne would let her pointer from anywhere.
        if (owner.IsAttendanceTracked && AttendanceRules.DeviceKindOf(session!.DeviceLabel) == AttendanceRules.DeviceKindPhone)
            return Result<RegisteredTerminalDto>.Failure(AttendanceRules.BorneDevicePhone, AttendanceRules.BorneDevicePhoneCode);

        // Choosing the same device again replaces the earlier choice rather than leaving two bornes waiting for it.
        var earlier = await _terminals.FindPendingForSessionAsync(clinicId, sessionId, cancellationToken);
        if (NameTaken(name, cabinetBornes, replacing?.Id, earlier?.Id) is { } taken)
            return taken;
        earlier?.Revoke(now);
        replacing?.Revoke(now);

        var terminal = AttendanceTerminal.AssignToDevice(
            Guid.NewGuid(), clinicId, name, sessionId, AttendanceRules.DescribeDevice(owner, session!), callerId, now);
        await _terminals.AddAsync(terminal, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<RegisteredTerminalDto>.Success(new RegisteredTerminalDto { Terminal = AttendanceRules.ToDto(terminal) });
    }
}

/// <summary>
/// The device an admin chose in « Ajouter une borne » picks the borne up: asked by the app as soon as its clock status
/// says a borne waits for it. The secret is minted here, on this device's own request, so it never travels through the
/// admin's computer; a borne this device already was is retired, since its cookie is about to be replaced.
/// </summary>
public class ClaimBorneCommand : IRequest<Result<RegisteredTerminalDto>>
{
    /// <summary>The borne cookie this device carries now, if any.</summary>
    public string? CurrentSecret { get; set; }
}

public class ClaimBorneCommandHandler : IRequestHandler<ClaimBorneCommand, Result<RegisteredTerminalDto>>
{
    private readonly IAttendanceTerminalRepository _terminals;
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;
    private readonly ITenantScope _tenantScope;
    private readonly AttendanceNotifier _notifier;
    private readonly IUnitOfWork _unitOfWork;

    public ClaimBorneCommandHandler(
        IAttendanceTerminalRepository terminals, IUserRepository users, IClinicContext clinicContext,
        ITenantScope tenantScope, AttendanceNotifier notifier, IUnitOfWork unitOfWork)
    {
        _terminals = terminals;
        _users = users;
        _clinicContext = clinicContext;
        _tenantScope = tenantScope;
        _notifier = notifier;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RegisteredTerminalDto>> Handle(ClaimBorneCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<RegisteredTerminalDto>.FailureFrom(resolved);
            var me = resolved.Value!;

            var sessionId = _clinicContext.GetSessionFamilyId();
            var waiting = sessionId is { } id
                ? await _terminals.FindPendingForSessionAsync(me.ClinicId, id, cancellationToken)
                : null;
            if (waiting is null)
                return Result<RegisteredTerminalDto>.Failure(AttendanceRules.BorneNotWaiting, AttendanceRules.BorneNotWaitingCode);

            var now = DateTime.UtcNow;
            if (await AttendanceRules.FindBorneAsync(_terminals, _tenantScope, request.CurrentSecret, cancellationToken) is { } previous
                && previous.ClinicId == me.ClinicId && previous.Id != waiting.Id)
            {
                previous.Revoke(now);
            }

            var secret = AttendanceRules.NewTerminalSecret();
            waiting.Claim(SessionCredential.Hash(secret), now);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _notifier.BorneInstalledAsync(waiting, me, cancellationToken);
            await _notifier.BorneLiveAsync(me.ClinicId, waiting.Name, cancellationToken);

            return Result<RegisteredTerminalDto>.Success(new RegisteredTerminalDto
            {
                Terminal = AttendanceRules.ToDto(waiting),
                Secret = secret,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<RegisteredTerminalDto>.Failure("Erreur lors de l'installation de la borne.", ex);
        }
    }
}

/// <summary>
/// A tap at the borne. Anonymous: the cabinet comes from the borne's secret, the person from the tile, and her
/// schéma proves it is her. An arrival opens a line; a departure closes today's open line and never an older one.
/// </summary>
public class PunchCommand : IRequest<Result<PunchOutcome>>
{
    /// <summary>Set by the controller from the borne cookie; never from the body.</summary>
    public string? Secret { get; set; }

    public string UserId { get; set; } = string.Empty;
    public List<int>? Pattern { get; set; }

    /// <summary>« arrival » or « departure » as the borne announced it; null skips the check.</summary>
    public string? Expected { get; set; }

    /// <summary>
    /// « 17:30 »: the departure of an earlier day's line she left open, given at this arrival (<c>pointage-obligatoire</c>
    /// AC-8). Absent while such a line exists, the arrival is refused with what the borne's step shows.
    /// </summary>
    public string? DeclaredPreviousDeparture { get; set; }
}

/// <summary>Either the punch, or a schéma refusal carrying what the borne shows (tries left, lock end).</summary>
public sealed record PunchOutcome(PunchResultDto? Done, PunchRefusal? Refused);

public sealed record PunchRefusal(
    string Code, string Error, int? AttemptsLeft, DateTime? LockedUntil, PreviousDayDto? PreviousDay = null);

public class PunchCommandHandler : IRequestHandler<PunchCommand, Result<PunchOutcome>>
{
    private readonly IAttendanceTerminalRepository _terminals;
    private readonly IAttendanceEntryRepository _entries;
    private readonly IStaffLeaveRequestRepository _leaves;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly ISubscriptionPolicy _subscriptionPolicy;
    private readonly ITenantScope _tenantScope;
    private readonly ILocalAuthService _hasher;
    private readonly IAuditActorProvider _auditActor;
    private readonly IAuditEntryRepository _journal;
    private readonly IRealtimeNotifier _realtime;
    private readonly AttendanceNotifier _notifier;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PunchCommandHandler> _logger;

    // Set by a wrong schéma inside the transaction, acted on once it has committed.
    private (User Person, bool Blocked)? _schemaFailure;

    public PunchCommandHandler(
        IAttendanceTerminalRepository terminals, IAttendanceEntryRepository entries, IStaffLeaveRequestRepository leaves,
        IUserRepository users, IClinicRepository clinics, IClinicSubscriptionRepository subscriptions,
        ISubscriptionPolicy subscriptionPolicy, ITenantScope tenantScope, ILocalAuthService hasher,
        IAuditActorProvider auditActor, IAuditEntryRepository journal, IRealtimeNotifier realtime, AttendanceNotifier notifier,
        IUnitOfWork unitOfWork, ILogger<PunchCommandHandler> logger)
    {
        _notifier = notifier;
        _journal = journal;
        _terminals = terminals;
        _entries = entries;
        _leaves = leaves;
        _users = users;
        _clinics = clinics;
        _subscriptions = subscriptions;
        _subscriptionPolicy = subscriptionPolicy;
        _tenantScope = tenantScope;
        _hasher = hasher;
        _auditActor = auditActor;
        _realtime = realtime;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<PunchOutcome>> Handle(PunchCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var terminal = await AttendanceRules.FindBorneAsync(_terminals, _tenantScope, request.Secret, cancellationToken);
            if (terminal is null)
                return Result<PunchOutcome>.Failure(AttendanceRules.TerminalUnknown, AttendanceRules.TerminalUnknownCode);

            // Her row is locked until the punch commits, so parallel guesses queue and each is counted — and before
            // the clinic is read, since that read brings its users with it.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                await _users.LockForUpdateAsync(request.UserId, cancellationToken);
                var outcome = await PunchLockedAsync(request, terminal, cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                // A wrong schéma changes « Équipe aujourd'hui » too (tries left, a block), so it is broadcast like a punch.
                if (outcome.IsSuccess && (outcome.Value!.Done is not null || _schemaFailure is not null))
                    await BroadcastAsync(terminal.ClinicId, cancellationToken);
                if (_schemaFailure is { } failure && failure.Blocked && failure.Person.SchemaLockedUntilUtc is { } until)
                    await _notifier.SchemaBlockedAsync(terminal.ClinicId, failure.Person, until, cancellationToken);
                return outcome;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PunchOutcome>.Failure("Erreur lors du pointage.", ex);
        }
    }

    /// <summary>Every refusal that does not need the schéma comes first, so none of them can tell a right guess from a wrong one.</summary>
    private async Task<Result<PunchOutcome>> PunchLockedAsync(
        PunchCommand request, AttendanceTerminal terminal, CancellationToken cancellationToken)
    {
        var clinic = await _clinics.GetByIdAsync(terminal.ClinicId, cancellationToken);
        if (clinic is not { AttendanceEnabled: true })
            return Result<PunchOutcome>.Failure(AttendanceRules.ModuleOff, AttendanceRules.ModuleOffCode);

        // The subscription gate lets anonymous requests through, so the borne asks the same reader itself.
        if (await SubscriptionRefusalAsync(clinic.Id, cancellationToken) is { } lapsed)
            return Result<PunchOutcome>.Failure(lapsed.Error, lapsed.Code);

        var person = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (person is null || person.ClinicId != clinic.Id || !person.IsActive || !person.IsAttendanceTracked)
            return Result<PunchOutcome>.Failure(AttendanceRules.PersonNotTracked, AttendanceRules.PersonNotTrackedCode);
        if (!person.HasAttendanceSchema)
            return Result<PunchOutcome>.Failure(AttendanceRules.SchemaMissing, AttendanceRules.SchemaMissingCode);

        var now = DateTime.UtcNow;
        if (person.IsSchemaLocked(now))
            return Refused(AttendanceRules.SchemaLockedCode, AttendanceRules.SchemaLocked(person.SchemaLockedUntilUtc!.Value, now),
                null, person.SchemaLockedUntilUtc);

        var today = AttendanceRules.Today(now);
        var open = await _entries.FindLatestOpenAsync(clinic.Id, person.Id, cancellationToken);
        var departure = open is not null && open.LocalDay == today;
        var action = departure ? AttendanceRules.DepartureAction : AttendanceRules.ArrivalAction;
        if (request.Expected is { } expected && expected != action)
            return Result<PunchOutcome>.Failure(AttendanceRules.PunchStateChanged, AttendanceRules.PunchStateChangedCode);

        if (!departure)
        {
            var leaves = await _leaves.GetApprovedOverlappingAsync(clinic.Id, today, today, cancellationToken);
            if (AttendanceRules.IsOnFullDayLeave(person.Id, leaves, today))
                return Result<PunchOutcome>.Failure(AttendanceRules.OnLeave, AttendanceRules.OnLeaveCode);
        }

        // A stray tap is not a guess: a pattern that cannot be a schéma costs no try.
        if (AttendanceRules.SchemaRefusalFor(request.Pattern) is { } shape)
            return Result<PunchOutcome>.Failure(shape, AttendanceRules.SchemaInvalidCode);

        var verdict = _hasher.VerifyPassword(person.AttendanceSchemaHash!, AttendanceRules.PatternText(request.Pattern!));
        if (verdict == PasswordVerificationOutcome.Failed)
            return await WrongSchemaAsync(person, terminal, now, cancellationToken);

        var schemaStateChanged = verdict == PasswordVerificationOutcome.SuccessNeedsRehash || person.SchemaFailedAttempts > 0;
        if (verdict == PasswordVerificationOutcome.SuccessNeedsRehash)
            person.SetAttendanceSchema(_hasher.HashPassword(AttendanceRules.PatternText(request.Pattern!)));
        else if (person.SchemaFailedAttempts > 0)
            person.ClearSchemaFailures();

        // Earlier days left open are answered before this arrival lands, oldest first — after the schéma, so only she reads their hours.
        if (!departure)
        {
            var earlier = await _entries.ListOpenBeforeAsync(clinic.Id, person.Id, today, cancellationToken);
            if (earlier.Count > 0)
            {
                var oldest = earlier[0];
                if (string.IsNullOrWhiteSpace(request.DeclaredPreviousDeparture))
                {
                    if (schemaStateChanged)
                        await SaveVerifiedSchemaAsync(person, cancellationToken);
                    return await AskForDepartureAsync(clinic.Id, person.Id, oldest, today, cancellationToken);
                }

                if (AttendanceRules.ParseLocalTime(oldest.LocalDay, request.DeclaredPreviousDeparture) is not { } declared
                    || declared <= oldest.ArrivalUtc)
                {
                    if (schemaStateChanged)
                        await SaveVerifiedSchemaAsync(person, cancellationToken);
                    return Result<PunchOutcome>.Failure(
                        AttendanceLockRules.DeclaredDepartureInvalid(AttendanceRules.LocalTime(oldest.ArrivalUtc)),
                        AttendanceLockRules.DeclaredDepartureInvalidCode);
                }

                oldest.DeclareDeparture(declared, await LastSaveOnAsync(clinic.Id, person.Id, oldest.LocalDay, cancellationToken), today, now);
                if (earlier.Count > 1)
                {
                    await SaveVerifiedSchemaAsync(person, cancellationToken);
                    return await AskForDepartureAsync(clinic.Id, person.Id, earlier[1], today, cancellationToken);
                }
            }
        }

        if (departure)
            open!.Depart(now, today);
        else
            await _entries.AddAsync(
                AttendanceEntry.Arrive(Guid.NewGuid(), clinic.Id, person.Id, today, terminal.Id, terminal.Name, now),
                cancellationToken);

        // Once a day is enough for « Dernier pointage », and spares the journal a borne row per punch.
        if (terminal.LastUsedAt is not { } lastUsed || AttendanceRules.LocalDayOf(lastUsed) != today)
            terminal.Touch(now);
        _users.Update(person);
        _auditActor.AuthenticatedAs(person.Id, person.Email);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PunchOutcome>.Success(new PunchOutcome(new PunchResultDto
        {
            Action = action,
            At = AttendanceRules.LocalTime(now),
            Name = StaffLeaveRules.DisplayName(person),
            TerminalName = terminal.Name,
        }, null));
    }

    private async Task<Result<PunchOutcome>> WrongSchemaAsync(
        User person, AttendanceTerminal terminal, DateTime now, CancellationToken cancellationToken)
    {
        var locked = person.RegisterSchemaFailure(now);
        _users.Update(person);
        // Nobody proved who typed it, so the journal names the borne rather than the person on the tile.
        _auditActor.RunAs($"borne {terminal.Name}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _schemaFailure = (person, locked);

        return locked
            ? Refused(AttendanceRules.SchemaLockedCode, AttendanceRules.SchemaLocked(person.SchemaLockedUntilUtc!.Value, now),
                null, person.SchemaLockedUntilUtc)
            : Refused(AttendanceRules.SchemaWrongCode,
                AttendanceRules.SchemaWrong(User.MaxSchemaFailures - person.SchemaFailedAttempts),
                User.MaxSchemaFailures - person.SchemaFailedAttempts, null);
    }

    private static Result<PunchOutcome> Refused(
        string code, string error, int? attemptsLeft, DateTime? lockedUntil, PreviousDayDto? previousDay = null) =>
        Result<PunchOutcome>.Success(new PunchOutcome(null, new PunchRefusal(code, error, attemptsLeft, lockedUntil, previousDay)));

    /// <summary>« Votre départ d'hier n'a pas été pointé » for one open line: its arrival and her last save that day.</summary>
    private async Task<Result<PunchOutcome>> AskForDepartureAsync(
        Guid clinicId, string userId, AttendanceEntry line, DateOnly today, CancellationToken cancellationToken)
    {
        var lastSave = await LastSaveOnAsync(clinicId, userId, line.LocalDay, cancellationToken);
        return Refused(AttendanceLockRules.PreviousDepartureMissingCode, AttendanceLockRules.PreviousDepartureMissing(line.LocalDay, today),
            null, null, new PreviousDayDto(AttendanceLockRules.DayKey(line.LocalDay), AttendanceRules.LocalTime(line.ArrivalUtc),
                lastSave is { } saved ? AttendanceRules.LocalTime(saved) : null));
    }

    private async Task<DateTime?> LastSaveOnAsync(Guid clinicId, string userId, DateOnly day, CancellationToken cancellationToken)
    {
        var (from, to) = ClinicClock.LocalDayRangeUtc(day.ToDateTime(TimeOnly.MinValue));
        return await _journal.GetLastSaveAtAsync(clinicId, userId, from, to, AttendanceLockRules.NotWorkInTheJournal, cancellationToken);
    }

    /// <summary>A right schéma resets her counter even when the arrival then waits for a declared hour.</summary>
    private async Task SaveVerifiedSchemaAsync(User person, CancellationToken cancellationToken)
    {
        _users.Update(person);
        _auditActor.AuthenticatedAs(person.Id, person.Email);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<(string Error, string Code)?> SubscriptionRefusalAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        if (!_subscriptionPolicy.RequiresSubscription)
            return null;
        var subscription = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);
        if (subscription is null)
            return (SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);

        var status = SubscriptionStateReader.Read(subscription, ClinicClock.ClinicToday());
        return status.AllowsWrites ? null : SubscriptionRefusals.For(status);
    }

    /// <summary>The pipeline broadcasts nothing for an anonymous request, so the punch reaches the admin's sheet here.</summary>
    private async Task BroadcastAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        try
        {
            await _realtime.NotifyEntityChangedAsync(
                clinicId, RealtimeResourceResolver.Resolve(typeof(PunchCommand))!, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pointage: broadcast failed for clinic {ClinicId}", clinicId);
        }
    }
}
