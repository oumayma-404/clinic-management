namespace ClinicManagement.Application.Features.Platform;

/// <summary>
/// The <b>single</b> place the console admits it cannot yet see a cabinet's subscription
/// (<c>platform-console</c> Part 2, step 5).
///
/// <para><b>Why a named type rather than four nulls scattered through a query.</b> Entitlement is
/// <c>features/clinic-subscription/</c>'s to own (FR-4) and it has not shipped on this branch. The console must
/// therefore render a state column with nothing behind it, and the two ways to do that are very different: a
/// handful of <c>null</c>s spread across the read and the screen, which Part 4 would have to hunt down and
/// which each look like an ordinary missing value; or one clearly-named thing that Part 4 <b>deletes</b>. The
/// second is the whole point — the compiler then lists every caller that has to change, and « is this null
/// because there is no subscription, or because we cannot see subscriptions? » has one answer.</para>
///
/// <para>⚠️ <b>This is not a fold and must never become one.</b> The temptation, once the entitlement tables
/// exist, is to widen this into a console-side « is it still valid? » computation. That would be exactly the
/// FR-4 violation this feature is defined around: two implementations of what a cabinet is entitled to, one of
/// which decides whether the clinic app locks and the other of which decides what the vendor is told. Part 4
/// removes this file and calls the companion's own read.</para>
///
/// <para>⚠️ It is also why the list's « en essai / expire sous N j / expiré / suspendu » filters and the
/// « par date de fin » sort do not exist yet: a filter that silently matches nothing is worse than a filter
/// that is not offered, and the screen hides them off <see cref="DataAvailable"/> rather than guessing.</para>
/// </summary>
public static class PlatformSubscriptionPlaceholder
{
    /// <summary>
    /// False for as long as this file exists. Reported to the console as <c>subscriptionDataAvailable</c> so the
    /// screen states the gap instead of rendering « — » that reads as « aucun abonnement ».
    /// </summary>
    public const bool DataAvailable = false;

    /// <summary>What the state column shows meanwhile. One character, and the screen explains it beside the table.</summary>
    public const string UnknownLabel = "—";

    /// <summary>
    /// The sentence the screen shows, in French, once per screen. Server-side for the reason every other refusal
    /// wording in this codebase is: two copies of an explanation drift, and this one has to disappear in Part 4
    /// rather than survive in a browser constant nobody greps.
    /// </summary>
    public const string Explanation =
        "Les abonnements ne sont pas encore gérés depuis cette console : la colonne « État » et les filtres "
        + "correspondants arriveront avec la gestion des abonnements. Les compteurs d'activité ci-dessous sont réels.";

    /// <summary>
    /// The same admission on one cabinet's detail, where the gap is wider: AC-3.2's payment history has nowhere to
    /// come from either.
    ///
    /// <para>⚠️ <b>Saying it is the whole point; an empty « Historique des paiements » section is not the same
    /// statement.</b> A table with no rows asserts that this cabinet has never paid — a claim about the cabinet —
    /// whereas the truth is a claim about the console. It is also why no end date is shown here at all rather than
    /// « n'expire jamais » (EC-14): until the entitlement ledger exists, « sans échéance » and « nous ne pouvons
    /// pas le lire » are indistinguishable, and the second is the one that is true today.</para>
    /// </summary>
    public const string DetailExplanation =
        "Les abonnements ne sont pas encore gérés depuis cette console : ni l'état, ni la date de fin, ni "
        + "l'historique des paiements de ce cabinet ne sont lisibles ici pour l'instant. Ce n'est pas la même "
        + "chose qu'un cabinet sans abonnement ou sans paiement. Les compteurs d'activité ci-dessous sont réels.";
}
