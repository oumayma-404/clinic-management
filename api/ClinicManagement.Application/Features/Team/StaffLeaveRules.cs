using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Team;

/// <summary>
/// The secretary congés' French sentences, labels, day count and yearly balance — one owner for every command,
/// query, notification and e-mail, so the figure a secretary reads is the figure her admin reads.
/// </summary>
public static class StaffLeaveRules
{
    /// <summary>A request or an account that is not this cabinet's, or does not exist — answered 404.</summary>
    public const string NotFoundCode = "staff_leave_not_found";

    public const string RequestNotFound = "Demande de congé introuvable.";
    public const string AccountNotFound = "Compte introuvable.";
    public const string ModuleOff = "Les demandes de congé ne sont pas activées pour ce cabinet.";
    public const string SecretaryOnly = "Les demandes de congé sont réservées aux comptes secrétaire.";
    public const string AdminOnly = "Seul un administrateur du cabinet peut décider d'un congé.";
    public const string NotOwnRequest = "Vous ne pouvez annuler que vos propres demandes.";
    public const string NotPending = "Cette demande a déjà reçu une réponse.";
    public const string AlreadyCancelled = "Cette demande est déjà annulée.";
    public const string AlreadyStarted = "Ce congé a commencé ou est passé : il ne peut plus être annulé.";
    public const string AdminEnteredOnly = "Cette absence a été saisie par l'administrateur : lui seul peut l'annuler.";
    public const string NotASecretary = "Une absence ne peut être saisie que pour un compte secrétaire.";
    public const string UnknownType = "Type de congé inconnu.";
    public const string UnknownStatusFilter = "Filtre de statut inconnu.";
    public const string SessionInvalid = "Session invalide, veuillez vous reconnecter.";

    public static string OwnOverlapSentence(StaffLeaveRequest existing) =>
        $"Vous avez déjà une demande {FrenchDayPeriod.Format(existing.StartDate, existing.EndDate)}.";

    public static string OverlapSentence(string name, StaffLeaveRequest existing) =>
        existing.Status == StaffLeaveStatus.Pending
            ? $"{name} a déjà une demande en attente {FrenchDayPeriod.Format(existing.StartDate, existing.EndDate)} : répondez-y plutôt."
            : $"{name} a déjà un congé accepté {FrenchDayPeriod.Format(existing.StartDate, existing.EndDate)}.";

    public static string TypeLabel(StaffLeaveType type) => type switch
    {
        StaffLeaveType.PaidLeave => "Congé payé",
        StaffLeaveType.Sick => "Maladie",
        StaffLeaveType.Unpaid => "Sans solde",
        StaffLeaveType.Other => "Autre",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Type de congé sans libellé."),
    };

    public static string StatusLabel(StaffLeaveStatus status) => status switch
    {
        StaffLeaveStatus.Pending => "En attente",
        StaffLeaveStatus.Approved => "Accepté",
        StaffLeaveStatus.Rejected => "Refusé",
        StaffLeaveStatus.Cancelled => "Annulé",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Statut de congé sans libellé."),
    };

    /// <summary>The member a client sent, by name; null for anything else.</summary>
    public static StaffLeaveType? ParseType(string? value) =>
        Enum.TryParse<StaffLeaveType>(value, ignoreCase: true, out var type) && Enum.IsDefined(type) ? type : null;

    /// <summary>
    /// The cabinet's open days between the two dates (« Horaires » in Paramètres), ½ for a demi-journée;
    /// Monday–Saturday when no hours are set or they cannot be read.
    /// </summary>
    public static decimal CountDays(DateOnly start, DateOnly end, bool halfDay, string? workingHoursJson)
    {
        if (end < start)
            return 0;
        var openDays = OpenWeekdays(workingHoursJson);
        var count = 0;
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (openDays.Contains(day.DayOfWeek))
                count++;
        }

        return halfDay ? count * 0.5m : count;
    }

    /// <summary>The congé payé an account has taken in <paramref name="year"/>, across every accepted request.</summary>
    public static decimal TakenIn(int year, IEnumerable<StaffLeaveRequest> requests, string? workingHoursJson) =>
        requests
            .Where(r => r.Status == StaffLeaveStatus.Approved && r.Type == StaffLeaveType.PaidLeave)
            .Sum(r => DaysInYear(r, year, workingHoursJson));

    public static StaffLeaveBalanceDto Balance(
        int year, decimal? allowance, IEnumerable<StaffLeaveRequest> requests, string? workingHoursJson)
    {
        var list = requests as IReadOnlyCollection<StaffLeaveRequest> ?? requests.ToList();
        var taken = TakenIn(year, list, workingHoursJson);
        return new StaffLeaveBalanceDto
        {
            Year = year,
            Allowance = allowance,
            Taken = taken,
            Remaining = allowance is { } a ? a - taken : null,
            Pending = list
                .Where(r => r.Status == StaffLeaveStatus.Pending && r.Type == StaffLeaveType.PaidLeave)
                .Sum(r => DaysInYear(r, year, workingHoursJson)),
        };
    }

    /// <summary>Accepted days in <paramref name="year"/> per type, every type listed (0 included).</summary>
    public static List<StaffLeaveTypeTotalDto> AcceptedByType(
        int year, IEnumerable<StaffLeaveRequest> requests, string? workingHoursJson)
    {
        var accepted = requests.Where(r => r.Status == StaffLeaveStatus.Approved).ToList();
        return Enum.GetValues<StaffLeaveType>()
            .Select(type => new StaffLeaveTypeTotalDto
            {
                Type = type.ToString(),
                TypeLabel = TypeLabel(type),
                Days = accepted.Where(r => r.Type == type).Sum(r => DaysInYear(r, year, workingHoursJson)),
            })
            .ToList();
    }

    /// <summary>
    /// The share of an accepted request's counted days falling in <paramref name="year"/>. A congé spans at most two
    /// years; the first year keeps its own open days and the second takes the rest — so a day taken out in
    /// « Décompter » (1 January, always férié) comes off the year it falls in.
    /// </summary>
    public static decimal DaysInYear(StaffLeaveRequest request, int year, string? workingHoursJson) =>
        ShareInYear(request.StartDate, request.EndDate, request.CountedDays ?? request.RequestedDays, year, workingHoursJson);

    /// <summary><see cref="DaysInYear"/> for dates not yet saved as a request (the form's preview).</summary>
    public static decimal ShareInYear(DateOnly start, DateOnly end, decimal days, int year, string? workingHoursJson)
    {
        if (start.Year == end.Year)
            return start.Year == year ? days : 0;

        var inFirst = CountDays(start, new DateOnly(start.Year, 12, 31), false, workingHoursJson);
        var firstShare = Math.Min(days, inFirst);
        if (year == start.Year)
            return firstShare;
        return year == end.Year ? days - firstShare : 0;
    }

    /// <summary>
    /// What is left of the start year's allowance once this congé is taken. Only that year's share counts — the same
    /// split <see cref="Balance"/> applies after acceptance, so the form and the balance never disagree.
    /// </summary>
    public static decimal BalanceAfter(
        decimal allowance, IEnumerable<StaffLeaveRequest> history, DateOnly start, DateOnly end, decimal days,
        string? workingHoursJson) =>
        allowance
        - TakenIn(start.Year, history, workingHoursJson)
        - ShareInYear(start, end, days, start.Year, workingHoursJson);

    /// <summary>A new request's type and day count, or the refusal: type, dates and motif, then no overlap with one she holds.</summary>
    public static async Task<Result<(StaffLeaveType Type, decimal Days)>> ValidateNewAsync(
        IStaffLeaveRequestRepository requests, Clinic clinic, User owner, string? type, DateOnly startDate,
        DateOnly endDate, bool halfDay, string? reason, Func<StaffLeaveRequest, string> overlapSentence,
        CancellationToken cancellationToken)
    {
        if (ParseType(type) is not { } parsed)
            return Result<(StaffLeaveType, decimal)>.Failure(UnknownType);
        if (StaffLeaveRequest.RefusalFor(startDate, endDate, halfDay, reason) is { } refusal)
            return Result<(StaffLeaveType, decimal)>.Failure(refusal);

        var overlap = await requests.FindHoldingOverlapAsync(clinic.Id, owner.Id, startDate, endDate, cancellationToken);
        if (overlap != null)
            return Result<(StaffLeaveType, decimal)>.Failure(overlapSentence(overlap));

        return Result<(StaffLeaveType, decimal)>.Success(
            (parsed, CountDays(startDate, endDate, halfDay, clinic.WorkingHoursJson)));
    }

    /// <summary>The account's display name — full name, else e-mail.</summary>
    public static string DisplayName(User user) =>
        !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName! : user.Email ?? "Utilisateur";

    /// <summary>Every account of the cabinet by id — one read, the roster is a handful of people.</summary>
    public static async Task<IReadOnlyDictionary<string, User>> RosterAsync(
        IUserRepository users, Guid clinicId, CancellationToken cancellationToken)
    {
        var page = await users.GetByClinicIdAsync(clinicId, cancellationToken: cancellationToken);
        return page.Items.ToDictionary(u => u.Id);
    }

    /// <summary>The signed-in account, or the refusal to return.</summary>
    public static async Task<Result<User>> ResolveCallerAsync(
        IClinicContext clinicContext, IUserRepository users, CancellationToken cancellationToken)
    {
        var callerId = clinicContext.GetUserId();
        if (string.IsNullOrEmpty(callerId))
            return Result<User>.Failure(SessionInvalid);
        var caller = await users.GetByAuth0SubAsync(callerId, cancellationToken);
        return caller == null ? Result<User>.Failure(AccountNotFound) : Result<User>.Success(caller);
    }

    /// <summary>A request of this cabinet, or the 404 refusal.</summary>
    public static async Task<Result<StaffLeaveRequest>> ResolveRequestAsync(
        IStaffLeaveRequestRepository requests, Guid clinicId, Guid id, CancellationToken cancellationToken)
    {
        var request = await requests.GetByIdAsync(id, cancellationToken);
        return request == null || request.ClinicId != clinicId
            ? Result<StaffLeaveRequest>.Failure(RequestNotFound, NotFoundCode)
            : Result<StaffLeaveRequest>.Success(request);
    }

    public static StaffLeaveRequestDto ToDto(
        StaffLeaveRequest request,
        IReadOnlyDictionary<string, User> roster,
        DateOnly clinicToday,
        decimal? balanceAfter = null,
        IEnumerable<string>? othersOff = null,
        bool viewerIsAdmin = true) => new()
    {
        Id = request.Id,
        UserId = request.UserId,
        UserName = roster.TryGetValue(request.UserId, out var owner) ? DisplayName(owner) : "Compte supprimé",
        Type = request.Type.ToString(),
        TypeLabel = TypeLabel(request.Type),
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        HalfDay = request.HalfDay,
        Reason = request.Reason,
        Status = request.Status.ToString(),
        StatusLabel = StatusLabel(request.Status),
        RequestedDays = request.RequestedDays,
        CountedDays = request.CountedDays,
        DecidedByName = request.DecidedByUserId is { } deciderId && roster.TryGetValue(deciderId, out var decider)
            ? DisplayName(decider)
            : null,
        DecidedAt = request.DecidedAt,
        RejectionReason = request.RejectionReason,
        EnteredByAdmin = request.EnteredByAdmin,
        BalanceAfter = balanceAfter,
        OthersOff = othersOff?.ToList() ?? new List<string>(),
        CanCancel = request.CanBeCancelledBy(viewerIsAdmin, clinicToday),
        CreatedAt = request.CreatedAt,
        Version = request.Version,
    };

    public static DateOnly ClinicToday() => DateOnly.FromDateTime(ClinicClock.ClinicToday());

    private static IReadOnlySet<DayOfWeek> OpenWeekdays(string? workingHoursJson)
    {
        var read = WorkingHoursSerializer.Read(workingHoursJson);
        if (read.State == WorkingHoursReadState.Valid)
        {
            var open = read.Days
                .Where(d => d.Enabled && Enum.TryParse<DayOfWeek>(d.Day, ignoreCase: true, out _))
                .Select(d => Enum.Parse<DayOfWeek>(d.Day, ignoreCase: true))
                .ToHashSet();
            if (open.Count > 0)
                return open;
        }

        return new HashSet<DayOfWeek>
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
        };
    }
}
