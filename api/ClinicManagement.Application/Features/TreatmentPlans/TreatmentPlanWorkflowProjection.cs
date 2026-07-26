using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>
/// Derives, for a plan or a whole page of plans, which appointment currently speaks for each planned act and
/// which invoice (if any) already bills the plan — the read-back that lets a devis show where the patient
/// actually is.
/// <para>
/// Nothing here is persisted. Cancelling or deleting an appointment silently returns the act to
/// « À planifier », which is exactly why the state is derived rather than stored on
/// <see cref="TreatmentPlanItem"/> — a stored flag would need repairing, this cannot go stale.
/// </para>
/// <para>
/// Two batched reads serve every plan passed in (one appointments query, one invoice-links query), so a list
/// page never degrades into an N+1.
/// </para>
/// </summary>
public static class TreatmentPlanWorkflowProjection
{
    /// <summary>
    /// Appointment statuses that still represent a standing booking for an act. <c>Cancelled</c> and
    /// <c>NoShow</c> are deliberately absent: counting them would pin the act to « Planifié » forever *and*
    /// keep "Planifier" hidden, leaving it permanently unbookable.
    /// </summary>
    private static readonly HashSet<AppointmentStatus> LiveStatuses = new()
    {
        AppointmentStatus.Scheduled,
        AppointmentStatus.Confirmed,
        AppointmentStatus.InProgress,
        AppointmentStatus.Completed,
    };

    /// <summary>Build the derived lookups for the given plans (already tenant-checked by the caller).</summary>
    public static async Task<TreatmentPlanWorkflow> BuildAsync(
        IReadOnlyCollection<TreatmentPlan> plans,
        Guid clinicId,
        IAppointmentRepository appointmentRepository,
        IInvoiceRepository invoiceRepository,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var itemIds = plans.SelectMany(p => p.Items).Select(i => i.Id).ToList();

        var appointments = await appointmentRepository.GetByTreatmentPlanItemIdsAsync(
            clinicId, itemIds, cancellationToken);
        var invoiceLinks = await invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken);

        var scheduledByItemId = appointments
            .Where(a => a.TreatmentPlanItemId.HasValue && LiveStatuses.Contains(a.Status))
            .GroupBy(a => a.TreatmentPlanItemId!.Value)
            .ToDictionary(g => g.Key, g => PickRepresentative(g, asOfUtc));

        // A cancelled bridge no longer represents the plan — the plan re-enters the balance and becomes
        // billable (and amendable) again, mirroring how the money reads exclude cancelled invoices.
        var invoiceByPlanId = invoiceLinks
            .Where(l => l.Status != InvoiceStatus.Cancelled)
            .GroupBy(l => l.TreatmentPlanId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(l => l.Number ?? string.Empty).First());

        // « Prochaine séance » per plan, evaluated against the same asOfUtc as the act states so a plan can
        // never claim an upcoming visit that its own acts report as past.
        var nextAppointmentAtByPlanId = plans.ToDictionary(
            p => p.Id,
            p => p.Items
                .Select(i => scheduledByItemId.TryGetValue(i.Id, out var appointment) ? appointment : null)
                .Where(a => a != null && a.AppointmentDateTime >= asOfUtc)
                .Select(a => (DateTime?)a!.AppointmentDateTime)
                .DefaultIfEmpty(null)
                .Min());

        return new TreatmentPlanWorkflow(scheduledByItemId, invoiceByPlanId, nextAppointmentAtByPlanId);
    }

    /// <summary>
    /// Which appointment speaks for an act when several are linked (a rebooked act): the earliest still-upcoming
    /// one, else the most recent past one — so a réalisé act still shows the visit it happened at, and an act
    /// whose visit has passed without a fiche can be surfaced as « À enregistrer » rather than « Planifié ».
    /// </summary>
    private static Appointment PickRepresentative(IEnumerable<Appointment> linked, DateTime asOfUtc)
    {
        var ordered = linked.OrderBy(a => a.AppointmentDateTime).ToList();
        return ordered.FirstOrDefault(a => a.AppointmentDateTime >= asOfUtc) ?? ordered[^1];
    }
}

/// <summary>
/// Request-scoped derived lookups consumed by <c>TreatmentPlanMappingExtensions.ToDto</c>.
/// <see cref="Empty"/> is the default for paths that don't derive (command responses) — the frontend reloads
/// after every mutation, so those leave the derived fields null rather than thread two repositories through
/// every command handler.
/// </summary>
public sealed record TreatmentPlanWorkflow(
    IReadOnlyDictionary<Guid, Appointment> ScheduledByItemId,
    IReadOnlyDictionary<Guid, (Guid TreatmentPlanId, Guid InvoiceId, string? Number, InvoiceStatus Status)> InvoiceByPlanId,
    IReadOnlyDictionary<Guid, DateTime?> NextAppointmentAtByPlanId)
{
    public static TreatmentPlanWorkflow Empty { get; } = new(
        new Dictionary<Guid, Appointment>(),
        new Dictionary<Guid, (Guid, Guid, string?, InvoiceStatus)>(),
        new Dictionary<Guid, DateTime?>());
}
