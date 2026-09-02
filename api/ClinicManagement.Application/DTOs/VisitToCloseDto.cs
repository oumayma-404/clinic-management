using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Domain.Common;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One séance still waiting for somebody — a row of « À clôturer ».
///
/// <para>It carries all three answers <b>and</b> the single next question. The booleans draw the row's progress;
/// <see cref="NextStep"/> is what the row may <i>ask</i>. Sending only the booleans would leave each client to
/// re-derive the cascade, and a second copy of « ask presence before the fiche » is how one surface starts
/// nagging about a fiche for a visit nobody has confirmed happened.</para>
/// </summary>
public class VisitToCloseDto
{
    public Guid AppointmentId { get; set; }

    /// <summary>
    /// Null for a « créneau occupé » — a blocked slot has no patient.
    ///
    /// <para>Only ever null in the <b>retirées</b> half: a blocked slot has nothing to close, so it never reaches
    /// the worklist, but « Supprimer (créé par erreur) » can retire one from the agenda and that list is the only
    /// way back. A client must therefore not build a <c>/patients/{id}</c> link from this without testing it.</para>
    /// </summary>
    public Guid? PatientId { get; set; }

    /// <summary>The patient's name, or « Créneau occupé » when <see cref="PatientId"/> is null.</summary>
    public string PatientName { get; set; } = string.Empty;

    /// <summary>Slot start, UTC. The client renders it in the clinic's own day.</summary>
    public DateTime AppointmentDateTime { get; set; }

    public int DurationMinutes { get; set; }

    /// <summary>The visit's practitioner, when it names one. Null is honest — many bookings carry none.</summary>
    public Guid? DoctorId { get; set; }
    public string? DoctorName { get; set; }

    /// <summary>The acts the séance was booked for, in the dentist's own order. Empty for a booking with none.</summary>
    public List<string> Procedures { get; set; } = new();

    /// <summary>The visit's own status, so the row can say « En cours » rather than only « à confirmer ».</summary>
    public string Status { get; set; } = string.Empty;

    public bool PresenceAnswered { get; set; }
    public bool FicheRecorded { get; set; }
    public bool BillingSettled { get; set; }

    /// <summary>The one question to put in front of the user: <c>Presence</c> | <c>Fiche</c> | <c>Billing</c>.</summary>
    public string NextStep { get; set; } = nameof(VisitClosureStep.Presence);

    /// <summary>
    /// A fiche already recorded for this visit, so « Ajouter la fiche » can become « Ouvrir la fiche ». Null when
    /// none — and when several exist this names the first, because the action opens the séance's record, not a
    /// list of them.
    /// </summary>
    public Guid? DentalRecordId { get; set; }

    /// <summary>The note d'honoraires billing this visit, when one does. Mirrors <c>AppointmentDto</c>'s pair.</summary>
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }

    /// <summary>
    /// Why this visit raises no document, when somebody said so. Present means the money question is closed by a
    /// recorded decision rather than by a derivation, which is exactly the difference a reader needs — one is a
    /// fact about the work, the other is a fact about a colleague.
    /// </summary>
    public string? NothingToBillReason { get; set; }
    public DateTime? NothingToBillAtUtc { get; set; }
}

/// <summary>
/// « À clôturer », as the screen reads it: a page of séances plus the standing count of the ones somebody has
/// taken off the list.
///
/// <para><b>Why the count rides on the page instead of being its own endpoint.</b> The worklist needs it to offer
/// the way back (« 143 séances retirées — les afficher »), and the set-aside screen needs it to describe what it
/// is showing. Both come out of the one <c>VisitClosureWorklist</c> read, so the two halves are complements of
/// each other by construction; a second endpoint would be a second predicate over the same window, and the first
/// term either side gained would make them overlap or leave a gap with nothing to notice it.</para>
/// </summary>
public class VisitsToCloseDto
{
    public PagedResult<VisitToCloseDto> Visits { get; set; } = PagedResult<VisitToCloseDto>.Empty();

    /// <summary>
    /// How many séances in the same window carry <c>Appointment.DisregardedAtUtc</c>. Always the set-aside count,
    /// whichever half <c>Visits</c> holds — it is a fact about the window, not about the page.
    /// </summary>
    public int DisregardedCount { get; set; }
}
