using System.Globalization;
using System.Security.Cryptography;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Team;

/// <summary>
/// The pointage's sentences, codes and arithmetic — one owner for the borne, the admin's sheet, the dashboard and
/// « Mes présences », so a line reads the same everywhere. Times are cabinet-local; no hour is ever invented.
/// </summary>
public static class AttendanceRules
{
    public const string ModuleOffCode = "team_module_off";
    public const string TerminalUnknownCode = "terminal_unknown";
    public const string SchemaWrongCode = "schema_wrong";
    public const string SchemaLockedCode = "schema_locked";
    public const string SchemaMissingCode = "schema_missing";
    public const string PersonNotTrackedCode = "person_not_tracked";
    public const string OnLeaveCode = "on_leave";
    public const string SchemaInvalidCode = "schema_invalid";
    public const string PunchStateChangedCode = "punch_state_changed";
    public const string NotFoundCode = "attendance_not_found";

    /// <summary>Today's state of a person, on the borne's tiles and in « Équipe aujourd'hui ».</summary>
    public const string StateIn = "in";
    public const string StateOut = "out";
    public const string StateLeave = "leave";

    public const string ModuleOff = "Le pointage n'est pas activé pour ce cabinet.";
    public const string TerminalUnknown = "Cet appareil n'est pas une borne du cabinet.";
    public const string SchemaMissing = "Schéma à créer dans « Mon profil » avant de pointer.";
    public const string PersonNotTracked = "Cette personne ne pointe pas sur cette borne.";
    public const string OnLeave = "Vous êtes en congé aujourd'hui.";
    public const string EntryNotFound = "Pointage introuvable.";
    public const string TimeRequired = "Indiquez l'heure au format 08:30.";
    public const string MonthInvalid = "Mois invalide (format attendu : 2026-11).";
    public const string AdminOnly = "Seul un administrateur du cabinet peut gérer les présences.";
    public const string NotTrackedSelf = "Votre compte ne pointe pas : demandez à l'administrateur.";
    public const string TerminalNotFound = "Borne introuvable.";
    public const string InFuture = "Une heure à venir ne peut pas être enregistrée.";
    public const string DayInFuture = "Choisissez un jour passé ou aujourd'hui.";
    public const string Overlaps = "Ces heures chevauchent un autre pointage de ce jour.";
    public const string PunchStateChanged = "Votre pointage a changé entre-temps. Touchez à nouveau votre nom.";

    public const int MinDots = 4;
    public const int MaxDots = 9;

    /// <summary>The French refusal for a schéma, or null: 4 to 9 dots of the 3×3 pad (0–8), each at most once.</summary>
    public static string? SchemaRefusalFor(IReadOnlyList<int>? pattern)
    {
        if (pattern is null || pattern.Count < MinDots)
            return $"Reliez au moins {MinDots} points.";
        if (pattern.Count > MaxDots)
            return $"Un schéma relie au plus {MaxDots} points.";
        if (pattern.Any(dot => dot is < 0 or > 8))
            return "Ce schéma n'est pas valide.";
        if (pattern.Distinct().Count() != pattern.Count)
            return "Chaque point ne sert qu'une fois.";
        return null;
    }

    /// <summary>The text the schéma is hashed as — the dots in the order they were drawn.</summary>
    public static string PatternText(IReadOnlyList<int> pattern) => string.Join("-", pattern);

    public static string SchemaWrong(int attemptsLeft) =>
        attemptsLeft == 1
            ? "Schéma incorrect. Encore 1 essai avant le blocage."
            : $"Schéma incorrect. Encore {attemptsLeft} essais avant le blocage.";

    public static string SchemaLocked(DateTime lockedUntilUtc, DateTime nowUtc)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((lockedUntilUtc - nowUtc).TotalMinutes));
        return $"Trop d'essais. Réessayez dans {minutes} min, ou demandez à l'administrateur de réinitialiser votre schéma.";
    }

    /// <summary>The signed-in account and its cabinet, refused unless the pointage is on (and, if asked, an admin calls).</summary>
    public static async Task<Result<(User Caller, Clinic Clinic)>> ResolveAsync(
        IClinicContext clinicContext, IUserRepository users, IClinicRepository clinics, bool adminOnly,
        CancellationToken cancellationToken)
    {
        var resolved = await StaffLeaveRules.ResolveCallerAsync(clinicContext, users, cancellationToken);
        if (resolved.IsFailure)
            return Result<(User, Clinic)>.FailureFrom(resolved);
        var caller = resolved.Value!;
        if (adminOnly && !caller.IsAdmin())
            return Result<(User, Clinic)>.Failure(AdminOnly);

        var clinic = await clinics.GetByIdAsync(caller.ClinicId, cancellationToken);
        if (clinic is not { AttendanceEnabled: true })
            return Result<(User, Clinic)>.Failure(ModuleOff, ModuleOffCode);
        return Result<(User, Clinic)>.Success((caller, clinic));
    }

    public static AttendancePersonSettingDto ToSetting(User user, DateTime nowUtc) => new()
    {
        UserId = user.Id,
        FullName = StaffLeaveRules.DisplayName(user),
        Role = user.Role,
        IsActive = user.IsActive,
        Tracked = user.IsAttendanceTracked,
        TrackedOverride = user.AttendanceTrackedOverride,
        HasSchema = user.HasAttendanceSchema,
        LockedUntil = user.IsSchemaLocked(nowUtc) ? user.SchemaLockedUntilUtc : null,
        Version = user.Version,
    };

    public static AttendanceTerminalDto ToDto(AttendanceTerminal terminal) => new()
    {
        Id = terminal.Id,
        Name = terminal.Name,
        CreatedAt = terminal.CreatedAt,
        LastUsedAt = terminal.LastUsedAt,
        IsActive = terminal.IsActive,
        Version = terminal.Version,
    };

    /// <summary>« 08:02 » — a UTC instant on the cabinet's 24-hour clock.</summary>
    public static string LocalTime(DateTime utc) =>
        ClinicClock.ToClinicLocal(utc).ToString("HH:mm", CultureInfo.InvariantCulture);

    public static DateOnly LocalDayOf(DateTime utc) => DateOnly.FromDateTime(ClinicClock.ToClinicLocal(utc));

    public static DateOnly Today(DateTime? nowUtc = null) => StaffLeaveRules.ClinicToday(nowUtc);

    /// <summary>« 08:30 » on <paramref name="day"/> as a UTC instant, or null for anything that is not a 24-hour time.</summary>
    public static DateTime? ParseLocalTime(DateOnly day, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !TimeOnly.TryParseExact(text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return null;
        return ClinicClock.ToUtc(day.ToDateTime(time));
    }

    /// <summary>An admin's hours on <paramref name="day"/>, as UTC instants, or the refusal: 24-hour times, none to come, a motif.</summary>
    public static Result<(DateTime Arrival, DateTime? Departure)> HoursRefusalFor(
        DateOnly day, string? arrivalText, string? departureText, string? reason, DateTime nowUtc)
    {
        if (ParseLocalTime(day, arrivalText) is not { } arrival)
            return Result<(DateTime, DateTime?)>.Failure(TimeRequired);
        DateTime? departure = null;
        if (!string.IsNullOrWhiteSpace(departureText))
        {
            if (ParseLocalTime(day, departureText) is not { } parsed)
                return Result<(DateTime, DateTime?)>.Failure(TimeRequired);
            departure = parsed;
        }
        if (arrival > nowUtc || departure > nowUtc)
            return Result<(DateTime, DateTime?)>.Failure(InFuture);
        if (AttendanceEntry.CorrectionRefusalFor(arrival, departure, reason) is { } refusal)
            return Result<(DateTime, DateTime?)>.Failure(refusal);
        return Result<(DateTime, DateTime?)>.Success((arrival, departure));
    }

    /// <summary>« AB » from « Amira Ben Salah » — the first two words, as the web's <c>initialsOf</c> draws every avatar.</summary>
    public static string Initials(string name)
    {
        var letters = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[0]));
        return letters.Length == 0 ? "?" : letters.ToUpperInvariant();
    }

    public static int? Minutes(AttendanceEntry entry) =>
        entry.DepartureUtc is { } departure ? (int)Math.Round((departure - entry.ArrivalUtc).TotalMinutes) : null;

    public static AttendanceEntryDto ToDto(AttendanceEntry entry, IReadOnlyDictionary<string, User> roster, DateOnly today) => new()
    {
        Id = entry.Id,
        UserId = entry.UserId,
        UserName = roster.TryGetValue(entry.UserId, out var owner) ? StaffLeaveRules.DisplayName(owner) : "Compte supprimé",
        LocalDay = entry.LocalDay,
        Arrival = LocalTime(entry.ArrivalUtc),
        Departure = entry.DepartureUtc is { } departure ? LocalTime(departure) : null,
        IsPresent = entry.IsOpen && entry.LocalDay == today,
        DepartureMissing = entry.IsOpen && entry.LocalDay < today,
        IsCorrected = entry.IsCorrected,
        CorrectionReason = entry.CorrectionReason,
        CorrectedByName = entry.CorrectedByUserId is { } correctorId && roster.TryGetValue(correctorId, out var corrector)
            ? StaffLeaveRules.DisplayName(corrector)
            : null,
        CorrectedAt = entry.CorrectedAt,
        PunchedArrival = entry.PunchedArrivalUtc is { } punchedArrival ? LocalTime(punchedArrival) : null,
        PunchedDeparture = entry.PunchedDepartureUtc is { } punchedDeparture ? LocalTime(punchedDeparture) : null,
        TerminalName = entry.TerminalName,
        Minutes = Minutes(entry),
        Version = entry.Version,
    };

    /// <summary>Days pointed, minutes of closed lines and missing departures — an open line counts its day, never its hours.</summary>
    public static AttendanceTotalsDto Totals(string userId, IEnumerable<AttendanceEntry> entries, DateOnly today)
    {
        var own = entries.Where(e => e.UserId == userId).ToList();
        return new AttendanceTotalsDto
        {
            UserId = userId,
            Days = own.Select(e => e.LocalDay).Distinct().Count(),
            Minutes = own.Sum(e => Minutes(e) ?? 0),
            MissingDepartures = own.Count(e => e.IsOpen && e.LocalDay < today),
            OpenToday = own.Any(e => e.IsOpen && e.LocalDay == today),
        };
    }

    /// <summary>The first and last day of « 2026-11 », or null for a key that is not a month.</summary>
    public static (DateOnly From, DateOnly To)? MonthDays(string? monthKey)
    {
        if (!ClinicClock.TryParseMonthKey(monthKey, out var year, out var month))
            return null;
        return (new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
    }

    /// <summary>Whether the cabinet opens on <paramref name="day"/> (Paramètres → Horaires; Monday–Saturday unset).</summary>
    public static bool IsOpenDay(DateOnly day, string? workingHoursJson) =>
        StaffLeaveRules.CountDays(day, day, false, workingHoursJson) > 0;

    /// <summary>The step-up action « Faire de cet appareil une borne » spends.</summary>
    public const string RegisterTerminalStepUpAction = "register-attendance-terminal";

    public const string ArrivalAction = "arrival";
    public const string DepartureAction = "departure";

    /// <summary>The borne cookie's value: 256 random bits, URL-safe. Stored only as its SHA-256.</summary>
    public static string NewTerminalSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// The active borne a secret names, with the request scoped to its cabinet (an anonymous request reads zero rows
    /// otherwise). Null for no secret, an unknown or retired one, or a caller already scoped to another cabinet.
    /// </summary>
    public static async Task<AttendanceTerminal?> FindBorneAsync(
        IAttendanceTerminalRepository terminals, ITenantScope tenantScope, string? secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return null;
        var terminal = await terminals.FindActiveBySecretHashAcrossClinicsAsync(SessionCredential.Hash(secret), cancellationToken);
        if (terminal is null)
            return null;
        var compatible = tenantScope.Kind == TenantScopeKind.Unset
            || (tenantScope.Kind == TenantScopeKind.Clinic && tenantScope.ClinicId == terminal.ClinicId);
        if (!compatible)
            return null;
        tenantScope.UseClinic(terminal.ClinicId);
        return terminal;
    }

    /// <summary>The accepted congé of <paramref name="userId"/> covering <paramref name="day"/>, whole or half day.</summary>
    public static StaffLeaveRequest? LeaveCovering(string userId, IEnumerable<StaffLeaveRequest> acceptedLeaves, DateOnly day) =>
        acceptedLeaves.FirstOrDefault(l => l.UserId == userId && l.Status == StaffLeaveStatus.Approved
            && l.StartDate <= day && day <= l.EndDate);

    /// <summary>A whole-day accepted congé covers today; a half day still lets her pointer the other half.</summary>
    public static bool IsOnFullDayLeave(string userId, IEnumerable<StaffLeaveRequest> acceptedLeaves, DateOnly today) =>
        LeaveCovering(userId, acceptedLeaves, today) is { HalfDay: false };

    /// <summary>
    /// « Ces heures chevauchent… », or null: a person's lines of one day never overlap, and an open line runs until
    /// it is closed — so a day never holds two open lines.
    /// </summary>
    public static string? OverlapRefusalFor(
        IEnumerable<AttendanceEntry> sameDayLines, DateTime arrivalUtc, DateTime? departureUtc, Guid? excludingId = null)
    {
        var end = departureUtc ?? DateTime.MaxValue;
        return sameDayLines.Any(line => line.Id != excludingId
            && line.ArrivalUtc < end && arrivalUtc < (line.DepartureUtc ?? DateTime.MaxValue))
            ? Overlaps
            : null;
    }
}
