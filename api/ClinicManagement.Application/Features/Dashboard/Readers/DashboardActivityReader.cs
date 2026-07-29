using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// Clinical throughput over the period and the one before it: appointments honoured, patients registered, the taux
/// d'absence, and devis accepted.
///
/// <para>Six reads, not twelve: the appointment side is a single <c>GROUP BY</c> per period from which all three
/// appointment-derived figures (honoured, missed, and the rate's denominator) are projected. Counting each status
/// separately would be four round trips per period whose windows could drift apart — the thing
/// <see cref="DashboardPeriod"/> exists to make impossible.</para>
/// </summary>
public class DashboardActivityReader : IDashboardActivityReader
{
    /// <summary>
    /// The statuses that mean the appointment did not happen. Cancelled is included alongside NoShow because for a
    /// clinic both are the same operational loss — an empty chair — and separating them would understate the figure
    /// that matters. The drill-through link filters on this exact pair.
    /// </summary>
    private static readonly AppointmentStatus[] MissedStatuses =
        { AppointmentStatus.NoShow, AppointmentStatus.Cancelled };

    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ITreatmentPlanRepository _planRepository;

    public DashboardActivityReader(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        ITreatmentPlanRepository planRepository)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _planRepository = planRepository;
    }

    public async Task<DashboardActivityDto> ReadAsync(
        Guid clinicId, DashboardPeriod period, CancellationToken cancellationToken)
    {
        var current = await _appointmentRepository.CountByStatusBetweenAsync(
            clinicId, period.From, period.ToInclusive, cancellationToken);
        var previous = await _appointmentRepository.CountByStatusBetweenAsync(
            clinicId, period.PreviousFrom, period.PreviousToInclusive, cancellationToken);

        var newPatients = await _patientRepository.CountCreatedBetweenAsync(
            clinicId, period.From, period.ToInclusive, cancellationToken: cancellationToken);
        var previousNewPatients = await _patientRepository.CountCreatedBetweenAsync(
            clinicId, period.PreviousFrom, period.PreviousToInclusive, cancellationToken: cancellationToken);

        // byAcceptedDate: « devis acceptés ce mois » means the patient said yes this month, not that a devis created
        // this month happens to be accepted now. The drill-through filters on AcceptedDate for the same reason.
        var acceptedPlans = await _planRepository.CountByStatusAsync(
            clinicId, TreatmentPlanStatus.Accepted, period.From, period.ToInclusive,
            byAcceptedDate: true, cancellationToken);
        var previousAcceptedPlans = await _planRepository.CountByStatusAsync(
            clinicId, TreatmentPlanStatus.Accepted, period.PreviousFrom, period.PreviousToInclusive,
            byAcceptedDate: true, cancellationToken);

        return new DashboardActivityDto
        {
            CompletedAppointments = PeriodComparison.Of(
                Count(current, AppointmentStatus.Completed),
                Count(previous, AppointmentStatus.Completed)),
            NewPatients = PeriodComparison.Of(newPatients, previousNewPatients),
            AbsenceRate = PeriodComparison.Rate(AbsenceRate(current), AbsenceRate(previous)),
            AcceptedPlans = PeriodComparison.Of(acceptedPlans, previousAcceptedPlans)
        };
    }

    /// <summary>A status absent from the breakdown had no rows — read it as zero, not as missing data.</summary>
    private static int Count(IReadOnlyDictionary<AppointmentStatus, int> breakdown, AppointmentStatus status) =>
        breakdown.TryGetValue(status, out var count) ? count : 0;

    /// <summary>
    /// Percentage of the window's appointments that did not happen, or <b>null</b> when there were none.
    ///
    /// <para>A period with no appointments has no absence rate. Returning <c>0</c> would assert perfect attendance —
    /// a far stronger claim than "the clinic had nothing booked", and one a closed practice would broadcast every
    /// August. Null renders as « — ».</para>
    /// </summary>
    private static decimal? AbsenceRate(IReadOnlyDictionary<AppointmentStatus, int> breakdown)
    {
        var total = breakdown.Values.Sum();
        if (total == 0)
        {
            return null;
        }

        var missed = MissedStatuses.Sum(status => Count(breakdown, status));
        return (decimal)missed / total * 100m;
    }
}
