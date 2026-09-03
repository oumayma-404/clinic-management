namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One act under way, as « Traitements en cours » shows it: whose, what, how far along, what is next, and
/// whether that next séance is booked yet.
/// <para>
/// This is the answer to the dentist's « je suis bloqué, je ne sais pas quoi planifier ». Everything the product
/// needed to say it was already stored — the devis, the steps, the appointments — and nothing anywhere put the
/// three together on one screen.
/// </para>
/// </summary>
public class TreatmentInProgressDto
{
    public Guid PlanId { get; set; }

    /// <summary>The devis number (<c>AAAA-NNNN</c>). Null only for a legacy draft, which this list excludes.</summary>
    public string? PlanNumber { get; set; }

    public Guid PatientId { get; set; }

    /// <summary>
    /// Resolved through one batched read over the page, never per row. Null when the patient record has since
    /// been deleted — the row still shows, because the treatment is the thing being reported.
    /// </summary>
    public string? PatientName { get; set; }

    public Guid ItemId { get; set; }

    /// <summary>The act — « Bridge 4 dents ».</summary>
    public string DesignationFr { get; set; } = string.Empty;

    public int StepsTotal { get; set; }
    public int StepsDone { get; set; }

    /// <summary>The next step to carry out. Never null on a row of this list: an act with no step left is
    /// « réalisé » and drops out of it.</summary>
    public Guid? NextStepId { get; set; }

    public string? NextStepLabel { get; set; }

    /// <summary>1-based for display — « étape 3 sur 3 ». The stored rank is 0-based.</summary>
    public int? NextStepNumber { get; set; }

    /// <summary>Chair time to book for the next step, when the protocol estimates one.</summary>
    public int? NextStepEstimatedDurationMinutes { get; set; }

    /// <summary>
    /// When the most recent carried-out step happened — the « dernière séance il y a 12 j » the list is ordered
    /// by, oldest first, because a treatment nobody has come back for is the point of the screen.
    /// </summary>
    public DateTime? LastStepDoneOn { get; set; }

    /// <summary>
    /// The appointment already booked for the <b>next</b> step, when there is one — so the row reads
    /// « prochaine séance le 12 septembre » instead of offering to book what is booked.
    /// <para>
    /// ⚠️ Rows are <b>not</b> filtered on this. A treatment under way belongs on the list whether or not its
    /// next séance is booked, and filtering after the page was cut would answer a different question than the
    /// total says it did — the trap the repository's own note names.
    /// </para>
    /// </summary>
    public Guid? NextStepAppointmentId { get; set; }

    public DateTime? NextStepAppointmentAt { get; set; }
}
