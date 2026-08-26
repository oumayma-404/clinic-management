using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.DTOs;

public class LabWorkOrderDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid PatientId { get; set; }
    public string? PatientName { get; set; }

    /// <summary>The séance this prothèse belongs to, or null (AC-23). Drives the « Voir le RDV » link.</summary>
    public Guid? AppointmentId { get; set; }

    public int? ToothNumber { get; set; }

    /// <summary>The laboratory's name as printed on the bon — free text, and always present.</summary>
    public string Prosthetist { get; set; } = string.Empty;

    /// <summary>The linked fournisseur, or null when this bon names a laboratory nobody has filed.</summary>
    public Guid? SupplierId { get; set; }

    /// <summary>
    /// The linked fournisseur's nom. Deliberately carried <b>beside</b> <see cref="Prosthetist"/> rather than
    /// replacing it: the bon prints the name it was raised with, and a supplier renamed since must not silently
    /// rewrite what was sent to the laboratory.
    /// </summary>
    public string? SupplierName { get; set; }

    /// <summary>
    /// The laboratory's deliverable Tunisian E.164 number, or null — what makes « Relancer le labo » a WhatsApp
    /// action rather than a note to go and look the number up.
    /// </summary>
    public string? SupplierPhoneE164 { get; set; }

    public string WorkDescription { get; set; } = string.Empty;
    public DateTime? SentDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public decimal? Cost { get; set; }

    /// <summary>
    /// The caisse dépense this bon produced on arrival, or null when none has been posted — because the bon is
    /// not in yet, or because it carries no coût. Read by the UI only to say which of the two happened.
    /// </summary>
    public Guid? ExpenseId { get; set; }

    public string Status { get; set; } = string.Empty;
    /// <summary>
    /// The stages this order may legally move to from its current one (AC-P2.40), so the UI's status control can
    /// offer only those instead of all four and then bouncing a refusal. Derived from the domain's transition
    /// table — the client never re-implements it.
    /// </summary>
    public List<string> AllowedNextStatuses { get; set; } = new();
    public string? Notes { get; set; }

    /// <summary>
    /// True when the piece is still at the laboratory past the day it was expected back — the same rule, from the
    /// same file, as the dashboard's « Prothèses en retard » count (<c>LabOrderOverdue</c>). Served rather than
    /// re-derived in the browser so the card's N and the rows wearing a badge are always the same N.
    /// </summary>
    public bool IsOverdue { get; set; }

    /// <summary>Round-tripped by the edit form so a concurrent change is a 409 rather than a silent overwrite.</summary>
    public uint Version { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public static class LabWorkOrderMappingExtensions
{
    /// <summary>
    /// <paramref name="supplier"/> is resolved by the caller's batched read — a query per row is the
    /// companion-read defect `list-pagination` documents, and this list carries a laboratory on every row.
    /// </summary>
    public static LabWorkOrderDto ToDto(
        this LabWorkOrder order, string? patientName = null, Supplier? supplier = null, bool isOverdue = false) => new()
    {
        Id = order.Id,
        ClinicId = order.ClinicId,
        PatientId = order.PatientId,
        PatientName = patientName ?? order.Patient?.GetFullName(),
        AppointmentId = order.AppointmentId,
        ToothNumber = order.ToothNumber,
        Prosthetist = order.Prosthetist,
        SupplierId = order.SupplierId,
        SupplierName = supplier?.Name,
        SupplierPhoneE164 = PhoneNumber.ToE164(supplier?.PhoneNumber),
        WorkDescription = order.WorkDescription,
        SentDate = order.SentDate,
        ExpectedDate = order.ExpectedDate,
        ReceivedDate = order.ReceivedDate,
        Cost = order.Cost,
        ExpenseId = order.ExpenseId,
        Status = order.Status.ToString(),
        AllowedNextStatuses = LabWorkOrder.NextStatusesFrom(order.Status).Select(s => s.ToString()).ToList(),
        Notes = order.Notes,
        IsOverdue = isOverdue,
        Version = order.Version,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt
    };
}
