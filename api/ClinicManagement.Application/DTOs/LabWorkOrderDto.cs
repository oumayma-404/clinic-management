using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

public class LabWorkOrderDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid PatientId { get; set; }
    public string? PatientName { get; set; }
    public int? ToothNumber { get; set; }
    public string Prosthetist { get; set; } = string.Empty;
    public string WorkDescription { get; set; } = string.Empty;
    public DateTime? SentDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public decimal? Cost { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public static class LabWorkOrderMappingExtensions
{
    public static LabWorkOrderDto ToDto(this LabWorkOrder order, string? patientName = null) => new()
    {
        Id = order.Id,
        ClinicId = order.ClinicId,
        PatientId = order.PatientId,
        PatientName = patientName ?? order.Patient?.GetFullName(),
        ToothNumber = order.ToothNumber,
        Prosthetist = order.Prosthetist,
        WorkDescription = order.WorkDescription,
        SentDate = order.SentDate,
        ExpectedDate = order.ExpectedDate,
        ReceivedDate = order.ReceivedDate,
        Cost = order.Cost,
        Status = order.Status.ToString(),
        Notes = order.Notes,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt
    };
}
