namespace ClinicManagement.Application.DTOs;

/// <summary>What « À encaisser » says the desk has to take for one séance (`seance-au-fauteuil`).</summary>
public static class SeanceCollectionKinds
{
    /// <summary>The fiche is saved and no note bills it yet: « Facturer et encaisser » takes the money.</summary>
    public const string Unbilled = "Unbilled";

    /// <summary>A note is issued and part of it is still owed: « Encaisser » on that note.</summary>
    public const string NoteBalance = "NoteBalance";

    /// <summary>
    /// A draft note exists for the séance: it holds no money and no number. « Encaisser » bills the FICHE and replaces
    /// the draft in one step — <see cref="SeanceToCollectDto.AmountDue"/> is the fiche's, and
    /// <see cref="SeanceToCollectDto.DraftTotal"/> says what the draft held, so the desk is told before confirming.
    /// </summary>
    public const string DraftNote = "DraftNote";

    /// <summary>
    /// The séance's acts are on a devis and an échéance of it is due now: « Encaisser » on that échéance, tagged with
    /// the fiche so the dentist's re-save does not take it twice.
    /// </summary>
    public const string Treatment = "Treatment";
}

/// <summary>One row of « À encaisser ».</summary>
public sealed class SeanceToCollectDto
{
    public Guid AppointmentId { get; set; }
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public string? PractitionerName { get; set; }
    /// <summary>The fiche the money is for — what « Facturer et encaisser » bills.</summary>
    public Guid DentalRecordId { get; set; }
    /// <summary>One of <see cref="SeanceCollectionKinds"/>.</summary>
    public string Kind { get; set; } = SeanceCollectionKinds.Unbilled;
    /// <summary>The fiche's total while unbilled, the note's balance once issued, the draft's total for a draft.</summary>
    public decimal AmountDue { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }

    /// <summary>A <see cref="SeanceCollectionKinds.DraftNote"/> row: the total the draft held. Null otherwise.</summary>
    public decimal? DraftTotal { get; set; }
    /// <summary>The devis a <see cref="SeanceCollectionKinds.Treatment"/> row collects on.</summary>
    public Guid? TreatmentPlanId { get; set; }
    public string? TreatmentPlanNumber { get; set; }
}
