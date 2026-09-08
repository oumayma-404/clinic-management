using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One recorded condition on a tooth (child-of-patient; no <c>ClinicId</c> — tenant isolation is via the
/// owning <see cref="Patient"/>). A tooth can have MANY of these across time. A <see cref="ToothStateSource.Treatment"/>
/// entry is produced by a <see cref="DentalRecord"/> (the session in which the doctor recorded a completed act)
/// and carries that record's date; a <see cref="ToothStateSource.Diagnosis"/> entry is charted directly on the
/// odontogram before treatment and has no source record. The patient's odontogram is the accumulation of these
/// entries; deleting a source record cascades its treatment entries away, while diagnosis entries persist until
/// treated or explicitly removed.
/// </summary>
public class ToothState : Entity<Guid>, IAuditable
{
    public Guid PatientId { get; private set; }

    /// <summary>The owning clinic, denormalised from the patient. See <see cref="PatientMedicalHistory.ClinicId"/>.</summary>
    public Guid ClinicId { get; private set; }

    public int ToothNumber { get; private set; }
    public ToothCondition Condition { get; private set; }
    /// <summary>Whether this entry is a charted diagnosis or a completed treatment (from a dental record).</summary>
    public ToothStateSource Source { get; private set; }
    /// <summary>Affected surfaces, a subset of <c>MODVL</c> (Mésial/Occlusal/Distal/Vestibulaire/Lingual); optional.</summary>
    public string? Surfaces { get; private set; }
    public string? Note { get; private set; }
    /// <summary>The dental record (session) that recorded this treatment. Null for diagnosis / legacy entries.</summary>
    public Guid? DentalRecordId { get; private set; }

    /// <summary>
    /// <b>Which bridge this tooth belongs to</b> — an opaque grouping token shared by every row of one bridge,
    /// and the answer to « where does this bridge begin and where does it end? ».
    ///
    /// <para>⚠️ <b>Not a foreign key, and not inferrable.</b> The odontogramme used to join a travée across
    /// bridge-marked teeth by <i>arch adjacency</i> — which merged two bridges placed side by side into one bar
    /// and, worse, propagated one bridge's « still to place » status onto the finished crown beside it.
    /// <see cref="Services.BridgeCharting"/>'s own summary already says a bridge's shape cannot be read off
    /// position; its <i>extent</i> cannot either, and this is what states it instead.</para>
    ///
    /// <para>⚠️ <b>Null is a first-class value, and the chart must keep drawing those.</b> Every row written
    /// before this column existed carries null, and so does a bridge recorded one tooth at a time; those fall
    /// back to the adjacency scan and render exactly as they always did. The migration deliberately does
    /// <b>not</b> backfill — inferring a group from adjacency would freeze today's wrong answer into data,
    /// where nothing can ever correct it.</para>
    ///
    /// <para>⚠️ <b>Only a bridge may carry one</b> (the constructor folds it away otherwise), and a group is
    /// minted only for an act with <b>two or more</b> teeth — see <c>DentalRecordActParser.BuildToothStates</c>
    /// for why a one-tooth act must stay ungrouped.</para>
    /// </summary>
    public Guid? BridgeGroupId { get; private set; }
    /// <summary>Date the treatment was carried out (the source record's intervention date), or the diagnosis date.</summary>
    public DateTime TreatmentDate { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ToothState() { } // For EF Core

    public ToothState(
        Guid id,
        Guid patientId,
        Guid clinicId,
        int toothNumber,
        ToothCondition condition,
        DateTime treatmentDate,
        string? surfaces = null,
        string? note = null,
        Guid? dentalRecordId = null,
        ToothStateSource source = ToothStateSource.Treatment,
        Guid? bridgeGroupId = null)
    {
        if (patientId == Guid.Empty)
            throw new ArgumentException("Le patient est requis.", nameof(patientId));
        if (!FdiTooth.IsValid(toothNumber))
            throw new ArgumentException($"Numéro de dent invalide : {toothNumber}.", nameof(toothNumber));

        Id = id;
        PatientId = patientId;
        ClinicId = clinicId;
        ToothNumber = toothNumber;
        Condition = condition;
        Source = source;
        Surfaces = NormalizeSurfaces(surfaces);
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        TreatmentDate = treatmentDate;
        DentalRecordId = dentalRecordId;
        /*
         * ⚠️ **A group may never describe a non-bridge**, and this folds rather than refusing — the same shape
         * as `DentalRecordAct`'s pontique intersect and for the same reason: the caller legitimately holds a
         * stale answer. A dentist changes an act's état from « Bridge » to « Couronne » and the group id is
         * still in flight; throwing would turn an ordinary edit into a 400 nobody earned, while keeping it
         * would let `bridge-runs.ts` draw a travée between two crowns.
         */
        BridgeGroupId = BridgeCharting.IsUnit(condition) ? bridgeGroupId : null;
        CreatedAt = DateTime.UtcNow;
    }

    // ⚠️ Delegates to ToothSurfaces: this method was byte-for-byte identical in ToothState and
    // DentalRecordAct, two entry points for the same string from the same picker.
    private static string? NormalizeSurfaces(string? surfaces) =>
        ToothSurfaces.Normalize(surfaces, nameof(surfaces));
}
