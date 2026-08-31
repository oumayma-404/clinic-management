using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

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

    /// <summary>
    /// The same number in E.164, or null when it is not a deliverable Tunisian one.
    /// <para>
    /// ⚠️ <b>This is what decides whether a WhatsApp action exists on a patient row</b>, and it is resolved
    /// <b>server-side</b> through <see cref="PhoneNumber.ToE164"/> for the reason <see cref="SupplierDto"/>
    /// states: the browser holds a mirror of that rule in <c>lib/phone.ts</c>, and a second copy deciding
    /// whether a link appears is how a patient becomes contactable on one screen and not on another.
    /// </para>
    /// </summary>
    public string? PhoneE164 { get; set; }
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

    /// <summary>
    /// Set when the Google Calendar import created this record from an event title alone and nobody has confirmed
    /// it yet. Drives the fiche's « à compléter » banner and the patients list's own filter.
    /// </summary>
    public DateTime? CalendarImportPendingReviewSince { get; set; }

    /// <summary>
    /// The existing patient this imported record is probably a duplicate of, resolved on read — or null, which is
    /// the ordinary case. Null also when the id no longer resolves (the suggested patient was deleted), because a
    /// question that has expired must read as no question rather than as a broken row.
    /// </summary>
    public SuggestedDuplicateDto? SuggestedDuplicate { get; set; }

    /// <summary>
    /// Whether the patient agreed to automated SMS/WhatsApp reminders — <c>"NotRecorded"</c>,
    /// <c>"Granted"</c> or <c>"Refused"</c>. The two stamps below say who took the answer and when; a consent
    /// nobody can date is not one a cabinet can defend.
    ///
    /// <para>⚠️ <b>A string, for <see cref="Dentition"/>'s reason, and I got this wrong first time.</b> This API
    /// registers no <c>JsonStringEnumConverter</c>, so a raw enum property leaves as <c>0</c>/<c>1</c>/<c>2</c>.
    /// The browser then compares an integer against <c>"Refused"</c>, never matches, and the control shows
    /// « non renseigné » over every stored answer while a write of <c>"Refused"</c> is refused as a 400 — both
    /// silent, and neither visible to <c>tsc</c>, the unit suite or <c>check:responsive</c>. Only a real request
    /// showed it.</para>
    /// </summary>
    public string ReminderConsent { get; set; } = nameof(PatientReminderConsent.NotRecorded);
    public DateTime? ReminderConsentRecordedAtUtc { get; set; }
    public string? ReminderConsentRecordedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (PostgreSQL <c>xmin</c>). Send it back on the matching update command so
    /// the save is checked against the copy the user actually edited; a peer's change in between then yields
    /// a 409 instead of a silent overwrite.
    /// </summary>
    public uint Version { get; set; }
}

/// <summary>
/// « S'agit-il de ce patient ? » — the existing record a calendar-imported fiche is probably a duplicate of.
///
/// <para>Carries the two things that let a human answer rather than guess: the birth date and the phone. The
/// names are near-identical by construction, so the name alone cannot separate « Imen Nasri » from
/// « Iman Nasri » — and « Oui » deletes a record.</para>
/// </summary>
public class SuggestedDuplicateDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string? Phone { get; set; }

    /// <summary>
    /// True when both records carry the same phone number — the strongest confirmation available here, and worth
    /// saying out loud. A <b>different</b> phone never reaches this DTO: it vetoes the suggestion at import.
    /// </summary>
    public bool PhoneMatches { get; set; }
}
