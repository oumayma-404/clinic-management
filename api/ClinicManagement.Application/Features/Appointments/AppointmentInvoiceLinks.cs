using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// Which visits are billed, and on which note d'honoraires — the read side of <c>Invoice.AppointmentId</c>
/// (AC-P6.13). One batched projection per request, resolved for the whole page of appointments rather than per
/// row, in the shape <c>TreatmentPlanWorkflowProjection</c> already established.
///
/// <para>
/// It exists as a shared helper rather than inline in each handler because the list and the single-appointment
/// read must agree on <b>which invoice counts</b>: a cancelled note is not billing (it would show « Facturé »
/// with no money behind it and hide the action needed to raise a replacement), and where two non-cancelled
/// invoices point at the same visit the live one wins. Two copies of that rule is one copy too many — it is the
/// class of drift that left five realtime keys broadcasting into the void.
/// </para>
/// </summary>
public static class AppointmentInvoiceLinks
{
    /// <summary>One appointment's billing link.</summary>
    public sealed record Link(Guid InvoiceId, string? Number);

    /// <summary>
    /// Resolves the billing link for each of <paramref name="appointmentIds"/>. Appointments with no live
    /// invoice are simply absent from the result.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, Link>> ResolveAsync(
        IInvoiceRepository invoiceRepository,
        Guid clinicId,
        IReadOnlyCollection<Guid> appointmentIds,
        CancellationToken cancellationToken = default)
    {
        if (appointmentIds.Count == 0)
        {
            return new Dictionary<Guid, Link>();
        }

        var rows = await invoiceRepository.GetAppointmentLinksAsync(clinicId, appointmentIds, cancellationToken);

        return rows
            .Where(r => r.Status != InvoiceStatus.Cancelled)
            .GroupBy(r => r.AppointmentId)
            // A visit should have at most one note, but nothing in the schema enforces it (the link is a soft
            // one, like DentalRecordId). Prefer the issued invoice over a stray draft so the badge names the
            // number the patient was actually given.
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var chosen = g.OrderBy(r => r.Number == null ? 1 : 0).ThenBy(r => r.Number).First();
                    return new Link(chosen.InvoiceId, chosen.Number);
                });
    }
}
