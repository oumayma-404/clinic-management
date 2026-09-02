using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One act booked into an <see cref="Appointment"/> (aggregate child). A séance is very often several acts —
/// « détartrage + deux obturations » is one visit, not three — and before this existed an appointment held a
/// single <c>ProcedureTypeId</c>, so the second and third acts could only be typed into the notes: invisible to
/// the colour, to the duration, to the fiche de soins proposal and to the devis read-back.
/// <para>
/// The parent keeps <c>ProcedureTypeId</c>/<c>ProcedureDurationMinutes</c>/<c>ProcedureColorHex</c> as a
/// **derived snapshot of the first row** rather than dropping them. That is deliberate: the agenda paints a card
/// one colour, `ProcedureType.Appointments` is a real FK relationship, and every existing read (calendar, patient
/// page, Google sync, `IsUsedByFutureAppointments`) keys off those columns. Making the collection authoritative
/// and the scalars derived means none of them had to learn about a list to keep being correct.
/// </para>
/// </summary>
public class AppointmentProcedure : Entity<Guid>
{
    public Guid AppointmentId { get; private set; }

    /// <summary>
    /// The catalog act. Nullable with a <c>SetNull</c> FK, matching the parent column: retiring a procedure from
    /// « Mes actes » must never block or cascade into a booked visit, and <see cref="ProcedureName"/> is what
    /// keeps the row readable afterwards.
    /// </summary>
    public Guid? ProcedureTypeId { get; private set; }

    /// <summary>Name snapshot, so a row whose procedure was later retired still names the act.</summary>
    public string? ProcedureName { get; private set; }

    /// <summary>The act's own duration at booking time — what the appointment's total duration is summed from.</summary>
    public int? DurationMinutes { get; private set; }

    public string? ColorHex { get; private set; }

    /// <summary>
    /// The price agreed for this act at this visit, or <b>null</b> when nothing was negotiated and the
    /// catalogue's own tarif stands. Null is the ordinary case and what every row written before this existed
    /// means, which is why the absence is modelled rather than a 0 written in its place — a negotiated 0 (an act
    /// offered) is a real answer and must stay distinguishable from « personne n'a négocié ».
    ///
    /// <para>⚠️ It is a <b>forfait for the act</b>, never a per-tooth rate. Teeth are not known when a visit is
    /// booked, so a unit price could not be turned back into the total the patient was quoted on the phone: told
    /// « 120 DT » for two extractions, a per-tooth reading bills 240. The fiche de soins therefore reopens such an
    /// act as a forfait at exactly this figure.</para>
    ///
    /// <para>This is the one thing about an act the <b>client</b> tells the server rather than the catalogue —
    /// see <c>AppointmentProcedureRequest.AgreedCost</c>, which explains why, and validates it.</para>
    /// </summary>
    public decimal? AgreedCost { get; private set; }

    /// <summary>
    /// The treatment-plan act this line carries out, if any. **This is the field that makes grouping work**: two
    /// devis acts booked into one séance are two rows here pointing at two plan items, so the plan's per-act état
    /// resolves for both instead of only for whichever one won the parent's single scalar.
    /// </summary>
    public Guid? TreatmentPlanItemId { get; private set; }

    /// <summary>Order within the séance (0-based) — the order the dentist listed the acts in.</summary>
    public int SequenceNumber { get; private set; }

    /// <summary>Navigation to the live catalog entry, so a read can prefer the current name/colour over the snapshot.</summary>
    public ProcedureType? ProcedureType { get; private set; }

    private AppointmentProcedure() { } // For EF Core

    public AppointmentProcedure(
        Guid id,
        Guid appointmentId,
        Guid? procedureTypeId,
        string? procedureName,
        int? durationMinutes,
        string? colorHex,
        decimal? agreedCost,
        Guid? treatmentPlanItemId,
        int sequenceNumber)
    {
        if (procedureTypeId == null && string.IsNullOrWhiteSpace(procedureName))
        {
            throw new ArgumentException(
                "Un acte du rendez-vous doit référencer une procédure ou porter un libellé.", nameof(procedureName));
        }
        if (durationMinutes is <= 0)
        {
            throw new ArgumentException("La durée d'un acte doit être supérieure à 0 minute.", nameof(durationMinutes));
        }
        if (sequenceNumber < 0)
        {
            throw new ArgumentException("La position de l'acte ne peut pas être négative.", nameof(sequenceNumber));
        }
        // Zero is allowed — an act offered is a negotiation, and only a negative price is nonsense.
        if (agreedCost is < 0)
        {
            throw new ArgumentException("Le prix convenu ne peut pas être négatif.", nameof(agreedCost));
        }

        Id = id;
        AppointmentId = appointmentId;
        ProcedureTypeId = procedureTypeId;
        ProcedureName = string.IsNullOrWhiteSpace(procedureName) ? null : procedureName.Trim();
        DurationMinutes = durationMinutes;
        ColorHex = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex.Trim();
        AgreedCost = agreedCost;
        TreatmentPlanItemId = treatmentPlanItemId;
        SequenceNumber = sequenceNumber;
    }

    /// <summary>
    /// Re-snapshot this row from its (renamed / recoloured) catalog entry. Used by
    /// <c>UpdateProcedureTypeCommand</c>, which already refreshed the parent's scalars — a child row left behind
    /// would make the same act show two names on the same screen.
    /// </summary>
    public void RefreshSnapshot(string? procedureName, string? colorHex)
    {
        if (!string.IsNullOrWhiteSpace(procedureName))
        {
            ProcedureName = procedureName.Trim();
        }
        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            ColorHex = colorHex.Trim();
        }
    }
}

/// <summary>
/// Parameter object for <c>Appointment.SetProcedures</c> — a positional tuple of five nullable values is exactly
/// where a colour ends up in the name slot. Same shape and reasoning as <c>DentalRecordActInput</c>.
/// </summary>
public record AppointmentProcedureInput(
    Guid? ProcedureTypeId,
    string? ProcedureName,
    int? DurationMinutes,
    string? ColorHex,
    decimal? AgreedCost,
    Guid? TreatmentPlanItemId);
