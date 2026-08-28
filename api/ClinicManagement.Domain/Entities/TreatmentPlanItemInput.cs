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
/// <param name="ProcedureTypeId">
/// The clinic's own <see cref="ProcedureType"/> this act is performed as — <b>the only catalog a devis line
/// comes from</b>. Carried so booking the act can preselect the procedure, which gives the appointment its
/// colour and default duration and lets the dental-record modal propose the act when the visit is recorded.
/// <para>
/// ⚠️ A line used to be able to carry a <c>DentalActCodeId</c> + <c>CodeActe</c> from the DCH catalog instead,
/// and that reference was what <c>CnamBillingCalculator</c> read to split a devis into « part CNAM » and « à
/// votre charge ». <b>Both are gone, and the CNAM split on a devis went with them</b> — a deliberate decision:
/// a devis is built from the services the practice sells, and asking the person writing one to also pick a
/// regulatory code put two catalogs in front of them for one line. The DCH catalog stays where it is genuinely
/// used, the bulletin CNAM BS1. Nothing carries a code into an invoice created from a devis any more either.
/// </para>
/// </param>
/// <param name="ToothNumbers">FDI teeth this act targets; empty for a mouth-level act.</param>
public sealed record TreatmentPlanItemInput(
    Guid? Id,
    string DesignationFr,
    decimal PlannedCost,
    Guid? ProcedureTypeId,
    IReadOnlyList<int> ToothNumbers);
