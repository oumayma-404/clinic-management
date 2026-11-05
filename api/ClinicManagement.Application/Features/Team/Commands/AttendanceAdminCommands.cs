using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Commands;

/// <summary>« Retirer »: the borne stops being one at its next punch. Its lines keep its name.</summary>
public class RevokeAttendanceTerminalCommand : IRequest<Result<AttendanceTerminalDto>>
{
    public Guid Id { get; set; }
}

public class RevokeAttendanceTerminalCommandHandler : IRequestHandler<RevokeAttendanceTerminalCommand, Result<AttendanceTerminalDto>>
{
    private readonly IAttendanceTerminalRepository _terminals;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public RevokeAttendanceTerminalCommandHandler(
        IAttendanceTerminalRepository terminals, IUserRepository users, IClinicRepository clinics,
        IClinicContext clinicContext, IUnitOfWork unitOfWork)
    {
        _terminals = terminals;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AttendanceTerminalDto>> Handle(RevokeAttendanceTerminalCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: true, cancellationToken);
            if (resolved.IsFailure)
                return Result<AttendanceTerminalDto>.FailureFrom(resolved);
            var (_, clinic) = resolved.Value;

            var terminal = await _terminals.GetByIdAsync(request.Id, cancellationToken);
            if (terminal == null || terminal.ClinicId != clinic.Id)
                return Result<AttendanceTerminalDto>.Failure(AttendanceRules.TerminalNotFound, AttendanceRules.NotFoundCode);

            terminal.Revoke(DateTime.UtcNow);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<AttendanceTerminalDto>.Success(AttendanceRules.ToDto(terminal));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<AttendanceTerminalDto>.Failure("Erreur lors du retrait de la borne.", ex);
        }
    }
}

/// <summary>« Réinitialiser le schéma »: she draws a new one; a lock from wrong attempts is lifted.</summary>
public class ResetAttendanceSchemaCommand : IRequest<Result<AttendancePersonSettingDto>>
{
    public string UserId { get; set; } = string.Empty;
}

public class ResetAttendanceSchemaCommandHandler : IRequestHandler<ResetAttendanceSchemaCommand, Result<AttendancePersonSettingDto>>
{
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public ResetAttendanceSchemaCommandHandler(
        IUserRepository users, IClinicRepository clinics, IClinicContext clinicContext, IUnitOfWork unitOfWork)
    {
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AttendancePersonSettingDto>> Handle(ResetAttendanceSchemaCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: true, cancellationToken);
            if (resolved.IsFailure)
                return Result<AttendancePersonSettingDto>.FailureFrom(resolved);
            var (_, clinic) = resolved.Value;

            var target = await _users.GetByIdAsync(request.UserId, cancellationToken);
            if (target == null || target.ClinicId != clinic.Id)
                return Result<AttendancePersonSettingDto>.Failure(StaffLeaveRules.AccountNotFound, StaffLeaveRules.NotFoundCode);

            target.ResetAttendanceSchema();
            _users.Update(target);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<AttendancePersonSettingDto>.Success(AttendanceRules.ToSetting(target, DateTime.UtcNow));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<AttendancePersonSettingDto>.Failure("Erreur lors de la réinitialisation du schéma.", ex);
        }
    }
}

/// <summary>
/// « Personnes qui pointent »: tick or untick an account. Null follows the role again. The screen sends version 0: the
/// account row is rewritten by every sign-in, so a token would refuse an admin's edit for a save nobody made.
/// </summary>
public class SetAttendanceProfileCommand : IRequest<Result<AttendancePersonSettingDto>>
{
    public string UserId { get; set; } = string.Empty;
    public bool? Tracked { get; set; }

    /// <summary>The account's version as the admin read it; 0 skips the check.</summary>
    public uint Version { get; set; }
}

public class SetAttendanceProfileCommandHandler : IRequestHandler<SetAttendanceProfileCommand, Result<AttendancePersonSettingDto>>
{
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public SetAttendanceProfileCommandHandler(
        IUserRepository users, IClinicRepository clinics, IClinicContext clinicContext, IUnitOfWork unitOfWork)
    {
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AttendancePersonSettingDto>> Handle(SetAttendanceProfileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: true, cancellationToken);
            if (resolved.IsFailure)
                return Result<AttendancePersonSettingDto>.FailureFrom(resolved);
            var (_, clinic) = resolved.Value;

            var target = await _users.GetByIdAsync(request.UserId, cancellationToken);
            if (target == null || target.ClinicId != clinic.Id)
                return Result<AttendancePersonSettingDto>.Failure(StaffLeaveRules.AccountNotFound, StaffLeaveRules.NotFoundCode);

            // An override equal to what the role already says is stored as « follow the role ».
            var tracked = request.Tracked is { } value && value == target.IsSecretary() ? null : request.Tracked;
            target.SetAttendanceTracked(tracked);
            _unitOfWork.SetExpectedVersion(target, request.Version);
            _users.Update(target);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<AttendancePersonSettingDto>.Success(AttendanceRules.ToSetting(target, DateTime.UtcNow));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<AttendancePersonSettingDto>.Failure("Erreur lors de l'enregistrement.", ex);
        }
    }
}

/// <summary>« Corriger »: the hours that count, with a motif. What the borne recorded stays visible beside them.</summary>
public class CorrectAttendanceEntryCommand : IRequest<Result<AttendanceEntryDto>>
{
    public Guid Id { get; set; }

    /// <summary>« 08:30 », cabinet-local, on the line's own day.</summary>
    public string Arrival { get; set; } = string.Empty;
    public string? Departure { get; set; }
    public string? Reason { get; set; }
    public uint Version { get; set; }
}

public class CorrectAttendanceEntryCommandHandler : IRequestHandler<CorrectAttendanceEntryCommand, Result<AttendanceEntryDto>>
{
    private readonly IAttendanceEntryRepository _entries;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public CorrectAttendanceEntryCommandHandler(
        IAttendanceEntryRepository entries, IUserRepository users, IClinicRepository clinics,
        IClinicContext clinicContext, IUnitOfWork unitOfWork)
    {
        _entries = entries;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AttendanceEntryDto>> Handle(CorrectAttendanceEntryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: true, cancellationToken);
            if (resolved.IsFailure)
                return Result<AttendanceEntryDto>.FailureFrom(resolved);
            var (admin, clinic) = resolved.Value;

            var entry = await _entries.GetByIdAsync(request.Id, cancellationToken);
            if (entry == null || entry.ClinicId != clinic.Id)
                return Result<AttendanceEntryDto>.Failure(AttendanceRules.EntryNotFound, AttendanceRules.NotFoundCode);

            var nowUtc = DateTime.UtcNow;
            var hours = AttendanceRules.HoursRefusalFor(entry.LocalDay, request.Arrival, request.Departure, request.Reason, nowUtc);
            if (hours.IsFailure)
                return Result<AttendanceEntryDto>.FailureFrom(hours);
            var (arrival, departure) = hours.Value;
            var sameDay = await _entries.ListBetweenAsync(clinic.Id, entry.LocalDay, entry.LocalDay, entry.UserId, cancellationToken);
            if (AttendanceRules.OverlapRefusalFor(sameDay, arrival, departure, entry.Id) is { } overlap)
                return Result<AttendanceEntryDto>.Failure(overlap);

            var roster = await StaffLeaveRules.RosterAsync(_users, clinic.Id, cancellationToken);
            entry.Correct(arrival, departure, request.Reason!, admin.Id, nowUtc);
            _unitOfWork.SetExpectedVersion(entry, request.Version);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<AttendanceEntryDto>.Success(AttendanceRules.ToDto(entry, roster, AttendanceRules.Today(nowUtc)));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<AttendanceEntryDto>.Failure("Erreur lors de la correction du pointage.", ex);
        }
    }
}

/// <summary>« Ajouter un pointage »: a day the borne never saw (borne en panne, oubli), entered with a motif.</summary>
public class RecordAttendanceEntryCommand : IRequest<Result<AttendanceEntryDto>>
{
    public string UserId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }
    public string Arrival { get; set; } = string.Empty;
    public string? Departure { get; set; }
    public string? Reason { get; set; }
}

public class RecordAttendanceEntryCommandHandler : IRequestHandler<RecordAttendanceEntryCommand, Result<AttendanceEntryDto>>
{
    private readonly IAttendanceEntryRepository _entries;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public RecordAttendanceEntryCommandHandler(
        IAttendanceEntryRepository entries, IUserRepository users, IClinicRepository clinics,
        IClinicContext clinicContext, IUnitOfWork unitOfWork)
    {
        _entries = entries;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AttendanceEntryDto>> Handle(RecordAttendanceEntryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: true, cancellationToken);
            if (resolved.IsFailure)
                return Result<AttendanceEntryDto>.FailureFrom(resolved);
            var (admin, clinic) = resolved.Value;

            var roster = await StaffLeaveRules.RosterAsync(_users, clinic.Id, cancellationToken);
            if (string.IsNullOrWhiteSpace(request.UserId) || !roster.TryGetValue(request.UserId, out var target))
                return Result<AttendanceEntryDto>.Failure(StaffLeaveRules.AccountNotFound, StaffLeaveRules.NotFoundCode);

            var nowUtc = DateTime.UtcNow;
            if (request.Day == default || request.Day > AttendanceRules.Today(nowUtc))
                return Result<AttendanceEntryDto>.Failure(AttendanceRules.DayInFuture);
            var hours = AttendanceRules.HoursRefusalFor(request.Day, request.Arrival, request.Departure, request.Reason, nowUtc);
            if (hours.IsFailure)
                return Result<AttendanceEntryDto>.FailureFrom(hours);
            var (arrival, departure) = hours.Value;
            var sameDay = await _entries.ListBetweenAsync(clinic.Id, request.Day, request.Day, target.Id, cancellationToken);
            if (AttendanceRules.OverlapRefusalFor(sameDay, arrival, departure) is { } overlap)
                return Result<AttendanceEntryDto>.Failure(overlap);

            var entry = AttendanceEntry.RecordByAdmin(
                Guid.NewGuid(), clinic.Id, target.Id, request.Day, arrival, departure, request.Reason!, admin.Id, nowUtc);
            await _entries.AddAsync(entry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<AttendanceEntryDto>.Success(AttendanceRules.ToDto(entry, roster, AttendanceRules.Today(nowUtc)));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<AttendanceEntryDto>.Failure("Erreur lors de l'ajout du pointage.", ex);
        }
    }
}
