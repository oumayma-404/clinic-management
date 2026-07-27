namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One planned act line as supplied to <see cref="TreatmentPlan.SetItems(System.Collections.Generic.IEnumerable{TreatmentPlanItemInput}, bool)"/>.
/// <para>
/// A record rather than another tuple element: the line already carried six fields, and the same call took a
/// six-element tuple whose members were positional and easy to transpose. This mirrors
/// <c>DentalRecordActInput</c>, introduced for the identical reason on the dental-record path.
/// </para>
/// </summary>
/// <param name="Id">
/// The existing act this line stands for, echoed back by the caller. When it matches a line already on the
/// plan, that line keeps its id, so an <c>Appointment.TreatmentPlanItemId</c> or
/// <c>TreatmentPlanItem.LinkedDentalRecordId</c> pointing at it survives the edit. Null (or unknown) means a
/// new line.
/// </param>
/// <param name="DesignationFr">The act's French designation. Required.</param>
/// <param name="PlannedCost">The agreed fee for the line, in TND.</param>
/// <param name="DentalActCodeId">The CNAM/DCH catalog act this line bills as, when it came from that catalog.</param>
/// <param name="CodeActe">The DCH code snapshotted from that act, so the devis still reads correctly if the catalog changes.</param>
/// <param name="ProcedureTypeId">
/// The clinic's own <see cref="ProcedureType"/> this act is performed as, when the line was chosen from that
/// menu. Carried so booking the act can preselect the procedure — which gives the appointment its colour and
/// default duration, and lets the dental-record modal propose the act when the visit is recorded. Independent
/// of <paramref name="DentalActCodeId"/>: a procedure is a service you schedule and sell, a DCH code is the
/// regulatory code for one clinical situation, and several codes can map to the same procedure.
/// </param>
/// <param name="ToothNumbers">FDI teeth this act targets; empty for a mouth-level act.</param>
public sealed record TreatmentPlanItemInput(
    Guid? Id,
    string DesignationFr,
    decimal PlannedCost,
    Guid? DentalActCodeId,
    string? CodeActe,
    Guid? ProcedureTypeId,
    IReadOnlyList<int> ToothNumbers);
