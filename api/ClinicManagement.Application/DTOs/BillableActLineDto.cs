namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One act a fiche de soins recorded, already priced — what the note d'honoraires editor offers under
/// « Reprendre des actes réalisés ».
///
/// <para>Composed by <c>GetPatientBillableActLinesQuery</c> from <c>DentalRecordInvoiceLines</c>, so the figures
/// are the ones the Factures module would bill for the same work. See that query for why the rule is not
/// re-derived in the browser.</para>
/// </summary>
public class BillableActLineDto
{
    /// <summary>
    /// Identifies this offered row to the picker, and nothing else. Composed rather than an act id because the
    /// fallback line of a legacy fiche with no acts has none.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The séance this act belongs to — the picker groups by it.</summary>
    public Guid DentalRecordId { get; set; }

    public DateTime InterventionDate { get; set; }

    /// <summary>« Composite (dents 16, 26) » — the act, never the diagnosis (medical secrecy).</summary>
    public string Designation { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPriceHt { get; set; }
}
