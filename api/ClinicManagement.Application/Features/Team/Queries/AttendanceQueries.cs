using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Csv;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>Paramètres → « Équipe »: the cabinet's bornes and who clocks in.</summary>
public class GetAttendanceSettingsQuery : IRequest<Result<AttendanceSettingsDto>>
{
}

public class GetAttendanceSettingsQueryHandler : IRequestHandler<GetAttendanceSettingsQuery, Result<AttendanceSettingsDto>>
{
    private readonly IAttendanceTerminalRepository _terminals;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public GetAttendanceSettingsQueryHandler(
        IAttendanceTerminalRepository terminals, IUserRepository users, IClinicRepository clinics, IClinicContext clinicContext)
    {
        _terminals = terminals;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
    }

    public async Task<Result<AttendanceSettingsDto>> Handle(GetAttendanceSettingsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: true, cancellationToken);
            if (resolved.IsFailure)
                return Result<AttendanceSettingsDto>.FailureFrom(resolved);
            var (_, clinic) = resolved.Value;

            var terminals = await _terminals.ListAsync(clinic.Id, cancellationToken);
            var roster = await StaffLeaveRules.RosterAsync(_users, clinic.Id, cancellationToken);
            var nowUtc = DateTime.UtcNow;
            return Result<AttendanceSettingsDto>.Success(new AttendanceSettingsDto
            {
                Terminals = terminals.Select(AttendanceRules.ToDto).ToList(),
                People = roster.Values
                    .OrderByDescending(u => u.IsActive)
                    .ThenBy(u => StaffLeaveRules.DisplayName(u), StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(u => u.Id, StringComparer.Ordinal)
                    .Select(u => AttendanceRules.ToSetting(u, nowUtc))
                    .ToList(),
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<AttendanceSettingsDto>.Failure("Erreur lors du chargement des réglages du pointage.", ex);
        }
    }
}

/// <summary>The month sheet for the admin — the whole team, or one secretary for her file.</summary>
public class GetAttendanceSheetQuery : IRequest<Result<AttendanceSheetDto>>
{
    /// <summary>« 2026-11 »; the current cabinet month when absent.</summary>
    public string? Month { get; set; }
    public string? UserId { get; set; }
}

public class GetAttendanceSheetQueryHandler : IRequestHandler<GetAttendanceSheetQuery, Result<AttendanceSheetDto>>
{
    private readonly IAttendanceEntryRepository _entries;
    private readonly IStaffLeaveRequestRepository _leaves;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public GetAttendanceSheetQueryHandler(
        IAttendanceEntryRepository entries, IStaffLeaveRequestRepository leaves, IUserRepository users,
        IClinicRepository clinics, IClinicContext clinicContext)
    {
        _entries = entries;
        _leaves = leaves;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
    }

    public async Task<Result<AttendanceSheetDto>> Handle(GetAttendanceSheetQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: true, cancellationToken);
            if (resolved.IsFailure)
                return Result<AttendanceSheetDto>.FailureFrom(resolved);
            var (_, clinic) = resolved.Value;

            var sheet = await AttendanceSheetReader.ReadAsync(
                _entries, _leaves, _users, clinic, request.Month, request.UserId, cancellationToken);
            return sheet;
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<AttendanceSheetDto>.Failure("Erreur lors du chargement des présences.", ex);
        }
    }
}

/// <summary>« Exporter »: the month as CSV, one line per pointage, the borne's own hours beside the ones that count.</summary>
public class ExportAttendanceQuery : IRequest<Result<CsvTable>>
{
    public string? Month { get; set; }
}

public class ExportAttendanceQueryHandler : IRequestHandler<ExportAttendanceQuery, Result<CsvTable>>
{
    private readonly IAttendanceEntryRepository _entries;
    private readonly IStaffLeaveRequestRepository _leaves;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public ExportAttendanceQueryHandler(
        IAttendanceEntryRepository entries, IStaffLeaveRequestRepository leaves, IUserRepository users,
        IClinicRepository clinics, IClinicContext clinicContext)
    {
        _entries = entries;
        _leaves = leaves;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
    }

    public async Task<Result<CsvTable>> Handle(ExportAttendanceQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: true, cancellationToken);
            if (resolved.IsFailure)
                return Result<CsvTable>.FailureFrom(resolved);
            var (_, clinic) = resolved.Value;

            var read = await AttendanceSheetReader.ReadAsync(
                _entries, _leaves, _users, clinic, request.Month, null, cancellationToken);
            if (read.IsFailure)
                return Result<CsvTable>.FailureFrom(read);
            var sheet = read.Value!;

            var table = CsvTable.Create(
                "Jour", "Personne", "Arrivée", "Départ", "Durée (min)", "État", "Borne", "Arrivée borne", "Départ borne",
                "Motif de correction");
            foreach (var day in sheet.Days)
            {
                foreach (var entry in day.Entries)
                {
                    table.Row(
                        CsvCell.CalendarDay(day.Day.ToDateTime(TimeOnly.MinValue)),
                        entry.UserName,
                        entry.Arrival,
                        entry.Departure,
                        CsvCell.Number(entry.Minutes),
                        entry.DepartureMissing ? "Départ non pointé" : entry.IsPresent ? "Présente" : entry.IsCorrected ? "Corrigé" : string.Empty,
                        entry.TerminalName,
                        entry.PunchedArrival,
                        entry.PunchedDeparture,
                        entry.CorrectionReason);
                }
            }
            return Result<CsvTable>.Success(table);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<CsvTable>.Failure("Erreur lors de l'export des présences.", ex);
        }
    }
}

/// <summary>« Mes présences » and Mon profil's schéma card: her own month, read-only.</summary>
public class GetMyAttendanceQuery : IRequest<Result<MyAttendanceDto>>
{
    public string? Month { get; set; }
}

public class GetMyAttendanceQueryHandler : IRequestHandler<GetMyAttendanceQuery, Result<MyAttendanceDto>>
{
    private readonly IAttendanceEntryRepository _entries;
    private readonly IStaffLeaveRequestRepository _leaves;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public GetMyAttendanceQueryHandler(
        IAttendanceEntryRepository entries, IStaffLeaveRequestRepository leaves, IUserRepository users,
        IClinicRepository clinics, IClinicContext clinicContext)
    {
        _entries = entries;
        _leaves = leaves;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
    }

    public async Task<Result<MyAttendanceDto>> Handle(GetMyAttendanceQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: false, cancellationToken);
            if (resolved.IsFailure)
                return Result<MyAttendanceDto>.FailureFrom(resolved);
            var (caller, clinic) = resolved.Value;

            var sheet = await AttendanceSheetReader.ReadAsync(
                _entries, _leaves, _users, clinic, request.Month, caller.Id, cancellationToken);
            if (sheet.IsFailure)
                return Result<MyAttendanceDto>.FailureFrom(sheet);

            return Result<MyAttendanceDto>.Success(new MyAttendanceDto
            {
                Tracked = caller.IsAttendanceTracked,
                HasSchema = caller.HasAttendanceSchema,
                Sheet = sheet.Value!,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<MyAttendanceDto>.Failure("Erreur lors du chargement de vos présences.", ex);
        }
    }
}

/// <summary>« Équipe aujourd'hui »: who is here and since when, who is on congé, and what waits for the admin.</summary>
public class GetTeamTodayQuery : IRequest<Result<TeamTodayDto>>
{
}

public class GetTeamTodayQueryHandler : IRequestHandler<GetTeamTodayQuery, Result<TeamTodayDto>>
{
    private readonly IAttendanceEntryRepository _entries;
    private readonly IStaffLeaveRequestRepository _leaves;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public GetTeamTodayQueryHandler(
        IAttendanceEntryRepository entries, IStaffLeaveRequestRepository leaves, IUserRepository users,
        IClinicRepository clinics, IClinicContext clinicContext)
    {
        _entries = entries;
        _leaves = leaves;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
    }

    public async Task<Result<TeamTodayDto>> Handle(GetTeamTodayQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: true, cancellationToken);
            if (resolved.IsFailure)
                return Result<TeamTodayDto>.FailureFrom(resolved);
            var (_, clinic) = resolved.Value;

            var today = AttendanceRules.Today();
            var roster = await StaffLeaveRules.RosterAsync(_users, clinic.Id, cancellationToken);
            var people = roster.Values.Where(u => u.IsActive && u.IsAttendanceTracked).ToList();
            // Open lines from earlier days surface as « départ non pointé »; a fortnight back is plenty for a desk read.
            var entries = await _entries.ListBetweenAsync(clinic.Id, today.AddDays(-14), today, null, cancellationToken);
            var leaves = await _leaves.GetApprovedOverlappingAsync(clinic.Id, today, today, cancellationToken);

            var team = people
                .Select(u => TeamTodayRules.PersonToday(u, entries, leaves, today))
                .OrderBy(p => p.State switch { AttendanceRules.StateIn => 0, AttendanceRules.StateOut => 1, _ => 2 })
                .ThenBy(p => p.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.UserId, StringComparer.Ordinal)
                .ToList();

            return Result<TeamTodayDto>.Success(new TeamTodayDto
            {
                Day = today,
                People = team,
                PendingLeaveCount = clinic.LeaveRequestsEnabled
                    ? await _leaves.CountPendingAsync(clinic.Id, cancellationToken)
                    : null,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<TeamTodayDto>.Failure("Erreur lors du chargement de l'équipe du jour.", ex);
        }
    }
}

/// <summary>The month read shared by the sheet, the export and « Mes présences ».</summary>
internal static class AttendanceSheetReader
{
    public static async Task<Result<AttendanceSheetDto>> ReadAsync(
        IAttendanceEntryRepository entries, IStaffLeaveRequestRepository leaves, IUserRepository users, Clinic clinic,
        string? month, string? userId, CancellationToken cancellationToken)
    {
        var monthKey = string.IsNullOrWhiteSpace(month) ? ClinicClock.CurrentMonthKey() : month.Trim();
        if (AttendanceRules.MonthDays(monthKey) is not { } range)
            return Result<AttendanceSheetDto>.Failure(AttendanceRules.MonthInvalid);

        var roster = await StaffLeaveRules.RosterAsync(users, clinic.Id, cancellationToken);
        if (userId is not null && !roster.ContainsKey(userId))
            return Result<AttendanceSheetDto>.Failure(StaffLeaveRules.AccountNotFound, StaffLeaveRules.NotFoundCode);

        var lines = await entries.ListBetweenAsync(clinic.Id, range.From, range.To, userId, cancellationToken);
        var accepted = await leaves.GetApprovedOverlappingAsync(clinic.Id, range.From, range.To, cancellationToken);
        var people = userId is not null
            ? new List<User> { roster[userId] }
            : AttendanceSheetBuilder.PeopleFor(roster, lines);

        return Result<AttendanceSheetDto>.Success(AttendanceSheetBuilder.Build(
            monthKey, range.From, range.To, people, lines, accepted, roster, clinic.WorkingHoursJson, AttendanceRules.Today()));
    }
}
