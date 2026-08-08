namespace ClinicManagement.Application.DTOs;

public class ClinicDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Code { get; set; }
    public string? LogoUrl { get; set; }

    // Billing / note-d'honoraires settings.
    public string? MatriculeFiscal { get; set; }
    public bool VatApplicable { get; set; }
    public decimal VatRate { get; set; }
    public bool StampDutyEnabled { get; set; }
    public decimal StampDutyAmount { get; set; }


    // Working hours (reliability-and-polish AC-7). Null = no saved hours yet (the UI falls back to a default).
    public List<WorkingDayDto>? WorkingHours { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (PostgreSQL <c>xmin</c>). Send it back on the matching update command so
    /// the save is checked against the copy the user actually edited; a peer's change in between then yields
    /// a 409 instead of a silent overwrite.
    /// </summary>
    public uint Version { get; set; }
}


