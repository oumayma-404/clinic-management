namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One act a dentist marked « non terminé » and which nothing has picked up yet — a row of
/// « Suites à planifier ».
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This list is a statement, where <see cref="ContinuableActDto"/>'s is a question.</b> That one offers
/// every recent act because a fiche records what was <i>done</i> and never what remains, so it cannot know; this
/// one carries only acts somebody ticked, so every row is a fact a human entered. The two therefore filter in
/// opposite ways on the same flag — a sort there, a filter here — and that is deliberate rather than
/// inconsistent.
/// </para>
/// <para>
/// ⚠️ <b>It carries the money and moves none of it.</b> An act billed 1 000 with 800 collected leaves 200 owed
/// <i>on its note</i>, where la caisse, « Créances » and « Solde patient » already carry it. The row says which
/// document that is so nobody collects it twice; no figure here is written anywhere by this feature.
/// </para>
/// <para>
/// ⚠️ <b>An act already carried by a devis is not here</b>, and the exclusion goes through
/// <c>ContinuationTracking</c> rather than through the tick. The flag is never cleared automatically — it
/// records what the dentist saw that day — so reading it as « still needs planning » would keep chasing work
/// that is already booked on a treatment, and offering it would mint a second devis over the same act.
/// </para>
/// </remarks>
public class UnfinishedActDto
{
    /// <summary>The fiche the act sits on — what the continuation is keyed on.</summary>
    public Guid DentalRecordId { get; set; }
    public Guid ActId { get; set; }

    public Guid PatientId { get; set; }

    /// <summary>
    /// Resolved from the patient aggregate, and <c>null</c> when the patient could not be read. Null is « je ne
    /// sais pas » and the row renders a placeholder; an empty string would render as a nameless row nobody can
    /// act on.
    /// </summary>
    public string? PatientName { get; set; }

    /// <summary>When the séance took place — what « il y a 18 jours » is measured from.</summary>
    public DateTime InterventionDate { get; set; }

    public string ProcedureName { get; set; } = string.Empty;
    public Guid? ProcedureTypeId { get; set; }
    public List<int> ToothNumbers { get; set; } = new();

    /// <summary>What the act was recorded at on its fiche.</summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// The note d'honoraires already billing this fiche, when there is one — <c>null</c> when the séance was
    /// never billed. Same fork as <see cref="ContinuableActDto.InvoiceId"/>, and the row states which of the two
    /// it is for the same reason: a dentist who cannot tell will collect the remainder twice or not at all.
    /// </summary>
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }

    /// <summary>What is still owed on that note. Read from the invoice, never re-derived.</summary>
    public decimal InvoiceOutstanding { get; set; }

    /// <summary>
    /// A séance already booked for this patient in the future, if any — so the row can say « un rendez-vous est
    /// déjà prévu » instead of sending somebody to book a second one.
    /// <para>
    /// ⚠️ It is <b>per patient</b> and not per act, and the wording has to match that: nothing links a booking
    /// to an act that has no treatment behind it, so this says « the patient is coming back », never « this act
    /// is booked ». Claiming the stronger of the two is how a worklist starts lying.
    /// </para>
    /// </summary>
    public DateTime? NextAppointmentAt { get; set; }
}
