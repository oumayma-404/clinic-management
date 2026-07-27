using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Dashboard.Queries;

public class GetDashboardStatsQuery : IRequest<Result<DashboardStatsDto>>
{
    // Optional client-supplied local-day/week boundaries so the KPI counts match
    // the appointment list (which uses the same date-fns ranges). Defaults to
    // UTC-now-based ranges when omitted.
    public DateTime? TodayStart { get; set; }
    public DateTime? TodayEnd { get; set; }
    public DateTime? WeekStart { get; set; }
    public DateTime? WeekEnd { get; set; }
    public DateTime? MonthStart { get; set; }
    public DateTime? MonthEnd { get; set; }
}

public class GetDashboardStatsQueryHandler : IRequestHandler<GetDashboardStatsQuery, Result<DashboardStatsDto>>
{
    // Cancelled / no-show appointments are not "active" work and are excluded from the
    // today / this-week counts (the dashboard list applies the same filter).
    private static readonly IReadOnlyCollection<AppointmentStatus> InactiveStatuses =
        new[] { AppointmentStatus.Cancelled, AppointmentStatus.NoShow };

    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IClinicContext _clinicContext;

    public GetDashboardStatsQueryHandler(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IUserRepository userRepository,
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        IClinicContext clinicContext)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _userRepository = userRepository;
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<DashboardStatsDto>> Handle(GetDashboardStatsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<DashboardStatsDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<DashboardStatsDto>.Failure("Utilisateur introuvable.");
            }

            var clinicId = user.ClinicId;
            var now = DateTime.UtcNow;

            var todayStart = request.TodayStart ?? now.Date;
            var todayEnd = request.TodayEnd ?? todayStart.AddDays(1).AddTicks(-1);
            var weekStart = request.WeekStart ?? StartOfWeek(now);
            var weekEnd = request.WeekEnd ?? weekStart.AddDays(7).AddTicks(-1);

            var todaysAppointments = await _appointmentRepository.CountByClinicIdAsync(
                clinicId, todayStart, todayEnd, null, InactiveStatuses, cancellationToken);

            var totalPatients = await _patientRepository.CountByClinicIdAsync(clinicId, cancellationToken);

            var upcomingPending = await _appointmentRepository.CountByClinicIdAsync(
                clinicId, now, null, AppointmentStatus.Scheduled, null, cancellationToken);

            var thisWeekAppointments = await _appointmentRepository.CountByClinicIdAsync(
                clinicId, weekStart, weekEnd, null, InactiveStatuses, cancellationToken);

            var urgentPatients = await _patientRepository.CountFlaggedByClinicIdAsync(clinicId, cancellationToken);

            var monthStart = request.MonthStart ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = request.MonthEnd ?? monthStart.AddMonths(1).AddTicks(-1);
            // Encaissé ce mois-ci = invoice payments + treatment-plan installment collections (both tracks).
            var invoiceCollected = await _invoiceRepository.GetCollectedBetweenAsync(
                clinicId, monthStart, monthEnd, cancellationToken);
            var installmentCollected = await _planRepository.GetInstallmentCollectedBetweenAsync(
                clinicId, monthStart, monthEnd, cancellationToken);
            var monthlyRevenueCollected = InvoiceCalculator.RoundMoney(invoiceCollected + installmentCollected);

            // En attente de recouvrement = clinic-wide outstanding across both tracks. A plan bridged into a
            // real invoice is counted through that invoice only (PlanBillingRules — the same rule
            // « Solde patient » and « Créances » apply), so the KPI can't overstate what patients owe.
            var invoiceOutstanding = (await _invoiceRepository.GetOutstandingByPatientAsync(clinicId, cancellationToken))
                .Sum(r => r.Outstanding);
            var billedPlanIds = PlanBillingRules.BilledPlanIds(
                await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken));
            var installmentOutstanding = (await _planRepository.GetInstallmentOutstandingByPatientAsync(clinicId, now, billedPlanIds, cancellationToken))
                .Sum(r => r.Outstanding);
            var totalOutstanding = InvoiceCalculator.RoundMoney(invoiceOutstanding + installmentOutstanding);

            var dto = new DashboardStatsDto
            {
                TodaysAppointments = todaysAppointments,
                TotalPatients = totalPatients,
                UpcomingPending = upcomingPending,
                ThisWeekAppointments = thisWeekAppointments,
                UrgentPatients = urgentPatients,
                MonthlyRevenueCollected = monthlyRevenueCollected,
                TotalOutstanding = totalOutstanding
            };

            return Result<DashboardStatsDto>.Success(dto);
        }
        catch (Exception)
        {
            return Result<DashboardStatsDto>.Failure("Erreur lors du chargement du tableau de bord. Veuillez réessayer.");
        }
    }

    // Monday-based start of the week (matches the frontend's weekStartsOn: 1).
    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = ((int)date.Date.DayOfWeek + 6) % 7; // Monday => 0, Sunday => 6
        return date.Date.AddDays(-diff);
    }
}
