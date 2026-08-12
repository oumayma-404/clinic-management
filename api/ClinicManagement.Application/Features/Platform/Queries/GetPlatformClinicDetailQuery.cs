using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
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
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly IPlatformAccessEntryRepository _accessEntryRepository;
    private readonly IPlatformSessionContext _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<GetPlatformClinicDetailQueryHandler> _logger;

    public GetPlatformClinicDetailQueryHandler(
        IClinicActivityRepository activityRepository,
        IUserRepository userRepository,
        IClinicSubscriptionRepository subscriptions,
        IPlatformAccessEntryRepository accessEntryRepository,
        IPlatformSessionContext session,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<GetPlatformClinicDetailQueryHandler> logger)
    {
        _activityRepository = activityRepository;
        _userRepository = userRepository;
        _subscriptions = subscriptions;
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
            var payments = await ReadPaymentsAsync(row, cancellationToken);
            var suspension = await ReadSuspensionAsync(row.ClinicId, cancellationToken);

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
                Clinic: PlatformClinicRowMapper.ToDto(row, ClinicClock.ClinicToday(), admin?.Email),
                AdminName: admin?.FullName,
                AdminEmail: admin?.Email,
                // No admin at all reads as « inactive » rather than as a reachable person: the screen shows a
                // marker either way, and a missing contact must not look like a live one.
                AdminIsActive: admin?.IsActive ?? false,
                Trend: trend,
                Payments: payments,
                Suspension: suspension));
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
        MonthLabel: ClinicClock.MonthLabelFr(month.Year, month.Month),
        Writes: days.Sum(d => d.Writes),
        Appointments: days.Sum(d => d.Appointments),
        PatientsCreated: days.Sum(d => d.PatientsCreated),
        DaysMeasured: days.Count);

    /// <summary>
    /// AC-6.1's suspension trail — why this cabinet is stopped, by whom and since when, or <b>null when it is not
    /// suspended at all</b>.
    ///
    /// <para>⚠️ <b>Read off the entitlement rather than added to the portfolio JOIN</b>, deliberately: the JOIN is the
    /// one bounded read the whole portfolio is cut from (EC-11), and three columns only the detail renders would ride
    /// on every row of every page for nothing. One extra read on a screen a vendor opens by hand is the cheaper end
    /// of that trade.</para>
    ///
    /// <para>⚠️ <b>The flag itself comes from the row, not from here.</b> Whether the cabinet reads « Suspendu » is
    /// <c>SubscriptionStateReader</c>'s answer on <see cref="PlatformClinicRowMapper"/>'s side — this only explains a
    /// suspension the state has already declared, which is what keeps « suspendu » having one authority.</para>
    /// </summary>
    private async Task<PlatformSuspensionDto?> ReadSuspensionAsync(
        Guid clinicId, CancellationToken cancellationToken)
    {
        var entitlement = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);

        // `Suspend` requires a motif and stamps the moment, so a suspended entitlement always has both. The guard is
        // for a row written before those fields existed rather than for a state the domain can reach today.
        if (entitlement is not { IsSuspended: true, SuspensionReason: { } reason, SuspendedAtUtc: { } at })
        {
            return null;
        }

        return new PlatformSuspensionDto(reason, at, entitlement.SuspendedBy);
    }

    /// <summary>
    /// AC-3.2's payment history — <b>the companion's ledger, read</b>, never a console-side re-derivation (FR-4).
    ///
    /// <para>⚠️ <b>The « période couverte » of each entry comes from the fold</b>, exactly as the cabinet's own
    /// « Abonnement » screen builds it: the span an entry covers is a function of every non-cancelled entry
    /// recorded before it, so computing it here from the entry alone would produce plausible dates describing
    /// periods no cabinet was ever entitled to.</para>
    ///
    /// <para>⚠️ <b>Newest first, cancelled entries included and marked.</b> An entry is never edited and never
    /// deleted (AC-5.2); a history that hid the cancelled ones would answer « what were we paid, and for what? »
    /// with a tidied version of the truth, on the one screen whose purpose is to check that.</para>
    ///
    /// <para>⚠️ <b>Each live entry also carries what cancelling it would do</b> (AC-5.3, EC-7) — see
    /// <see cref="PreviewCancellation"/>. That is why this takes the JOINed row rather than an id: the preview has to
    /// be read through the FR-1 rule, which needs the cabinet's suspension flag.</para>
    /// </summary>
    private async Task<IReadOnlyList<PlatformSubscriptionEntryDto>> ReadPaymentsAsync(
        PlatformClinicRow row, CancellationToken cancellationToken)
    {
        var entries = await _subscriptions.GetEntriesAsync(row.ClinicId, cancellationToken);
        var ledger = entries.Select(e => e.ToLedgerEntry()).ToList();
        var spans = SubscriptionLedger.FoldWithSpans(ledger).Spans.ToDictionary(s => s.EntryId);
        var clinicToday = ClinicClock.ClinicToday();

        return entries
            .OrderByDescending(e => e.RecordedAtUtc)
            .ThenByDescending(e => e.Id)
            .Select(e =>
            {
                var span = spans.TryGetValue(e.Id, out var found) ? found : null;
                return new PlatformSubscriptionEntryDto(
                    EntryId: e.Id,
                    Kind: e.Kind.ToString(),
                    KindLabel: SubscriptionLabels.PeriodKind(e.Kind),
                    RecordedOn: e.RecordedOnClinicDay,
                    CoversFrom: span?.FromDay,
                    CoversThrough: span?.ThroughDay,
                    AmountDt: e.Amount,
                    Method: e.Method?.ToString(),
                    MethodLabel: e.Method is { } method ? SubscriptionLabels.PaymentMethod(method) : null,
                    Reference: e.Reference,
                    Note: e.Note,
                    RecordedBy: e.RecordedBy,
                    IsCancelled: e.IsCancelled,
                    CancelledAt: e.CancelledAtUtc,
                    CancelledBy: e.CancelledBy,
                    CancelReason: e.CancelReason,
                    IfCancelled: e.IsCancelled
                        ? null
                        : PreviewCancellation(ledger, e.Id, row, clinicToday));
            })
            .ToList();
    }

    /// <summary>
    /// AC-5.3 / EC-7 — where the cabinet would stand if this one entry went, so the confirmation can say it
    /// <b>before</b> the vendor commits.
    ///
    /// <para><b>⚠️ It re-folds the real ledger with that entry marked cancelled, and nothing here computes a date.</b>
    /// The tempting shortcut — « the current end minus this entry's duration » — is wrong whenever the entry is not
    /// the latest one, which is precisely the case a correction is for: with durations folded on an exclusive cursor,
    /// removing a <i>middle</i> entry shortens every stretch after it. Re-folding is also what makes the preview and
    /// the write agree by construction rather than by review, since <c>SubscriptionRefold</c> will run the same fold
    /// over the same rows a moment later.</para>
    ///
    /// <para>⚠️ <c>isTrial</c> comes from the <b>previewed</b> fold, not the cabinet's current cover: cancelling a
    /// paid entry can hand the cover back to the trial, and labelling that « Actif » would describe the state the
    /// cabinet is leaving.</para>
    /// </summary>
    private static PlatformCancellationPreviewDto? PreviewCancellation(
        IReadOnlyList<SubscriptionLedgerEntry> ledger, Guid entryId, PlatformClinicRow row, DateTime clinicToday)
    {
        // FR-13's failure state has no entitlement to move, and the portfolio already says so in words.
        if (!row.HasEntitlement)
        {
            return null;
        }

        var without = ledger
            .Select(e => e.Id == entryId ? e with { IsCancelled = true } : e)
            .ToList();

        var folded = SubscriptionLedger.FoldWithSpans(without);
        var status = SubscriptionStateReader.Read(
            folded.EndsOn,
            row.SubscriptionIsSuspended,
            clinicToday,
            folded.LatestCoverKind == SubscriptionPeriodKind.Trial);

        return new PlatformCancellationPreviewDto(
            EndsOn: status.EndsOn,
            State: status.State.ToString(),
            StateLabel: SubscriptionLabels.State(status.State),
            MakesReadOnly: !status.AllowsWrites);
    }
}
