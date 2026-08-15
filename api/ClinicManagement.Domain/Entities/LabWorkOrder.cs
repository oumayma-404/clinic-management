using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A dental lab / prosthetics work order (bon de laboratoire / prothèse): a piece of work sent
/// to an external prothésiste (crown, bridge, denture, …) and tracked from « Envoyé » through to
/// « Posé ». Clinic-scoped and attached to a patient. Cost is a TND value stored to the millime
/// (decimal(18,3)), like the other money columns.
/// </summary>
public class LabWorkOrder : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public Guid PatientId { get; private set; }

    /// <summary>
    /// The séance this prothèse belongs to — the visit at which the impression was taken or the piece fitted
    /// (AC-23). Optional: plenty of lab work is ordered between visits, and a bon with no appointment is ordinary.
    ///
    /// <para>⚠️ <b>Nullable with a real FK, and the FK is <c>SetNull</c> (D-3).</b> Cancelling or deleting the
    /// appointment must leave the bon standing with its link *cleared*, not pointing at a row that no longer
    /// exists: the prothèse is at the laboratory either way, and losing the bon because the visit was rescheduled
    /// would lose the money and the patient's crown with it.</para>
    /// </summary>
    public Guid? AppointmentId { get; private set; }

    public int? ToothNumber { get; private set; }

    /// <summary>
    /// The laboratory's name as printed on the bon. Still required and still free text: it is what the piece
    /// travels with, and a lab the cabinet has dealt with once must be recordable without first filing a
    /// fournisseur.
    /// </summary>
    public string Prosthetist { get; private set; }

    /// <summary>
    /// The <see cref="Entities.Supplier"/> this bon was sent to — the laboratory as a record somebody can
    /// <b>call</b>, rather than only a name on a line.
    /// <para>
    /// ⚠️ It sits <b>beside</b> <see cref="Prosthetist"/> and does not replace it, unlike the stock side where
    /// the free-text column was dropped. Two reasons: the name is printed on the bon and on the PDF, so it must
    /// survive a supplier being deleted or never linked at all; and a bon is routinely raised for a laboratory
    /// used once, which must not require filing a fournisseur first. The migration links the ones it can match
    /// by name and leaves the rest null.
    /// </para>
    /// </summary>
    public Guid? SupplierId { get; private set; }
    public string WorkDescription { get; private set; }
    public DateTime? SentDate { get; private set; }
    public DateTime? ExpectedDate { get; private set; }
    public DateTime? ReceivedDate { get; private set; }
    public decimal? Cost { get; private set; }
    public LabOrderStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation property
    public Patient Patient { get; private set; } = null!;

    private LabWorkOrder() { } // For EF Core

    public LabWorkOrder(
        Guid id,
        Guid clinicId,
        Guid patientId,
        string prosthetist,
        string workDescription,
        int? toothNumber = null,
        DateTime? sentDate = null,
        DateTime? expectedDate = null,
        decimal? cost = null,
        string? notes = null,
        Guid? appointmentId = null,
        Guid? supplierId = null)
    {
        if (string.IsNullOrWhiteSpace(prosthetist))
            throw new ArgumentException("Le prothésiste est requis.", nameof(prosthetist));
        if (string.IsNullOrWhiteSpace(workDescription))
            throw new ArgumentException("La description du travail est requise.", nameof(workDescription));
        if (cost.HasValue && cost.Value < 0)
            throw new ArgumentException("Le coût ne peut pas être négatif.", nameof(cost));

        Id = id;
        ClinicId = clinicId;
        PatientId = patientId;
        Prosthetist = prosthetist;
        WorkDescription = workDescription;
        ToothNumber = toothNumber;
        SentDate = sentDate;
        ExpectedDate = expectedDate;
        Cost = cost;
        Notes = notes;
        AppointmentId = appointmentId;
        SupplierId = supplierId;
        Status = LabOrderStatus.Sent;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateDetails(
        string prosthetist,
        string workDescription,
        int? toothNumber,
        DateTime? sentDate,
        DateTime? expectedDate,
        decimal? cost,
        string? notes,
        Guid? appointmentId = null,
        Guid? supplierId = null)
    {
        if (string.IsNullOrWhiteSpace(prosthetist))
            throw new ArgumentException("Le prothésiste est requis.", nameof(prosthetist));
        if (string.IsNullOrWhiteSpace(workDescription))
            throw new ArgumentException("La description du travail est requise.", nameof(workDescription));
        if (cost.HasValue && cost.Value < 0)
            throw new ArgumentException("Le coût ne peut pas être négatif.", nameof(cost));

        Prosthetist = prosthetist;
        WorkDescription = workDescription;
        ToothNumber = toothNumber;
        SentDate = sentDate;
        ExpectedDate = expectedDate;
        Cost = cost;
        Notes = notes;
        AppointmentId = appointmentId;
        SupplierId = supplierId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// The stages an order may move to from each stage — the declared transition table (AC-P2.38).
    /// <para>
    /// Forward one step is the normal path. One step **backward** is deliberately legal: « Reçu » → « En cours »
    /// is how a prothèse that arrives wrong goes back to the lab, and « Posé » → « Reçu » undoes a fitting
    /// recorded on the wrong order. What is refused is skipping the lab (« Envoyé » → « Posé », so a fitting is
    /// never recorded for work never received) and rewinding a fitted order all the way to « Envoyé », which
    /// would erase that the piece was ever delivered.
    /// </para>
    /// <para>
    /// Reads are never gated (AC-P2.41) — this table governs <see cref="SetStatus"/> only, so rows already in
    /// any state, including ones this table could not have produced, still load and render.
    /// </para>
    /// </summary>
    private static readonly Dictionary<LabOrderStatus, LabOrderStatus[]> AllowedTransitions = new()
    {
        [LabOrderStatus.Sent] = new[] { LabOrderStatus.InProgress, LabOrderStatus.Received },
        [LabOrderStatus.InProgress] = new[] { LabOrderStatus.Sent, LabOrderStatus.Received },
        [LabOrderStatus.Received] = new[] { LabOrderStatus.InProgress, LabOrderStatus.Fitted },
        [LabOrderStatus.Fitted] = new[] { LabOrderStatus.Received },
    };

    /// <summary>The stages this order may move to right now. Drives the UI's status control (AC-P2.40).</summary>
    public static IReadOnlyCollection<LabOrderStatus> NextStatusesFrom(LabOrderStatus current) =>
        AllowedTransitions.TryGetValue(current, out var allowed) ? allowed : Array.Empty<LabOrderStatus>();

    /// <summary>
    /// Move the order to a new lab stage.
    /// <para>
    /// This was a bare assignment with no rules at all: a « Posé » order could be pushed straight back to
    /// « Envoyé », and an order could jump from « Envoyé » to « Posé » without ever being received — recording a
    /// fitting for work the clinic never had. Illegal transitions now throw with a French message
    /// (AC-P2.40); re-assigning the current status stays a silent no-op, since a UI select can re-emit it.
    /// </para>
    /// </summary>
    public void SetStatus(LabOrderStatus status)
    {
        if (status == Status)
        {
            return;
        }

        if (!NextStatusesFrom(Status).Contains(status))
        {
            throw new InvalidOperationException(
                $"Transition impossible : un bon « {FrenchLabel(Status)} » ne peut pas passer à « {FrenchLabel(status)} ».");
        }

        Status = status;

        // AC-P2.39: re-stamped on every arrival, not only the first. A prothèse sent back to the lab and
        // received again is a NEW arrival; keeping the original date forever meant the délai the clinic reads
        // was the one for the piece that had to be redone.
        if (status == LabOrderStatus.Received)
        {
            ReceivedDate = DateTime.UtcNow;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// French stage name, so a refusal names the stages the way the user sees them in the UI. Lives here rather
    /// than in the API layer because it is the domain that raises the message.
    /// </summary>
    private static string FrenchLabel(LabOrderStatus status) => status switch
    {
        LabOrderStatus.Sent => "Envoyé",
        LabOrderStatus.InProgress => "En cours",
        LabOrderStatus.Received => "Reçu",
        LabOrderStatus.Fitted => "Posé",
        _ => status.ToString(),
    };
}
