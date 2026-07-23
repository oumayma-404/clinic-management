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
    public int? ToothNumber { get; private set; }
    public string Prosthetist { get; private set; }
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
        string? notes = null)
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
        string? notes)
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
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetStatus(LabOrderStatus status)
    {
        Status = status;
        if (status == LabOrderStatus.Received && ReceivedDate == null)
            ReceivedDate = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
