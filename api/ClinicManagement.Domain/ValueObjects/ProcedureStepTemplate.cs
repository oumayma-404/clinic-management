namespace ClinicManagement.Domain.ValueObjects;

/// <summary>
/// One suggested clinical step of a catalogue act — « Préparation, 45 min ».
/// <para>
/// This is the client's « sous-catégorie », and it is deliberately a <b>template</b> rather than a second
/// catalogue of bookable acts. Making <c>ProcedureType</c> itself hierarchical would force all four of its
/// consumers — <c>AppointmentProcedure</c> (a real FK), <c>TreatmentPlanItem</c>, <c>DentalRecordAct</c> and the
/// fiche→facture bridge — to re-decide « is this bookable? is this priceable? », where a child picked in a fiche
/// under-bills and a parent picked in a continuation séance double-bills, and neither raises an error anywhere.
/// </para>
/// <para>
/// So the catalogue <b>proposes</b> and the devis line <b>owns</b>: these labels are copied onto
/// <c>TreatmentPlanItem.Steps</c> when the act is added to a plan, and are freely edited per case afterwards.
/// That is also the clinically honest shape — a bridge is three séances for one patient and five for another,
/// and a prothèse amovible protocol runs to seven.
/// </para>
/// <para>
/// ⚠️ <b>No price, ever.</b> The fee lives once on <c>TreatmentPlanItem.PlannedCost</c>.
/// </para>
/// </summary>
/// <param name="Label">What is done at this step, in French.</param>
/// <param name="DurationMinutes">
/// Chair time for the step, or null when the practice has not estimated it.
/// <para>
/// ⚠️ Never summed into <c>ProcedureType.DefaultDurationMinutes</c> — the steps happen on different days, so
/// adding them up would treble the agenda block of every bridge.
/// </para>
/// </param>
public sealed record ProcedureStepTemplate(string Label, int? DurationMinutes);
