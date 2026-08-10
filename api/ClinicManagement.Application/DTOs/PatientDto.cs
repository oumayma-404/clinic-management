namespace ClinicManagement.Application.DTOs;

public class PatientDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    /// <summary>Null when the patient was registered without one — the client renders « âge inconnu » (AC-18).</summary>
    public DateTime? DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;

    /// <summary>
    /// Which teeth this patient is charted on — <c>"Child"</c> or <c>"Adult"</c>.
    ///
    /// <para>
    /// A **string**, not the raw enum: this API registers no <c>JsonStringEnumConverter</c>, so an enum property
    /// would go over the wire as <c>0</c>/<c>1</c> and the client would be switching on integers. Same fix as the
    /// caisse ledger's movement kinds. The client maps the English key to a French label at display time.
    /// </para>
    /// </summary>
    public string Dentition { get; set; } = nameof(Domain.Enums.DentitionType.Adult);
    /// <summary>Null when the patient gave none — not an empty string, and never a placeholder address.</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Null when the patient gave none. A patient without one receives no reminder and no relance; the UI says
    /// so rather than rendering a neutral blank.
    /// </summary>
    public string? PhoneNumber { get; set; }
    public string? MedicalHistory { get; set; }
    public string? Allergies { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    /// <summary>
    /// Who referred the patient — « adressé par ». Null when they came on their own; free text, since the referrer
    /// is normally a practitioner outside this clinic.
    /// </summary>
    public string? ReferredBy { get; set; }

    /// <summary>
    /// Patient-level notes — what to be reminded of on every visit, as opposed to a dental record's notes, which
    /// describe one séance. <see cref="ImportantNotes"/> is rendered highlighted at the top of the patient's file.
    /// </summary>
    public string? Notes { get; set; }

    /// <inheritdoc cref="Notes"/>
    public string? ImportantNotes { get; set; }
    public AddressDto? Address { get; set; }
    public InsuranceInfoDto? InsuranceInfo { get; set; }
    public CnamInfoDto? CnamInfo { get; set; }
    public List<PatientFlagDto> Flags { get; set; } = new();

    /// <summary>
    /// Archived patients are hidden from lists, search, recall and every picker, but keep every record and stay
    /// reachable by direct URL — so a detail page that loads one must be able to say so.
    /// </summary>
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public string? ArchiveReason { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (PostgreSQL <c>xmin</c>). Send it back on the matching update command so
    /// the save is checked against the copy the user actually edited; a peer's change in between then yields
    /// a 409 instead of a silent overwrite.
    /// </summary>
    public uint Version { get; set; }
}
