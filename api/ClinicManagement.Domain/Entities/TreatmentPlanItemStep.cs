using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One clinical step of a <see cref="TreatmentPlanItem"/> (aggregate child of <see cref="TreatmentPlan"/>) —
/// « Préparation », « Empreinte », « Scellement définitif ».
/// <para>
/// An implant, a bridge or a couronne is a <b>process</b>, and the model used to hold it as an event: a devis
/// line was <c>Planned</c> or <c>Done</c>, with nothing between, so « il reste le scellement » could only be
/// typed into the notes. These rows are that missing middle.
/// </para>
/// <para>
/// ⚠️ <b>A step carries no money, and never will.</b> The price lives once on
/// <see cref="TreatmentPlanItem.PlannedCost"/>, the échéancier collects it across the séances, and one facture
/// bills it. Splitting a tariff across steps would put the same act on several invoice lines — which
/// <c>CnamBillingCalculator</c>'s ceiling read, applying no per-line cap, would then count once per line.
/// </para>
/// <para>
/// ⚠️ <b>Each step holds its own <see cref="LinkedDentalRecordId"/>.</b> That is the whole point:
/// <see cref="TreatmentPlanItem.MarkDone"/> refuses a second, different record, so before this a devis line
/// could never record work in more than one fiche de soins. The line now spans N fiches because each step
/// answers for one.
/// </para>
/// </summary>
public class TreatmentPlanItemStep : Entity<Guid>
{
    /// <summary>
    /// Upper bound on the steps of one act. Not a clinical rule — a prothèse amovible protocol runs to seven
    /// séances — but a guard on an unbounded list, and the same figure
    /// <c>AppointmentProcedureSelection.MaxProceduresPerAppointment</c> uses for the same reason.
    /// </summary>
    public const int MaxStepsPerItem = 12;

    /// <summary>Longest a step label may be. Shorter than the line's own désignation: it is read in a strip.</summary>
    public const int MaxLabelLength = 120;

    /// <summary>
    /// Longest interval a protocol may impose between two steps — three years, which covers the whole of an
    /// orthodontic course (the shipped protocol states its active phase as a single 540-day wait) and still
    /// refuses a mistyped figure. See <see cref="GuardInterval"/> for why it is not one year.
    /// </summary>
    public const int MaxDaysBetweenSteps = 1095;

    public Guid TreatmentPlanItemId { get; private set; }

    /// <summary>What is done at this step, in French, as the dentist words it.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>Clinical order within the act (0-based, dense). Held dense by <c>verify-schema</c>.</summary>
    public int SequenceNumber { get; private set; }

    /// <summary>When this step was carried out; null while it is still to come.</summary>
    public DateTime? DoneDate { get; private set; }

    /// <summary>
    /// The fiche de soins that evidences this step. A <b>soft reference</b> — deliberately no FK, exactly like
    /// <see cref="TreatmentPlanItem.LinkedDentalRecordId"/>, and for the same reason plus one more:
    /// <c>DentalRecord.SetActs</c> rebuilds every act with a fresh id, so nothing downstream may hold a key
    /// into a record's children.
    /// </summary>
    public Guid? LinkedDentalRecordId { get; private set; }

    /// <summary>
    /// How long this step takes at the chair, when known — what a séance booked for it should last.
    /// <para>
    /// ⚠️ Never summed into <see cref="ProcedureType.DefaultDurationMinutes"/>. The steps happen on different
    /// days; adding them would triple the agenda block of every bridge.
    /// </para>
    /// </summary>
    public int? EstimatedDurationMinutes { get; private set; }

    /// <summary>
    /// Calendar days that must elapse after the <b>previous</b> step before this one can be carried out, or null
    /// when the interval is clinically free.
    /// <para>
    /// ⚠️ A different quantity from <see cref="EstimatedDurationMinutes"/>: that one is chair time, this one is
    /// waiting time. Holding only the first is what let a correctly-progressing implant read as abandoned —
    /// osseointegration is eight to twelve weeks, and with no interval to compare against the worklist alarmed
    /// on a flat fortnight.
    /// </para>
    /// <para>
    /// Copied from the catalogue's <c>ProcedureStepTemplate</c> when the act joins a devis and then <b>owned by
    /// the devis</b>, exactly like the label and the chair time: a protocol improved in the catalogue must not
    /// move under a quote the patient has signed.
    /// </para>
    /// </summary>
    public int? MinDaysAfterPrevious { get; private set; }

    public bool IsDone => DoneDate.HasValue;

    /// <summary>
    /// The earliest date this step should be carried out, given when the previous one was — or null when either
    /// the interval or that date is unknown, which is the ordinary case and means « no opinion ».
    /// </summary>
    /// <remarks>
    /// The previous step's date is a <b>parameter</b>: a step cannot see its siblings, and reaching back through
    /// a navigation to find them is the unloaded-collection trap this solution has already been bitten by.
    /// </remarks>
    public DateTime? DueFrom(DateTime? previousStepDoneOn) =>
        MinDaysAfterPrevious is int days && previousStepDoneOn is DateTime from
            ? from.Date.AddDays(days)
            : null;

    private TreatmentPlanItemStep() { } // For EF Core

    public TreatmentPlanItemStep(
        Guid id,
        Guid treatmentPlanItemId,
        string label,
        int sequenceNumber,
        int? estimatedDurationMinutes = null,
        int? minDaysAfterPrevious = null)
    {
        Id = id;
        TreatmentPlanItemId = treatmentPlanItemId;
        Label = NormalizeLabel(label);
        SequenceNumber = GuardSequence(sequenceNumber);
        EstimatedDurationMinutes = GuardDuration(estimatedDurationMinutes);
        MinDaysAfterPrevious = GuardInterval(minDaysAfterPrevious);
    }

    /// <summary>
    /// Correct the step's label or its chair time in place, keeping its id — so an
    /// <c>AppointmentProcedure.TreatmentPlanItemStepId</c> pointing at it, and its own evidence link, survive
    /// the edit.
    /// <para>
    /// Deliberately does not touch <see cref="DoneDate"/> or <see cref="LinkedDentalRecordId"/>: renaming
    /// « Empreinte » to « Empreinte définitive » is a clerical correction, not a statement that the step
    /// un-happened. Mirrors <see cref="TreatmentPlanItem.Revise"/>, including that it is allowed on a step
    /// already carried out.
    /// </para>
    /// </summary>
    internal void Revise(string label, int? estimatedDurationMinutes, int? minDaysAfterPrevious)
    {
        Label = NormalizeLabel(label);
        EstimatedDurationMinutes = GuardDuration(estimatedDurationMinutes);
        MinDaysAfterPrevious = GuardInterval(minDaysAfterPrevious);
    }

    /// <summary>
    /// Record that this step was carried out, linking the fiche that evidences it.
    /// <para>
    /// Re-linking the <b>same</b> record is a no-op, because a fiche can legitimately be saved twice. Linking a
    /// <b>different</b> one is refused, for the reason <see cref="TreatmentPlanItem.MarkDone"/> gives: it would
    /// rewrite clinical history, claiming the step happened at a visit it did not.
    /// </para>
    /// </summary>
    internal void MarkDone(DateTime doneOn, Guid? linkedDentalRecordId)
    {
        if (IsDone)
        {
            if (linkedDentalRecordId == null || linkedDentalRecordId == LinkedDentalRecordId)
            {
                return;
            }

            throw new InvalidOperationException(
                $"L'étape « {Label} » est déjà réalisée et rattachée à une autre fiche de soins. "
                + "Détachez-la de cette fiche avant de la rattacher à une nouvelle.");
        }

        DoneDate = doneOn;
        LinkedDentalRecordId = linkedDentalRecordId;
    }

    /// <summary>
    /// Undo <see cref="MarkDone"/>. Returns <c>false</c> when the step was already « à venir », so the caller
    /// can tell "nothing to undo" from a real correction.
    /// </summary>
    internal bool Unmark()
    {
        if (!IsDone)
        {
            return false;
        }

        DoneDate = null;
        LinkedDentalRecordId = null;
        return true;
    }

    internal void SetSequenceNumber(int sequenceNumber) => SequenceNumber = GuardSequence(sequenceNumber);

    private static string NormalizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Le libellé de l'étape est requis.", nameof(label));
        }

        var trimmed = label.Trim();
        if (trimmed.Length > MaxLabelLength)
        {
            throw new ArgumentException(
                $"Le libellé d'une étape ne peut pas dépasser {MaxLabelLength} caractères.", nameof(label));
        }

        return trimmed;
    }

    private static int GuardSequence(int sequenceNumber) =>
        sequenceNumber < 0
            ? throw new ArgumentException("La position d'une étape ne peut pas être négative.", nameof(sequenceNumber))
            : sequenceNumber;

    // Same band as ProcedureType.DefaultDurationMinutes: a step is one sitting at the chair, and 0 or 8 hours
    // are both nonsense. Null is the ordinary case — nobody has to estimate a step to use one.
    private static int? GuardDuration(int? minutes)
    {
        if (minutes is null)
        {
            return null;
        }
        if (minutes is <= 0)
        {
            throw new ArgumentException(
                "La durée d'une étape doit être supérieure à 0 minute.", nameof(minutes));
        }
        if (minutes is >= 480)
        {
            throw new ArgumentException(
                "La durée d'une étape doit être inférieure à 480 minutes (8 heures).", nameof(minutes));
        }

        return minutes;
    }

    /// <summary>
    /// The one authority for how long an interval may be. 0 is refused because « the same day » is what null
    /// already says, and the ceiling is three years — wide enough for the longest protocol in dentistry and
    /// still narrow enough to catch a mistyped figure.
    /// <para>
    /// ⚠️ It was a year, on a comment asserting « the longest is twelve weeks », and the shipped catalogue
    /// already contradicted that: orthodontics states its **active phase** as one 540-day interval rather than
    /// twenty activation rows, which is what stops the worklist reporting an ortho case running exactly to plan
    /// as forgotten. The cap therefore made the longest treatment in dentistry unrepresentable — and it went
    /// unnoticed because <c>ProcedureType.ValidateSteps</c> was silently dropping every interval before this
    /// guard ever saw one. Carrying the value through turned that into an exception that took the whole
    /// « Charger les actes courants » run down.
    /// </para>
    /// <para>
    /// Reachable from <c>ProcedureType.ValidateSteps</c> so the catalogue's suggested protocol and the plan's
    /// own steps are held to the same band — the alternative is two bands that drift, and an interval a
    /// clinic can type into the catalogue but never into the devis it produces.
    /// </para>
    /// </summary>
    internal static int? GuardInterval(int? days)
    {
        if (days is null)
        {
            return null;
        }
        if (days is <= 0)
        {
            throw new ArgumentException("Le délai entre deux étapes doit être supérieur à 0 jour.", nameof(days));
        }
        if (days > MaxDaysBetweenSteps)
        {
            throw new ArgumentException(
                $"Le délai entre deux étapes ne peut pas dépasser {MaxDaysBetweenSteps} jours.", nameof(days));
        }

        return days;
    }
}

/// <summary>
/// One step as supplied to <c>TreatmentPlan.SetItemSteps</c>. A record rather than a tuple, for the reason
/// <see cref="TreatmentPlanItemInput"/> gives: positional members of the same primitive type transpose silently.
/// </summary>
/// <param name="Id">
/// The existing step this line stands for, echoed back by the caller. When it matches a step already on the
/// act, that step keeps its id — so its <see cref="TreatmentPlanItemStep.DoneDate"/>, its evidence link and any
/// <c>AppointmentProcedure.TreatmentPlanItemStepId</c> pointing at it survive the edit. Null means a new step.
/// </param>
/// <param name="Label">What is done at this step. Required.</param>
/// <param name="EstimatedDurationMinutes">Chair time for the step, or null when nobody has estimated it.</param>
/// <param name="MinDaysAfterPrevious">
/// Calendar days to wait after the previous step, or null when the interval is clinically free — a different
/// quantity from the chair time above, see <see cref="TreatmentPlanItemStep.MinDaysAfterPrevious"/>.
/// </param>
public sealed record TreatmentPlanItemStepInput(
    Guid? Id,
    string Label,
    int? EstimatedDurationMinutes,
    int? MinDaysAfterPrevious = null);
