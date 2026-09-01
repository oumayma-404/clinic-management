using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// Picks acts out of a clinic's own catalogue. An act matches when every set field agrees with it, so
/// <c>Produces</c> alone selects « whatever this clinic calls the act that leaves a tooth obturée », and
/// <c>Category</c> alone selects a discipline for acts that leave no odontogram state at all (détartrage,
/// surfaçage, prothèse amovible).
/// </summary>
/// <param name="Produces">The <see cref="ToothCondition"/> the act leaves behind, or null to ignore.</param>
/// <param name="Category">The canonical discipline, or null to ignore.</param>
public sealed record TreatmentSelector(ToothCondition? Produces = null, string? Category = null);

/// <summary>
/// <b>What treats what.</b> The one place the product states that a carie is answered by an obturation before a
/// dévitalisation, and a racine résiduelle by an extraction.
///
/// <para>⚠️ This is the <b>inverse</b> of <c>ProcedureType.ResultingCondition</c> and could not be derived from
/// it. That field answers « what state does this act leave the tooth in? », which is 1:1 and only ever names an
/// *end* state — so inverting it works for a diagnosis that is itself an end state (« Couronne » → the crown act)
/// and cannot work for a pathology, because no act ends in « Carie ». Reading the odontogram's plan seeds off
/// that inversion is exactly why charting a carie produced a blank, costless plan line.</para>
///
/// <para><b>Order is clinical and load-bearing: least invasive first.</b> Managing a carious lesion runs
/// restoration → vital pulp therapy / endodontics → crown → extraction, and the first entry is the one the plan
/// pre-fills, so the order decides what a devis quotes by default.</para>
///
/// <para><b>Nothing here names an act.</b> Selectors resolve against the clinic's live catalogue, so a practice
/// that does no endodontics simply never sees it offered, and renaming an act cannot break the mapping.</para>
///
/// <para>⚠️ A first selector matching <b>several</b> acts must not be resolved by picking one — see
/// <c>Rank</c>'s note on <c>ProcedureTypeDto</c>. « Racine résiduelle » matches both extractions, and choosing
/// between a simple and a surgical extraction is a judgement about access, not something a table knows.</para>
/// </summary>
public static class ConditionTreatments
{
    private static readonly IReadOnlyList<TreatmentSelector> None = Array.Empty<TreatmentSelector>();

    /// <summary>Restore, then endo, then crown, then take it out. Shared by the three conditions that damage a
    /// tooth the same way and are answered by the same ladder.</summary>
    private static readonly TreatmentSelector[] RestoreThenEscalate =
    [
        new(Produces: ToothCondition.Obturation),
        new(Produces: ToothCondition.TraitementDeCanal),
        new(Produces: ToothCondition.Couronne),
        new(Produces: ToothCondition.ExtraitAbsent),
    ];

    private static readonly Dictionary<ToothCondition, IReadOnlyList<TreatmentSelector>> Map = new()
    {
        [ToothCondition.Carie] = RestoreThenEscalate,
        [ToothCondition.Fracture] = RestoreThenEscalate,
        [ToothCondition.RestaurationDefectueuse] = RestoreThenEscalate,

        [ToothCondition.LesionPeriapicale] =
        [
            new(Produces: ToothCondition.TraitementDeCanal),
            new(Produces: ToothCondition.ExtraitAbsent),
        ],

        [ToothCondition.RacineResiduelle] = [new(Produces: ToothCondition.ExtraitAbsent)],
        [ToothCondition.DentIncluse] = [new(Produces: ToothCondition.ExtraitAbsent)],

        // No act in the catalogue leaves a periodontal state behind, so the discipline is the only handle.
        [ToothCondition.MaladieParodontale] = [new(Category: "Parodontologie")],

        /*
         * A tooth already charted as a *restoration* is a plan target: « je veux une couronne sur la 26 ». The act
         * that produces that state is its treatment, which is what the old ResultingCondition inversion got right.
         */
        [ToothCondition.Obturation] = [new(Produces: ToothCondition.Obturation)],
        [ToothCondition.TraitementDeCanal] = [new(Produces: ToothCondition.TraitementDeCanal)],
        [ToothCondition.Couronne] = [new(Produces: ToothCondition.Couronne)],
        [ToothCondition.Implant] = [new(Produces: ToothCondition.Implant)],

        // ⚠️ No act leaves `Bridge` behind — the seeded catalogue files « Couronne / bridge (par élément) » under
        // Couronne — so inverting ResultingCondition left a Bridge diagnosis with no act and no cost at all.
        [ToothCondition.Bridge] =
        [
            new(Category: "Prothèse fixe"),
            new(Produces: ToothCondition.Couronne),
        ],

        /*
         * ⚠️ A missing tooth is REPLACED, never extracted again. Inverting ResultingCondition answered
         * « Extrait / Absent » with an extraction act, i.e. it proposed pulling a tooth that is already gone.
         */
        [ToothCondition.ExtraitAbsent] =
        [
            new(Produces: ToothCondition.Implant),
            new(Category: "Prothèse fixe"),
            new(Category: "Prothèse amovible"),
        ],

        // « À traiter » names no problem, so it earns no suggestion — the picker opens on the whole catalogue.
        // Offering a default here would be the app inventing a diagnosis the dentist declined to give.
        [ToothCondition.ATraiter] = None,
        [ToothCondition.Sain] = None,
    };

    /// <summary>
    /// The conditions that describe <b>work still to do</b>. Only these seed a plan from the odontogram and only
    /// these are counted in « N dents à traiter ».
    ///
    /// <para>⚠️ <see cref="ToothCondition.DentIncluse"/> and <see cref="ToothCondition.ExtraitAbsent"/> are
    /// absent by design although both carry treatments: an impacted tooth is usually monitored, and a missing one
    /// is only replaced if the patient wants it. Counting either as outstanding work inflates the one figure a
    /// dentist has to be able to trust.</para>
    /// </summary>
    public static readonly IReadOnlySet<ToothCondition> NeedsTreatment = new HashSet<ToothCondition>
    {
        ToothCondition.Carie,
        ToothCondition.ATraiter,
        ToothCondition.Fracture,
        ToothCondition.RacineResiduelle,
        ToothCondition.RestaurationDefectueuse,
        ToothCondition.LesionPeriapicale,
        ToothCondition.MaladieParodontale,
    };

    /// <summary>The ordered selectors that treat a condition; empty when the product has nothing to suggest.</summary>
    public static IReadOnlyList<TreatmentSelector> For(ToothCondition condition) =>
        Map.TryGetValue(condition, out var selectors) ? selectors : None;

    /// <summary>
    /// Every condition this act treats, with the rank it holds among that condition's selectors (0 = first
    /// choice). Computed per act so the catalogue itself carries the answer to the client and no second copy of
    /// this table has to exist there.
    /// </summary>
    public static IReadOnlyList<(ToothCondition Condition, int Rank)> RanksFor(
        ToothCondition? resultingCondition,
        string? category)
    {
        var ranks = new List<(ToothCondition, int)>();
        foreach (var (condition, selectors) in Map)
        {
            for (var rank = 0; rank < selectors.Count; rank++)
            {
                if (!Matches(selectors[rank], resultingCondition, category)) continue;
                ranks.Add((condition, rank));
                // The FIRST selector an act satisfies is its rank: an extraction act matches « Carie »'s last
                // rung, and must not also be recorded at a later one.
                break;
            }
        }
        return ranks;
    }

    private static bool Matches(TreatmentSelector selector, ToothCondition? resultingCondition, string? category)
    {
        if (selector.Produces is not null && resultingCondition != selector.Produces) return false;
        if (selector.Category is not null && !string.Equals(category, selector.Category, StringComparison.Ordinal))
        {
            return false;
        }
        return selector.Produces is not null || selector.Category is not null;
    }
}
