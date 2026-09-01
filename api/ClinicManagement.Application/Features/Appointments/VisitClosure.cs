using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// Everything <see cref="VisitClosureRules"/> needs about one visit, assembled by the caller from the batched
/// link projections. A parameter object rather than the <c>Appointment</c> entity itself, deliberately: the rule
/// is about a visit <i>and its surroundings</i> (its fiche, its note d'honoraires, its devis), none of which the
/// aggregate can see — and keeping it a plain value is what makes the whole truth table unit-testable with no
/// repository in sight.
/// </summary>
/// <param name="AppointmentId">The visit.</param>
/// <param name="PatientId">Null for a « créneau occupé » — a blocked slot has nothing to close.</param>
/// <param name="Status">Where the visit's own lifecycle has got to.</param>
/// <param name="StartUtc">Slot start.</param>
/// <param name="Duration">Slot length. The end is derived here rather than read from the database — see
/// <c>IAppointmentRepository.GetClosureCandidatesAsync</c> on why the column is unmapped.</param>
/// <param name="HasFiche">At least one <c>DentalRecord</c> names this visit.</param>
/// <param name="FicheCost">The linked fiches' total. Null when there is none. <c>0</c> is meaningful — a
/// contrôle gratuit — and is not the same as « no fiche ».</param>
/// <param name="HasLiveInvoice">A non-<c>Cancelled</c> note d'honoraires names this visit. A cancelled one does
/// not bill it: it would read « facturé » with no money behind it and hide the action to raise a replacement.</param>
/// <param name="CoveredByPlan">The visit carries a devis step, so its money lives on the échéancier.</param>
/// <param name="NothingToBill">Somebody recorded that this visit raises no document.</param>
/// <param name="Disregarded">Somebody took this visit off the worklist without answering any of the three
/// questions — see <c>Appointment.DisregardedAtUtc</c>. Carried in the input rather than tested at the call site
/// so <see cref="VisitClosureRules.IsOnWorklist"/> is the one place that decides, and the truth table can be
/// unit-tested over it like every other term.</param>
public readonly record struct VisitClosureInput(
    Guid AppointmentId,
    Guid? PatientId,
    AppointmentStatus Status,
    DateTime StartUtc,
    TimeSpan Duration,
    bool HasFiche,
    decimal? FicheCost,
    bool HasLiveInvoice,
    bool CoveredByPlan,
    bool NothingToBill,
    bool Disregarded = false)
{
    /// <summary>The first instant the slot no longer covers.</summary>
    public DateTime EndUtc => StartUtc + Duration;
}

/// <summary>
/// The one question to put in front of the user for a visit that is still open.
///
/// <para>The order is the visit's own: somebody came, something was done, something was owed. It is also why
/// this is a single step rather than three flags on a badge row — see <see cref="VisitClosureState.NextStep"/>.</para>
/// </summary>
public enum VisitClosureStep
{
    /// <summary>« Le patient est-il venu ? » — the visit's slot has passed and nobody has said.</summary>
    Presence = 1,

    /// <summary>« Qu'a-t-on fait ? » — the patient came and no fiche de soins records the séance.</summary>
    Fiche = 2,

    /// <summary>« Combien a-t-il payé ? » — work is recorded and no money document exists for it.</summary>
    Billing = 3,
}

/// <summary>
/// How far along a visit is towards being closed. Carries all three answers so the row can draw its progress,
/// and derives the single next question so the row can only ever <i>ask</i> one.
/// </summary>
public readonly record struct VisitClosureState(
    bool PresenceAnswered,
    bool FicheRecorded,
    bool BillingSettled)
{
    /// <summary>
    /// The one question worth asking right now, or <c>null</c> when nothing is left.
    ///
    /// <para><b>The gaps cascade; they do not stack.</b> Three simultaneous red badges on a visit that ended an
    /// hour ago is nagging, and it also asks questions that cannot be answered yet: a visit nobody has confirmed
    /// happened is not « missing » a fiche, and a séance with no fiche has no acts to price. Answering one
    /// question reveals the next, which is the difference between a worklist and a scolding.</para>
    /// </summary>
    public VisitClosureStep? NextStep =>
        !PresenceAnswered ? VisitClosureStep.Presence
        : !FicheRecorded ? VisitClosureStep.Fiche
        : !BillingSettled ? VisitClosureStep.Billing
        : null;

    /// <summary>True when this visit still needs somebody.</summary>
    public bool IsOpen => NextStep is not null;
}

/// <summary>
/// « Quelles séances ne sont pas encore clôturées ? » — the single authority, pure and total.
///
/// <para><b>Nothing here is stored.</b> A visit is open because a record is <i>absent</i>, not because a task row
/// says so — the same choice <c>GetCaisseLedgerQuery</c> made against a <c>CashMovement</c> table, and for the
/// same reason: a table written by each write path disagrees with reality the day one path forgets, and nothing
/// can then say which is right. Here the read cannot drift, because it <i>is</i> the absence.</para>
///
/// <para>It is also why this closes the gap <c>AppointmentProgressJob</c> deliberately leaves: that pass only ever
/// <i>starts</i> a visit, because leaving a slot is not evidence the patient came. So « Terminé » and « Absent »
/// stay human decisions — and something has to ask the human.</para>
/// </summary>
public static class VisitClosureRules
{
    /// <summary>
    /// Whether this visit is in scope at all: its slot has ended, a patient was booked, and it was not cancelled
    /// or already recorded as missed.
    ///
    /// <para><c>Cancelled</c> and <c>NoShow</c> are excluded because both are <b>complete answers</b>, not gaps —
    /// a visit that did not happen needs no fiche and owes no money. Excluding them here rather than treating them
    /// as satisfied is what keeps <see cref="Evaluate"/> honest: it never reports a cancelled visit as « closed »,
    /// which would invite a caller to count it as work done.</para>
    /// </summary>
    public static bool IsClosable(in VisitClosureInput visit, DateTime nowUtc) =>
        visit.PatientId.HasValue
        && visit.Status != AppointmentStatus.Cancelled
        && visit.Status != AppointmentStatus.NoShow
        && visit.EndUtc <= nowUtc;

    /// <summary>
    /// Whether this visit belongs on « À clôturer » <b>right now</b> — closable, and not one somebody has taken
    /// off the list.
    ///
    /// <para><b>Why the disregard test is a separate rule and not folded into <see cref="IsClosable"/>.</b>
    /// « Retirée de la liste » is a statement about the <i>worklist</i>, not about the visit: the séance is still
    /// perfectly closable, and the screen that shows what has been set aside needs to evaluate exactly those rows
    /// — it asks <see cref="IsClosable"/> and then reads <c>Disregarded</c> itself. Collapsing the two would make
    /// « voir les séances retirées » unable to describe its own rows.</para>
    ///
    /// <para>⚠️ This governs the worklist and the dashboard chip, both of which reach it through
    /// <c>VisitClosureReader</c>. It does <b>not</b> govern the appointment status counts behind the taux
    /// d'absence — those are a SQL <c>GROUP BY</c> in <c>CountByStatusBetweenAsync</c>, which excludes
    /// disregarded rows on its own. A mark honoured by the list but not by the figures would take a hundred
    /// phantom visits off the screen and leave the absence rate exactly as wrong as it was.</para>
    /// </summary>
    public static bool IsOnWorklist(in VisitClosureInput visit, DateTime nowUtc) =>
        IsClosable(visit, nowUtc) && !visit.Disregarded;

    /// <summary>
    /// The three answers for one visit. Callers are expected to have filtered on <see cref="IsClosable"/> first;
    /// evaluating an out-of-scope visit is harmless but meaningless.
    /// </summary>
    public static VisitClosureState Evaluate(in VisitClosureInput visit)
    {
        // Presence is answered by exactly one status. Scheduled / Confirmed / InProgress past their own slot all
        // mean the same thing — nobody has said — and InProgress is the common one, because the minutely pass
        // auto-starts every visit and nothing has ever closed one.
        var presenceAnswered = visit.Status == AppointmentStatus.Completed;

        var ficheRecorded = visit.HasFiche;

        // Three derivations before the recorded escape hatch, in the order they are cheap to be sure of.
        //   · a live note d'honoraires  → billed
        //   · a devis step on the visit → the money is on the échéancier, and counting it here would double it
        //   · a fiche worth nothing     → a contrôle gratuit is complete work with no document to raise
        // The recorded mark is last precisely because it is the one a human had to type.
        var billingSettled =
            visit.HasLiveInvoice
            || visit.CoveredByPlan
            || visit.FicheCost is { } cost && cost <= 0m
            || visit.NothingToBill;

        return new VisitClosureState(presenceAnswered, ficheRecorded, billingSettled);
    }
}
