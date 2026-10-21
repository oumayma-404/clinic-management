namespace ClinicManagement.Application.DTOs;

/// <summary>One practitioner congé: clinic-local days, both ends inclusive.</summary>
public class DoctorLeaveDto
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int DayCount { get; set; }
    public string? Note { get; set; }
    public uint Version { get; set; }
}

/// <summary>A saved congé, and the booked visits it now covers — those are left exactly as they were.</summary>
public class DoctorLeaveSaveResultDto
{
    public DoctorLeaveDto Leave { get; set; } = new();
    public int AppointmentsDuring { get; set; }

    /// <summary>The clinic-local day of the first such visit, for the agenda link; null when there is none.</summary>
    public DateOnly? FirstAppointmentDay { get; set; }
}
