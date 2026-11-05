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
/// « Faire de cet appareil une borne »: an admin, step-up already spent by the controller, names the device. The
/// secret goes back to the controller for the cookie and is stored only as its SHA-256.
/// </summary>
public class RegisterAttendanceTerminalCommand : IRequest<Result<RegisteredTerminalDto>>
{
    public string? Name { get; set; }

    /// <summary>The borne this device already is (read from its status), retired since its secret is about to be lost.</summary>
    public Guid? ReplacingTerminalId { get; set; }
}

public class RegisterAttendanceTerminalCommandHandler
    : IRequestHandler<RegisterAttendanceTerminalCommand, Result<RegisteredTerminalDto>>
{
    private readonly IAttendanceTerminalRepository _terminals;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterAttendanceTerminalCommandHandler(
        IAttendanceTerminalRepository terminals, IUserRepository users, IClinicRepository clinics,
        IClinicContext clinicContext, IUnitOfWork unitOfWork)
    {
        _terminals = terminals;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
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
            if (request.ReplacingTerminalId is { } replacingId
                && await _terminals.GetByIdAsync(replacingId, cancellationToken) is { } previous
                && previous.ClinicId == clinic.Id)
            {
                previous.Revoke(now);
            }

            var secret = AttendanceRules.NewTerminalSecret();
            var terminal = AttendanceTerminal.Register(
                Guid.NewGuid(), clinic.Id, request.Name!, SessionCredential.Hash(secret), caller.Id, now);
            await _terminals.AddAsync(terminal, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

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
}

/// <summary>Either the punch, or a schéma refusal carrying what the borne shows (tries left, lock end).</summary>
public sealed record PunchOutcome(PunchResultDto? Done, PunchRefusal? Refused);

public sealed record PunchRefusal(string Code, string Error, int? AttemptsLeft, DateTime? LockedUntil);

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
    private readonly IRealtimeNotifier _realtime;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PunchCommandHandler> _logger;

    public PunchCommandHandler(
        IAttendanceTerminalRepository terminals, IAttendanceEntryRepository entries, IStaffLeaveRequestRepository leaves,
        IUserRepository users, IClinicRepository clinics, IClinicSubscriptionRepository subscriptions,
        ISubscriptionPolicy subscriptionPolicy, ITenantScope tenantScope, ILocalAuthService hasher,
        IAuditActorProvider auditActor, IRealtimeNotifier realtime, IUnitOfWork unitOfWork,
        ILogger<PunchCommandHandler> logger)
    {
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
                if (outcome.IsSuccess && outcome.Value!.Done is not null)
                    await BroadcastAsync(terminal.ClinicId, cancellationToken);
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

        if (verdict == PasswordVerificationOutcome.SuccessNeedsRehash)
            person.SetAttendanceSchema(_hasher.HashPassword(AttendanceRules.PatternText(request.Pattern!)));
        else if (person.SchemaFailedAttempts > 0)
            person.ClearSchemaFailures();

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

        return locked
            ? Refused(AttendanceRules.SchemaLockedCode, AttendanceRules.SchemaLocked(person.SchemaLockedUntilUtc!.Value, now),
                null, person.SchemaLockedUntilUtc)
            : Refused(AttendanceRules.SchemaWrongCode,
                AttendanceRules.SchemaWrong(User.MaxSchemaFailures - person.SchemaFailedAttempts),
                User.MaxSchemaFailures - person.SchemaFailedAttempts, null);
    }

    private static Result<PunchOutcome> Refused(string code, string error, int? attemptsLeft, DateTime? lockedUntil) =>
        Result<PunchOutcome>.Success(new PunchOutcome(null, new PunchRefusal(code, error, attemptsLeft, lockedUntil)));

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
