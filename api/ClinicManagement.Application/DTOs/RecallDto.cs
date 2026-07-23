namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One patient due (or overdue) for a recall (« à relancer »). The due date is derived on read from the
/// patient's last completed visit (or their creation date when they have never been seen) + the clinic's
/// recall interval. Excludes patients with a future booked appointment or an active snooze.
/// </summary>
public class RecallDto
{
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateTime? LastVisitDate { get; set; }
    public DateTime DueDate { get; set; }
    public int DaysOverdue { get; set; }
    public string? Reason { get; set; }
    public DateTime? LastContactedAt { get; set; }
}

/// <summary>The per-clinic recall configuration (currently just the interval in months, default 6).</summary>
public class RecallSettingsDto
{
    public int IntervalMonths { get; set; }
}
