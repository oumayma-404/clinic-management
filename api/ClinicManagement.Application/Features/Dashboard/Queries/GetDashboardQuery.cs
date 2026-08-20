using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Dashboard.Readers;

namespace ClinicManagement.Application.Features.Dashboard.Queries;

/// <summary>
/// The whole dashboard for the caller's clinic over one window.
///
/// <para>The client sends only <see cref="Period"/>. The bounds — current <b>and</b> previous — are derived
/// server-side by <see cref="DashboardPeriod"/>, because a comparison whose halves were computed by two different
/// authorities is not a comparison. This replaced the retired <c>GetDashboardStatsQuery</c>, which accepted six
/// client-supplied boundary parameters.</para>
/// </summary>
public class GetDashboardQuery : IRequest<Result<DashboardDto>>
{
    /// <summary>
    /// L9 — narrow the <b>Argent</b> section to one practitioner. Only that section: Activité, À-traiter and the
    /// Tendance sparkline stay clinic-wide, because « RDV honorés » and « Prothèses en retard » are the practice's
    /// operational state and a filter there would answer a question nobody asked.
    /// </summary>
    public Guid? DoctorId { get; set; }

    /// <summary>Which window to read. Defaults to the month — the only period long enough for the money figures to say much.</summary>
    public DashboardPeriodKey Period { get; set; } = DashboardPeriodKey.Month;
}

/// <summary>
/// Thin composer: resolve the clinic, resolve the period, ask each section reader, assemble.
///
/// <para><b>The reads are sequential on purpose.</b> All four readers share the request's scoped
/// <c>ApplicationDbContext</c>, which is not thread-safe — running them under <c>Task.WhenAll</c> throws
/// « A second operation was started on this context ». Every read is an indexed <c>COUNT</c>/<c>SUM</c> over a single
/// clinic's rows, so the serial cost is small; parallelising would require a context per reader, which is a much
/// larger change than the latency justifies.</para>
/// </summary>
public class GetDashboardQueryHandler : IRequestHandler<GetDashboardQuery, Result<DashboardDto>>
{
    private readonly IDashboardActivityReader _activityReader;
    private readonly IDashboardMoneyReader _moneyReader;
    private readonly IDashboardAlertsReader _alertsReader;
    private readonly IDashboardTrendReader _trendReader;
    private readonly IDashboardProcedureMixReader _procedureMixReader;
    private readonly IDashboardAppointmentTrendReader _appointmentTrendReader;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetDashboardQueryHandler> _logger;

    public GetDashboardQueryHandler(
        IDashboardActivityReader activityReader,
        IDashboardMoneyReader moneyReader,
        IDashboardAlertsReader alertsReader,
        IDashboardTrendReader trendReader,
        IDashboardProcedureMixReader procedureMixReader,
        IDashboardAppointmentTrendReader appointmentTrendReader,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetDashboardQueryHandler> logger)
    {
        _activityReader = activityReader;
        _moneyReader = moneyReader;
        _alertsReader = alertsReader;
        _trendReader = trendReader;
        _procedureMixReader = procedureMixReader;
        _appointmentTrendReader = appointmentTrendReader;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<DashboardDto>> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<DashboardDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            // One `now` for the whole response. Reading DateTime.UtcNow per reader would let a request that spans
            // midnight compute its activity against one day and its alerts against the next.
            var nowUtc = DateTime.UtcNow;
            var period = DashboardPeriod.Resolve(request.Period, nowUtc);

            var activity = await _activityReader.ReadAsync(clinicId, period, cancellationToken);
            var (money, receivables) = await _moneyReader.ReadAsync(
                clinicId, period, nowUtc, request.DoctorId, cancellationToken);
            var alerts = await _alertsReader.ReadAsync(clinicId, nowUtc, cancellationToken);
            var trend = await _trendReader.ReadAsync(clinicId, period, nowUtc, cancellationToken);
            // Narrowed by the practitioner filter, unlike Activité and À-traiter: « quels actes ai-je faits »
            // is a question about one dentist's own work, which is exactly what that filter asks.
            var procedureMix = await _procedureMixReader.ReadAsync(
                clinicId, period, request.DoctorId, cancellationToken);
            // Clinic-wide like the money trend beside it: the six-month shape of the practice is not one
            // practitioner's, and « Rendez-vous par statut » is where a per-praticien cut would belong.
            var appointmentTrend = await _appointmentTrendReader.ReadAsync(
                clinicId, period, nowUtc, cancellationToken);

            var dto = new DashboardDto
            {
                Period = new DashboardPeriodDto
                {
                    Key = period.Key.ToString(),
                    From = period.From,
                    ToInclusive = period.ToInclusive,
                    PreviousFrom = period.PreviousFrom,
                    PreviousToInclusive = period.PreviousToInclusive
                },
                Activity = activity,
                Money = money,
                Receivables = receivables,
                Alerts = alerts,
                Trend = trend,
                ProcedureMix = procedureMix,
                AppointmentTrend = appointmentTrend
            };

            return Result<DashboardDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // The detail goes to the log; the caller only ever sees French guidance (AC-13.2).
            _logger.LogError(ex, "Unhandled failure building the dashboard");
            return Result<DashboardDto>.Failure("Erreur lors du chargement du tableau de bord. Veuillez réessayer.");
        }
    }
}
