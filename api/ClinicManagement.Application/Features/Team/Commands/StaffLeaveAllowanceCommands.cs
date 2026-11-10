using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Commands;

/// <summary>
/// « Droit à congé » (admin): a rule, a starting balance, days added or removed, a line cancelled. Each appends to her
/// lines and answers her balance for the current year, folded again — nothing derived is ever written.
/// </summary>
public static class StaffLeaveAllowanceWrites
{
    /// <summary>How far back or ahead a date here may be: wider is a mistyped year, not a decision.</summary>
    public const int YearsAround = 5;

    public const string EntryCancelledCode = "leave_allowance_entry_cancelled";
    public const string EntryCancelled = "Cette ligne est déjà annulée.";
    public const string EntryNotFound = "Ligne introuvable.";
    public const string FutureDay = "La date ne peut pas être dans le futur.";
    public const string TooFar = "Vérifiez l'année saisie.";
    public const string AccrualRequired = "Choisissez « par an » ou « par mois ».";
    public const string CancelReasonRequired = "Indiquez le motif de l'annulation.";

    /// <summary>The admin, the cabinet (congés on) and the account she is writing for, or the refusal.</summary>
    public static async Task<Result<(User Admin, Clinic Clinic, User Target)>> ResolveAsync(
        IClinicContext clinicContext, IUserRepository users, IClinicRepository clinics, string userId,
        CancellationToken cancellationToken)
    {
        var resolved = await StaffLeaveRules.ResolveCallerAsync(clinicContext, users, cancellationToken);
        if (resolved.IsFailure)
            return Result<(User, Clinic, User)>.FailureFrom(resolved);
        var admin = resolved.Value!;
        if (!admin.IsAdmin())
            return Result<(User, Clinic, User)>.Failure(StaffLeaveRules.AdminOnly);
        var clinic = await clinics.GetByIdAsync(admin.ClinicId, cancellationToken);
        if (clinic is not { LeaveRequestsEnabled: true })
            return Result<(User, Clinic, User)>.Failure(StaffLeaveRules.ModuleOff);
        var target = await users.GetByIdAsync(userId, cancellationToken);
        if (target == null || target.ClinicId != admin.ClinicId)
            return Result<(User, Clinic, User)>.Failure(StaffLeaveRules.AccountNotFound, StaffLeaveRules.NotFoundCode);
        return Result<(User, Clinic, User)>.Success((admin, clinic, target));
    }

    /// <summary>A day in the cabinet's reach: not after today when <paramref name="pastOnly"/>, and within a few years.</summary>
    public static string? DayRefusalFor(DateOnly day, DateOnly today, bool pastOnly)
    {
        if (pastOnly && day > today)
            return FutureDay;
        return Math.Abs(day.Year - today.Year) > YearsAround ? TooFar : null;
    }

    public static async Task<StaffLeaveBalanceDto> BalanceAsync(
        IStaffLeaveAllowanceRepository allowances, IStaffLeaveRequestRepository requests, Clinic clinic, User target,
        CancellationToken cancellationToken)
    {
        var entries = await allowances.GetForUserAsync(clinic.Id, target.Id, cancellationToken);
        var history = await requests.GetByUserAsync(clinic.Id, target.Id, cancellationToken);
        var today = StaffLeaveRules.ClinicToday();
        return StaffLeaveAccounts.Balance(
            StaffLeaveAccounts.Fold(entries, history, clinic.WorkingHoursJson, today), today.Year);
    }
}

/// <summary>« 20 j par an » / « 2 j par mois » from a 1st; a later rule supersedes this one from its own day.</summary>
public class SetLeaveRuleCommand : IRequest<Result<StaffLeaveBalanceDto>>
{
    public string UserId { get; set; } = string.Empty;
    public decimal Days { get; set; }

    /// <summary>« Year » or « Month ».</summary>
    public string? Accrual { get; set; }

    /// <summary>False: days not taken by 31 December are lost.</summary>
    public bool CarryOver { get; set; }

    /// <summary>The first credit day: 1 January (par an) or the 1st of a month (par mois).</summary>
    public DateOnly Since { get; set; }
}

public class SetLeaveRuleCommandHandler : IRequestHandler<SetLeaveRuleCommand, Result<StaffLeaveBalanceDto>>
{
    private readonly IStaffLeaveAllowanceRepository _allowances;
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public SetLeaveRuleCommandHandler(
        IStaffLeaveAllowanceRepository allowances, IStaffLeaveRequestRepository requests, IUserRepository users,
        IClinicRepository clinics, IClinicContext clinicContext, IUnitOfWork unitOfWork)
    {
        _allowances = allowances;
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<StaffLeaveBalanceDto>> Handle(SetLeaveRuleCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveAllowanceWrites.ResolveAsync(_clinicContext, _users, _clinics, request.UserId, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveBalanceDto>.FailureFrom(resolved);
            var (admin, clinic, target) = resolved.Value;

            if (!Enum.TryParse<StaffLeaveAccrual>(request.Accrual, ignoreCase: true, out var accrual) || !Enum.IsDefined(accrual))
                return Result<StaffLeaveBalanceDto>.Failure(StaffLeaveAllowanceWrites.AccrualRequired);
            var refusal = StaffLeaveAllowanceEntry.RuleRefusalFor(request.Days, accrual, request.Since)
                          ?? StaffLeaveAllowanceWrites.DayRefusalFor(request.Since, StaffLeaveRules.ClinicToday(), pastOnly: false);
            if (refusal is not null)
                return Result<StaffLeaveBalanceDto>.Failure(refusal);

            await _allowances.AddAsync(StaffLeaveAllowanceEntry.Rule(
                Guid.NewGuid(), clinic.Id, target.Id, request.Days, accrual, request.CarryOver, request.Since,
                admin.Id, StaffLeaveRules.DisplayName(admin), DateTime.UtcNow), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<StaffLeaveBalanceDto>.Success(
                await StaffLeaveAllowanceWrites.BalanceAsync(_allowances, _requests, clinic, target, cancellationToken));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveBalanceDto>.Failure("Erreur lors de l'enregistrement du droit à congé.", ex);
        }
    }
}

/// <summary>« Il lui reste 10 j au 06/11/2026 » — what she has left on that day; everything before it is inside the figure.</summary>
public class SetOpeningLeaveBalanceCommand : IRequest<Result<StaffLeaveBalanceDto>>
{
    public string UserId { get; set; } = string.Empty;
    public decimal Days { get; set; }
    public DateOnly OnDay { get; set; }
}

public class SetOpeningLeaveBalanceCommandHandler : IRequestHandler<SetOpeningLeaveBalanceCommand, Result<StaffLeaveBalanceDto>>
{
    private readonly IStaffLeaveAllowanceRepository _allowances;
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public SetOpeningLeaveBalanceCommandHandler(
        IStaffLeaveAllowanceRepository allowances, IStaffLeaveRequestRepository requests, IUserRepository users,
        IClinicRepository clinics, IClinicContext clinicContext, IUnitOfWork unitOfWork)
    {
        _allowances = allowances;
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<StaffLeaveBalanceDto>> Handle(SetOpeningLeaveBalanceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveAllowanceWrites.ResolveAsync(_clinicContext, _users, _clinics, request.UserId, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveBalanceDto>.FailureFrom(resolved);
            var (admin, clinic, target) = resolved.Value;

            var refusal = StaffLeaveAllowanceEntry.OpeningRefusalFor(request.Days)
                          ?? StaffLeaveAllowanceWrites.DayRefusalFor(request.OnDay, StaffLeaveRules.ClinicToday(), pastOnly: true);
            if (refusal is not null)
                return Result<StaffLeaveBalanceDto>.Failure(refusal);

            await _allowances.AddAsync(StaffLeaveAllowanceEntry.Opening(
                Guid.NewGuid(), clinic.Id, target.Id, request.Days, request.OnDay, admin.Id,
                StaffLeaveRules.DisplayName(admin), DateTime.UtcNow), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<StaffLeaveBalanceDto>.Success(
                await StaffLeaveAllowanceWrites.BalanceAsync(_allowances, _requests, clinic, target, cancellationToken));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveBalanceDto>.Failure("Erreur lors de l'enregistrement du solde de départ.", ex);
        }
    }
}

/// <summary>Days added (positive) or removed (negative) once, with a motif.</summary>
public class AdjustLeaveBalanceCommand : IRequest<Result<StaffLeaveBalanceDto>>
{
    public string UserId { get; set; } = string.Empty;
    public decimal Days { get; set; }
    public DateOnly OnDay { get; set; }
    public string? Reason { get; set; }
}

public class AdjustLeaveBalanceCommandHandler : IRequestHandler<AdjustLeaveBalanceCommand, Result<StaffLeaveBalanceDto>>
{
    private readonly IStaffLeaveAllowanceRepository _allowances;
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public AdjustLeaveBalanceCommandHandler(
        IStaffLeaveAllowanceRepository allowances, IStaffLeaveRequestRepository requests, IUserRepository users,
        IClinicRepository clinics, IClinicContext clinicContext, IUnitOfWork unitOfWork)
    {
        _allowances = allowances;
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<StaffLeaveBalanceDto>> Handle(AdjustLeaveBalanceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveAllowanceWrites.ResolveAsync(_clinicContext, _users, _clinics, request.UserId, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveBalanceDto>.FailureFrom(resolved);
            var (admin, clinic, target) = resolved.Value;

            var refusal = StaffLeaveAllowanceEntry.AdjustmentRefusalFor(request.Days, request.Reason)
                          ?? StaffLeaveAllowanceWrites.DayRefusalFor(request.OnDay, StaffLeaveRules.ClinicToday(), pastOnly: true);
            if (refusal is not null)
                return Result<StaffLeaveBalanceDto>.Failure(refusal);

            await _allowances.AddAsync(StaffLeaveAllowanceEntry.Adjustment(
                Guid.NewGuid(), clinic.Id, target.Id, request.Days, request.OnDay, request.Reason!, admin.Id,
                StaffLeaveRules.DisplayName(admin), DateTime.UtcNow), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<StaffLeaveBalanceDto>.Success(
                await StaffLeaveAllowanceWrites.BalanceAsync(_allowances, _requests, clinic, target, cancellationToken));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveBalanceDto>.Failure("Erreur lors de l'ajustement du solde.", ex);
        }
    }
}

/// <summary>Strikes a line through with a motif; every balance it fed is folded again on the next read.</summary>
public class CancelLeaveAllowanceEntryCommand : IRequest<Result<StaffLeaveBalanceDto>>
{
    public Guid EntryId { get; set; }
    public string? Reason { get; set; }
}

public class CancelLeaveAllowanceEntryCommandHandler : IRequestHandler<CancelLeaveAllowanceEntryCommand, Result<StaffLeaveBalanceDto>>
{
    private readonly IStaffLeaveAllowanceRepository _allowances;
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public CancelLeaveAllowanceEntryCommandHandler(
        IStaffLeaveAllowanceRepository allowances, IStaffLeaveRequestRepository requests, IUserRepository users,
        IClinicRepository clinics, IClinicContext clinicContext, IUnitOfWork unitOfWork)
    {
        _allowances = allowances;
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<StaffLeaveBalanceDto>> Handle(CancelLeaveAllowanceEntryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await _allowances.GetByIdAsync(request.EntryId, cancellationToken);
            if (entry is null)
                return Result<StaffLeaveBalanceDto>.Failure(StaffLeaveAllowanceWrites.EntryNotFound, StaffLeaveRules.NotFoundCode);

            var resolved = await StaffLeaveAllowanceWrites.ResolveAsync(_clinicContext, _users, _clinics, entry.UserId, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveBalanceDto>.FailureFrom(resolved);
            var (admin, clinic, target) = resolved.Value;
            if (entry.ClinicId != clinic.Id)
                return Result<StaffLeaveBalanceDto>.Failure(StaffLeaveAllowanceWrites.EntryNotFound, StaffLeaveRules.NotFoundCode);
            if (entry.IsCancelled)
                return Result<StaffLeaveBalanceDto>.Failure(StaffLeaveAllowanceWrites.EntryCancelled, StaffLeaveAllowanceWrites.EntryCancelledCode);
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Result<StaffLeaveBalanceDto>.Failure(StaffLeaveAllowanceWrites.CancelReasonRequired);

            entry.Cancel(request.Reason, StaffLeaveRules.DisplayName(admin), DateTime.UtcNow);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<StaffLeaveBalanceDto>.Success(
                await StaffLeaveAllowanceWrites.BalanceAsync(_allowances, _requests, clinic, target, cancellationToken));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveBalanceDto>.Failure("Erreur lors de l'annulation de la ligne.", ex);
        }
    }
}
