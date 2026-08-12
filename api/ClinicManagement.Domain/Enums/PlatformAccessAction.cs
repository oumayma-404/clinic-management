namespace ClinicManagement.Domain.Enums;

/// <summary>
/// What a console account did to a cabinet, as recorded in the console's own access ledger
/// (<c>platform-console</c> FR-5, AC-7.3).
///
/// <para>⚠️ <b>Each member arrives with the write that produces it</b>, never ahead of it — the decision
/// <c>PlatformPortfolioSort</c> made in Part 2 for « par date de fin » (progress.md DEV-6): a member nothing can
/// produce is a value the journal can never show and a filter can never match, and the reader has no way to tell
/// « jamais fait » from « pas encore possible ». Part 6 closed the set with the last two.</para>
/// </summary>
public enum PlatformAccessAction
{
    /// <summary>
    /// A console account opened one cabinet's detail.
    ///
    /// <para>⚠️ Listing cabinets is deliberately <b>not</b> recorded (AC-3.5): one list read touches every
    /// cabinet, so a row per cabinet per page load would drown every reading anyone actually wants — including
    /// this one.</para>
    /// </summary>
    ViewedClinic = 0,

    /// <summary>
    /// A console account recorded a received payment and extended the cabinet's entitlement (Part 4, AC-4.7).
    /// </summary>
    GrantedPeriod = 1,

    /// <summary>
    /// A console account cancelled one ledger entry, with a written reason (Part 5, AC-5.1).
    ///
    /// <para>⚠️ The entry itself is <b>kept</b> — never edited, never deleted (AC-5.2) — so this row records who
    /// struck it through, while the motif and the moment live on the entry the cabinet's own screen shows.</para>
    /// </summary>
    CancelledPeriod = 2,

    /// <summary>
    /// A console account stopped a cabinet recording new work, for a stated reason (Part 6, AC-6.1).
    ///
    /// <para>⚠️ <b>Not a payment state</b> (AC-6.3): suspension is for abuse or fraud and touches no ledger entry,
    /// which is why this row carries no <c>SubscriptionPeriodId</c> — there is none to name.</para>
    /// </summary>
    Suspended = 3,

    /// <summary>
    /// A console account lifted a suspension (Part 6, AC-6.4).
    ///
    /// <para>⚠️ <b>These two rows are the only durable record of a suspension.</b>
    /// <c>ClinicSubscription.Unsuspend</c> clears the motif, the moment and the author off the entitlement — by
    /// design, since a lifted suspension must not keep reading as one — so « qui a suspendu ce cabinet en mars, et
    /// pourquoi ? » is answerable here and nowhere else.</para>
    /// </summary>
    Unsuspended = 4,

    /// <summary>
    /// A console account restored a cabinet from an archive the practice supplied
    /// (<c>clinic-data-archive-and-restore</c>).
    ///
    /// <para>⚠️ <b>The heaviest row in this ledger, and the reason it must exist.</b> This is the one console
    /// action that writes a practice's <i>clinical</i> records rather than its entitlement — and it runs precisely
    /// when the cabinet's own accounts are gone, so nobody at the practice can see it happen. The cabinet's own
    /// « Journal d'activité » carries the individual rows under a <c>restore|</c> actor; this row is what says
    /// <i>which vendor account</i> put them there.</para>
    ///
    /// <para>No <c>SubscriptionPeriodId</c>: no money changed hands and no entitlement was extended.</para>
    /// </summary>
    RestoredClinic = 5
}
