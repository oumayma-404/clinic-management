using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// « Cette échéance est-elle en retard ? » — asked in one place so no two surfaces can call the same row late
/// and not late.
///
/// <para>
/// ⚠️ <b>Before this, every devis in the database read « En retard », and the badge therefore meant nothing.</b>
/// Measured on the dev database: <b>25 of 27</b> unpaid échéances were flagged, across every status including
/// devis that were <i>facturés</i> (the money lives on the note) and devis that were <i>annulés</i> (nothing is
/// owed at all). The cause is not the badge: <see cref="Entities.TreatmentPlan.Accept"/> raises a single
/// lump-sum échéance for the whole total <b>dated at the acceptance instant</b> when the dentist supplied no
/// schedule — so the row is in the past by the time anyone opens the plan, and « en retard » from the next day.
/// </para>
///
/// <para>
/// That row is a <b>ledger container, not a promise</b>. It exists because a payment needs an échéance to
/// attach to and <c>Outstanding</c> is derived from the schedule; nobody agreed its date, and the create
/// form says so in as many words (« le total est dû à la signature… qui apparaîtra en retard dès demain »).
/// <see cref="Entities.Installment.IsAutoRaised"/> is what lets this rule tell the two apart, which is the
/// whole reason that flag exists: a schedule a dentist actually typed <b>must</b> still go red when the patient
/// misses it, or the feature loses the only thing it is for.
/// </para>
/// </summary>
public static class InstallmentLateness
{
    /// <summary>
    /// True when this échéance is genuinely late — the money is claimable today, a date was agreed for it (or
    /// the work that earns it is finished), and that day has passed.
    /// </summary>
    /// <param name="planStatus">
    /// Gated through <see cref="PlanBillingRules.CarriesDebt"/> rather than by listing statuses again: a
    /// <c>Draft</c> is un-quoted and a <c>Cancelled</c> devis is void, and neither owes anything — which is
    /// precisely why both were absurd to flag.
    /// </param>
    /// <param name="planIsBilled">
    /// A devis a note d'honoraires represents. Its échéancier is dead — the workspace already prints « les
    /// paiements s'enregistrent désormais sur la note d'honoraires » beside these very rows — so calling one
    /// late chases money on a document that can no longer receive it.
    /// </param>
    /// <param name="planHasUnrealisedWork">
    /// Any act of the treatment not yet <c>Done</c>. The dentist's own rule, and the right one: an act billed
    /// once and delivered over six visits is paid as the visits happen, so the balance of a treatment still
    /// under way is <b>not yet due</b> — there is nothing to be late for. Only consulted for an auto-raised
    /// row; a schedule somebody typed says when the money is due regardless of how the work is going.
    /// </param>
    /// <param name="clinicToday">
    /// The clinic's own calendar day (<c>ClinicClock.ClinicToday</c>), never the server's: for the first hour
    /// of every Tunisian day the UTC date is yesterday, and an échéance due today would read « en retard ».
    /// </param>
    public static bool IsLate(
        bool isPaid,
        bool isAutoRaised,
        DateTime dueDate,
        TreatmentPlanStatus planStatus,
        bool planIsBilled,
        bool planHasUnrealisedWork,
        DateTime clinicToday)
    {
        if (isPaid) return false;
        if (!PlanBillingRules.CarriesDebt(planStatus)) return false;
        if (planIsBilled) return false;

        /*
         * ⚠️ **An auto-raised row is never compared against its own date, and it used to be.** Its `DueDate` is
         * the acceptance instant — a value `Accept` had to write because a payment needs somewhere to attach,
         * not a day anybody agreed. Testing it made « en retard » mean « this devis was signed before today »,
         * which is true of every devis, and the `planHasUnrealisedWork` short-circuit was a patch over that
         * rather than the rule itself.
         *
         * The rule, stated once per row kind:
         *   • solde à régler (auto-raised)   — the work is finished and the balance is unpaid.
         *   • échéance convenue (typed)      — the agreed day has passed and the row is unpaid, whatever the
         *                                      clinical progress; a schedule somebody signed says when.
         *
         * The frontend renders an auto-raised row as « Solde à régler » with no date for the same reason: a
         * fabricated date must not be shown, and must not be read.
         */
        if (isAutoRaised)
        {
            return !planHasUnrealisedWork;
        }

        return dueDate.Date < clinicToday.Date;
    }
}
