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

    /// <summary>« Mode discret »: the clinic-wide money screens are hidden on every device of the cabinet.</summary>
    public bool IsMoneyHidden { get; set; }

    /// <summary>Secretaries may ask for a congé (Paramètres → « Équipe »). Mapped at all seven build sites.</summary>
    public bool LeaveRequestsEnabled { get; set; }

    /// <summary>Secretaries clock in and out at the cabinet's borne (Paramètres → « Équipe »).</summary>
    public bool AttendanceEnabled { get; set; }

    /// <summary>« Bloquer l'app tant que l'arrivée n'est pas pointée » — meaningful only while the pointage is on.</summary>
    public bool AttendanceLockEnabled { get; set; }

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


