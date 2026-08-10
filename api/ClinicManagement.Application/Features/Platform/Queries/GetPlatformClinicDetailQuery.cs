using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Queries;

/// <summary>
/// One cabinet's detail (<c>platform-console</c> US-3): the list's own figures, a six-month trend, and who to
/// call.
///
/// <para>⚠️ <b>A query that writes, deliberately.</b> AC-7.3 requires the detail read to be recorded, and the
/// alternative — a command — would be worse in a specific way: <c>RealtimeBroadcastBehavior</c> derives its
/// resource key from the namespace, so a request under <c>.Commands</c> would broadcast into a clinic group on
/// every page load, announcing a change to a cabinet nobody made. Reading is what this does; the ledger row is a
/// consequence of the read, not a use case of its own.</para>
///
/// <para>⚠️ <b>The ledger row is not best-effort, and this is the one place in the codebase where that is the
/// right call.</b> Notification and reminder side effects are swallowed because the operation they follow has
/// already committed and must not be undone by a secondary failure. Here the operation <i>is</i> the thing being
/// recorded: « every detail read is recorded » is false the moment an unrecorded read succeeds, so a failed write
/// fails the read.</para>
///
/// <para>⚠️ <b>The list read is deliberately not recorded</b> (AC-3.5). One list read touches every cabinet, so a
/// row per cabinet per page load would drown every reading anyone actually wants — including this one.</para>
/// </summary>
public class GetPlatformClinicDetailQuery : IRequest<Result<PlatformClinicDetailDto>>
{
    public Guid ClinicId { get; set; }

    /// <summary>The code a vanished cabinet is refused with, so the console renders EC-13's own state rather than
    /// a generic error page. Matched on the code, never on the French sentence.</summary>
    public const string NotFoundCode = "clinic_not_found";

    /// <summary>How many clinic-local months the trend covers (AC-3.1).</summary>
    public const int TrendMonths = 6;
}

public class GetPlatformClinicDetailQueryHandler
    : IRequestHandler<GetPlatformClinicDetailQuery, Result<PlatformClinicDetailDto>>
{
    private readonly IClinicActivityRepository _activityRepository;
    private readonly IUserRepository _userRepository;
    private readonly IPlatformAccessEntryRepository _accessEntryRepository;
    private readonly IPlatformSessionContext _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<GetPlatformClinicDetailQueryHandler> _logger;

    public GetPlatformClinicDetailQueryHandler(
        IClinicActivityRepository activityRepository,
        IUserRepository userRepository,
        IPlatformAccessEntryRepository accessEntryRepository,
        IPlatformSessionContext session,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<GetPlatformClinicDetailQueryHandler> logger)
    {
        _activityRepository = activityRepository;
        _userRepository = userRepository;
        _accessEntryRepository = accessEntryRepository;
        _session = session;
        _unitOfWork = unitOfWork;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformClinicDetailDto>> Handle(
        GetPlatformClinicDetailQuery request, CancellationToken cancellationToken)
    {
        // EC-12, as on every console read: an undeclared scope reads zero rows with no error, and « ce cabinet
        // n'existe plus » is exactly what that would look like here.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            var row = await _activityRepository.GetClinicRowAsync(request.ClinicId, cancellationToken);
            if (row is null)
            {
                // EC-13. A refusal with a code, not a 500 and not an empty detail: the console turns this into
                // « ce cabinet n'existe plus » with a way back to the portfolio.
                return Result<PlatformClinicDetailDto>.Failure(
                    "Ce cabinet n'existe plus : il a été supprimé depuis l'affichage de la liste.",
                    GetPlatformClinicDetailQuery.NotFoundCode);
            }

            var admin = await _userRepository.GetPrimaryAdminContactAsync(request.ClinicId, cancellationToken);
            var trend = await ReadTrendAsync(request.ClinicId, cancellationToken);

            // AC-7.3. Staged before the save below, and its failure fails the read — see the class remarks.
            await PlatformAccessLedger.RecordAsync(
                _accessEntryRepository,
                _session,
                row.ClinicId,
                row.Name,
                PlatformAccessAction.ViewedClinic,
                DateTime.UtcNow,
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<PlatformClinicDetailDto>.Success(new PlatformClinicDetailDto(
                Clinic: ToRowDto(row),
                AdminName: admin?.FullName,
                AdminEmail: admin?.Email,
                // No admin at all reads as « inactive » rather than as a reachable person: the screen shows a
                // marker either way, and a missing contact must not look like a live one.
                AdminIsActive: admin?.IsActive ?? false,
                Trend: trend,
                SubscriptionDataAvailable: PlatformSubscriptionPlaceholder.DataAvailable,
                SubscriptionExplanation: PlatformSubscriptionPlaceholder.DataAvailable
                    ? null
                    : PlatformSubscriptionPlaceholder.DetailExplanation));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading the platform detail of clinic {ClinicId}", request.ClinicId);
            return Result<PlatformClinicDetailDto>.Failure("Erreur lors de la lecture de la fiche du cabinet.");
        }
    }

    /// <summary>
    /// The six clinic-local months ending with the current one, oldest first.
    ///
    /// <para>⚠️ <b>Bucketed on <c>ClinicActivityDay.Day</c>, which is already a Tunisian calendar day</b>, so no
    /// timezone arithmetic happens here at all — that conversion was done once by the counter pass and stored. A
    /// second conversion would move the last day of every month into the next one.</para>
    ///
    /// <para>⚠️ <b>Every month is present, including the empty ones.</b> The pass writes a rolling 30-day window
    /// (progress.md DEV-5), so five of these six hold nothing on a young deployment; returning them with
    /// <c>DaysMeasured = 0</c> is what lets the screen say « pas encore mesuré » instead of drawing a cabinet
    /// collapsing to zero the further back the reader looks.</para>
    /// </summary>
    private async Task<IReadOnlyList<PlatformActivityMonthDto>> ReadTrendAsync(
        Guid clinicId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(ClinicClock.ClinicToday());
        var firstMonth = new DateOnly(today.Year, today.Month, 1)
            .AddMonths(-(GetPlatformClinicDetailQuery.TrendMonths - 1));

        var days = await _activityRepository.GetDaysAsync(clinicId, firstMonth, today, cancellationToken);
        var byMonth = days.ToLookup(d => (d.Day.Year, d.Day.Month));

        return Enumerable.Range(0, GetPlatformClinicDetailQuery.TrendMonths)
            .Select(offset => firstMonth.AddMonths(offset))
            .Select(month => Bucket(month, byMonth[(month.Year, month.Month)].ToList()))
            .ToList();
    }

    private static PlatformActivityMonthDto Bucket(DateOnly month, IReadOnlyList<ClinicActivityDay> days) => new(
        Year: month.Year,
        Month: month.Month,
        MonthLabel: PlatformAccessLabels.Month(month.Year, month.Month),
        Writes: days.Sum(d => d.Writes),
        Appointments: days.Sum(d => d.Appointments),
        PatientsCreated: days.Sum(d => d.PatientsCreated),
        DaysMeasured: days.Count);

    /// <summary>
    /// The list's row, unchanged — including the four entitlement members the placeholder keeps null. Kept
    /// alongside <c>ListPlatformClinicsQuery</c>'s mapper rather than shared, because the two differ in nothing
    /// today and Part 4 replaces both with the companion's read in one edit.
    /// </summary>
    private static PlatformClinicRowDto ToRowDto(PlatformClinicRow row) => new(
        ClinicId: row.ClinicId,
        Name: row.Name,
        City: row.City,
        CreatedAt: row.CreatedAt,
        Plan: null,
        State: null,
        EndsOn: null,
        DaysRemaining: null,
        Users: row.Users,
        Patients: row.Patients,
        Appointments30d: row.Appointments30d,
        Writes7d: row.Writes7d,
        Writes30d: row.Writes30d,
        ActiveDays30d: row.ActiveDays30d,
        LastWriteAt: row.LastWriteAt,
        LastLoginAt: row.LastLoginAt,
        ClinicCollectedThisMonthDt: row.CollectedThisMonth,
        CountersComputedAt: row.CountersComputedAt);
}
