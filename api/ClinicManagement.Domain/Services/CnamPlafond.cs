namespace ClinicManagement.Domain.Services;

/// <summary>
/// The CNAM <b>annual ceiling</b> (« plafond annuel ») — the one place this product states what a patient's
/// reimbursement is capped at, and which acts consume it (L10).
///
/// <para><b>Why it exists.</b> <c>CnamReimbursementCalculator</c> computes <c>coefficient × VLC × rate</c> with no
/// cap and no knowledge of what the patient has already consumed this year, so « Remboursement indicatif »
/// over-promised for anyone near their ceiling — and the disclaimer beside it named only the age band. A figure that
/// is confidently wrong is worse than one that is absent.</para>
///
/// <para>⚠️ <b>The amounts below are sourced but not officially confirmed</b>, and that is recorded here rather
/// than in a comment somewhere downstream. They are the figures effective <b>1 February 2024</b> as reported by two
/// Tunisian outlets in agreement; no official CNAM publication was retrieved. They are therefore a
/// <b>default</b> — <c>AnnualCeilingOverride</c> on the patient's <c>CnamInfo</c> is what an admin who knows the
/// household's real ceiling sets, and it always wins. Do not treat <see cref="BaseCeiling"/> as authoritative
/// without re-checking it against the caisse's own published barème.</para>
///
/// <para>⚠️ <b>Everything computed from this is an estimate for a second, independent reason</b>: the clinic can
/// only see the acts <i>it</i> performed. A patient treated at another practice has consumed ceiling this software
/// has no way to know about, so the remaining figure is an upper bound. Every surface that shows it must say so —
/// see <c>CnamCeilingDto</c>.</para>
/// </summary>
public static class CnamPlafond
{
    /// <summary>The ceiling for an insured person with no dependants, in TND.</summary>
    public const decimal CeilingAlone = 450m;

    /// <summary>
    /// The ceiling by number of dependants, index 0 = alone. Beyond the last entry the ceiling does not keep
    /// growing — the published barème stops at « 4 et plus », so a household of six shares the same figure as one
    /// of four.
    /// </summary>
    private static readonly decimal[] CeilingByDependants = { CeilingAlone, 675m, 900m, 1_125m, 1_350m };

    /// <summary>
    /// The portion of the ceiling dedicated to <b>soins dentaires externes</b>, on top of the household figure.
    /// <para>
    /// ⚠️ This is the figure that actually binds a dental practice, and it is the least certain of the set: the
    /// sources describe it as a dedicated allowance for external dental care without settling whether it sits
    /// inside the household ceiling or above it. It is modelled as <b>additive</b> and surfaced as its own line, so
    /// a reader can see both numbers rather than one blended figure whose derivation nobody can check.
    /// </para>
    /// </summary>
    public const decimal DentalAllowance = 150m;

    /// <summary>
    /// Supplements the sources report, declared for the UI to <b>quote</b> beside the override field rather than
    /// applied automatically.
    /// <para>
    /// Deliberate: each of the three turns on a fact this product does not record (a dependent parent, a dependent
    /// disabled child, a pregnancy), and three more nullable columns to hold facts nobody would maintain is how a
    /// setting ships with no caller — the <c>SetStockExpiryLeadDays</c> failure this repo keeps documenting. Naming
    /// the amounts lets an admin compute the household's real ceiling once and type it into the override, which is
    /// the one number the calculation then trusts.
    /// </para>
    /// </summary>
    public const decimal DependentParentSupplement = 100m;

    /// <inheritdoc cref="DependentParentSupplement"/>
    public const decimal DisabledChildSupplement = 100m;

    /// <inheritdoc cref="DependentParentSupplement"/>
    public const decimal PregnancySupplement = 150m;

    /// <summary>
    /// The act categories that do <b>not</b> consume the ceiling. Matched against
    /// <c>DentalActCode.Category</c> — the value <c>DentalActCatalogSeed</c> writes — case- and accent-tolerantly
    /// by <see cref="ConsumesCeiling"/>, because the category is open text a clinic may retype.
    /// <para>
    /// « Prothèse » has been hors plafond since April 2019. **Cone Beam is also hors plafond** and is deliberately
    /// absent from this list: it is imaging, so it has no row in the dental-act catalogue and there is nothing here
    /// to match it against — an entry pretending otherwise would suggest a check that never runs.
    /// </para>
    /// </summary>
    private static readonly string[] HorsPlafondCategories = { "prothese" };

    /// <summary>
    /// The household ceiling for <paramref name="dependants"/>, before <see cref="DentalAllowance"/>. A negative
    /// count is read as none, and anything past the barème's last band gets that band's figure.
    /// </summary>
    public static decimal BaseCeiling(int dependants)
    {
        if (dependants < 0) dependants = 0;
        return dependants >= CeilingByDependants.Length
            ? CeilingByDependants[^1]
            : CeilingByDependants[dependants];
    }

    /// <summary>
    /// The ceiling to measure consumption against: the override when an admin supplied one, otherwise
    /// <see cref="BaseCeiling"/> + <see cref="DentalAllowance"/>.
    /// <para>
    /// ⚠️ An override of <b>0 or less is ignored</b>, not honoured. A zero ceiling would report every patient as
    /// fully consumed, which reads as « CNAM refuses this patient » — and a blank numeric field arriving as 0 is
    /// the ordinary way that value appears.
    /// </para>
    /// </summary>
    public static decimal EffectiveCeiling(int? dependants, decimal? annualCeilingOverride)
    {
        if (annualCeilingOverride is { } custom && custom > 0m)
        {
            return custom;
        }

        return BaseCeiling(dependants ?? 0) + DentalAllowance;
    }

    /// <summary>
    /// True when an act of this category consumes the ceiling. An <b>unknown or blank</b> category consumes it —
    /// the safe direction is to count the act, because failing to count one silently inflates the remaining figure,
    /// which is the exact over-promise L10 exists to remove.
    /// </summary>
    public static bool ConsumesCeiling(string? actCategory)
    {
        if (string.IsNullOrWhiteSpace(actCategory)) return true;

        var folded = Fold(actCategory);
        return !HorsPlafondCategories.Contains(folded);
    }

    /// <summary>
    /// Lower-cases and strips accents, punctuation and spaces, so « Prothèse », « prothese » and « PROTHÈSES » all
    /// match. The same tolerance <c>ProcedureTypeCategories.Normalize</c> applies, and for the same reason: the
    /// category is open text, so a clinic that retyped it must not silently start consuming ceiling.
    /// </summary>
    private static string Fold(string value)
    {
        var chars = value.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                            != System.Globalization.UnicodeCategory.NonSpacingMark
                        && char.IsLetterOrDigit(c))
            .Select(char.ToLowerInvariant)
            .ToArray();
        // A plural « prothèses » folds onto « prothese » so both spellings are recognised as one category.
        var folded = new string(chars);
        return folded.EndsWith('s') && folded.Length > 1 ? folded[..^1] : folded;
    }
}
