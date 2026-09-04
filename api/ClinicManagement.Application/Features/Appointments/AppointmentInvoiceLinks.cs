using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

using ClinicManagement.Application.Features.Invoices;

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

        // A visit should have at most one note, but nothing in the schema enforces it (the link is a soft one,
        // like DentalRecordId). `InvoiceLinkChoice` is where that rule lives now — the continuation feature needs
        // the same answer keyed on the fiche, and two copies of « which note counts » is how two screens come to
        // name different numbers for one act.
        return InvoiceLinkChoice
            .ByKey(rows.Select(r => (r.AppointmentId, r.InvoiceId, r.Number, r.Status)))
            .ToDictionary(kv => kv.Key, kv => new Link(kv.Value.InvoiceId, kv.Value.Number));
    }
}
