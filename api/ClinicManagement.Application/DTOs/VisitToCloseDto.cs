using ClinicManagement.Application.Features.Appointments;

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
    public Guid PatientId { get; set; }
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
