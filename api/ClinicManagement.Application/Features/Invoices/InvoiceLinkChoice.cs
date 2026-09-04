using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Invoices;

/// <summary>
/// Which note d'honoraires speaks for one thing — a visit, a fiche de soins — when several point at it.
/// </summary>
/// <remarks>
/// <para>
/// The links from an invoice to an appointment, a fiche and a devis are all <b>soft</b>: nothing in the schema
/// stops two notes naming one fiche, and in practice two do (a draft raised and abandoned beside the issued one,
/// or a note cancelled and replaced). So every read that annotates « facturé sur … » has to choose, and they must
/// all choose the same one or two screens name different numbers for the same work.
/// </para>
/// <para>
/// The rule: a <b>cancelled</b> note bills nothing — it would show « facturé » with no money behind it *and* hide
/// the action to raise a replacement — and among what is left the <b>issued</b> one wins over a stray draft, so
/// the number named is the one the patient was actually given.
/// </para>
/// <para>
/// ⚠️ Extracted from <c>AppointmentInvoiceLinks</c>, which had it inline, rather than written a second time
/// beside it: this repository's dominant defect is a correct rule wired to one call site, and « which note counts »
/// now has three readers (the agenda's badge, and the two halves of the continuation feature).
/// </para>
/// </remarks>
public static class InvoiceLinkChoice
{
    /// <summary>
    /// One chosen invoice per key, from rows that may hold several per key. Keys with only cancelled notes are
    /// absent from the result — which is the correct answer, not a missing one: nothing bills that work.
    /// </summary>
    public static Dictionary<Guid, (Guid InvoiceId, string? Number)> ByKey(
        IEnumerable<(Guid Key, Guid InvoiceId, string? Number, InvoiceStatus Status)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .Where(r => r.Status != InvoiceStatus.Cancelled)
            .GroupBy(r => r.Key)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var chosen = g.OrderBy(r => r.Number == null ? 1 : 0).ThenBy(r => r.Number).First();
                    return (chosen.InvoiceId, chosen.Number);
                });
    }
}
