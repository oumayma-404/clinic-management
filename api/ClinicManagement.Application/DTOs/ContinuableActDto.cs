namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One act of a past fiche de soins that could turn out to need another séance — what
/// « C'est la suite d'une séance précédente ? » offers when a visit is being booked.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The list is a set of candidates, not a diagnosis.</b> A fiche records what was <i>done</i> and never
/// what remains, so nothing in this product can know an act was left unfinished — only the dentist can. Every
/// recent act is therefore offered and the dentist picks; inferring "this one looks incomplete" would be wrong
/// on ordinary finished work, on the one surface whose job is to remind somebody of a fact.
/// </para>
/// <para>
/// ⚠️ <b>The money fields describe the note that already bills it, and they are the reason this DTO is not just
/// an act name.</b> An act billed at 1 000 DT with 800 collected leaves 200 owed on that note — not on the devis
/// the continuation is about to create — and the dentist has to be told which, or they will collect it twice or
/// not at all.
/// </para>
/// </remarks>
public class ContinuableActDto
{
    public Guid DentalRecordId { get; set; }
    public Guid ActId { get; set; }

    /// <summary>When the séance took place — how the dentist recognises it in the list.</summary>
    public DateTime InterventionDate { get; set; }

    public string ProcedureName { get; set; } = string.Empty;
    public Guid? ProcedureTypeId { get; set; }
    public List<int> ToothNumbers { get; set; } = new();

    /// <summary>What this act was recorded at. Becomes the devis line's <c>PlannedCost</c>.</summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// The note d'honoraires already billing this fiche, when there is one — <c>null</c> when the séance was
    /// never billed.
    /// <para>
    /// This is <b>the</b> fork in the whole feature: with no note, the new devis owns the act's money and bills
    /// it once when the work is finished; with one, the note keeps the money and the devis is attached to it so
    /// the pair cannot both claim it.
    /// </para>
    /// </summary>
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }

    /// <summary>
    /// What is still owed on that note, when there is one. Read from the invoice, never re-derived: it is the
    /// figure « Solde patient » is already carrying, and a second computation here would be a second answer.
    /// </summary>
    public decimal InvoiceOutstanding { get; set; }
}
