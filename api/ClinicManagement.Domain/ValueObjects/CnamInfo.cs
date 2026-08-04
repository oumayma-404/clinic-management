using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.ValueObjects;

// Optional CNAM (Caisse Nationale d'Assurance Maladie — Tunisia) identity for a patient, used to
// pre-fill the Bulletin de soins (BS1). Every field is optional (spec AC-1): a patient may carry any
// subset, and existing patients simply have none. Stored as an owned value object on Patient.
public class CnamInfo : ValueObject
{
    // ── The two closed sets the BS1 form's checkboxes are keyed on ──────────────────────────────────────
    //
    // These seven literals are what `CnamBs1BulletinRenderer` ticks on the printed form and what the patient
    // dialog offers as `<SelectItem value>`. They live HERE, on the value object that owns the fields, because
    // they used to be two independent copies of a French string in two projects. A mismatch in casing or in an
    // accent — « Convention bilatérale » carries one — made the renderer's `switch` fall through, so the régime
    // box printed EMPTY and nothing anywhere raised: a bulletin that looks filled on screen and is refused at
    // the caisse. One const per value cannot drift, and `IsKnownRegime`/`IsKnownLien` below turn a value from
    // outside the set into a refusal at the write instead of a silent no-op at the draw.
    public const string RegimeCnss = "CNSS";
    public const string RegimeCnrps = "CNRPS";
    public const string RegimeConventionBilaterale = "Convention bilatérale";

    public static readonly IReadOnlyList<string> AllowedRegimes =
        new[] { RegimeCnss, RegimeCnrps, RegimeConventionBilaterale };

    public const string LienAssureLuiMeme = "Assuré lui-même";
    public const string LienConjoint = "Conjoint";
    public const string LienEnfant = "Enfant";
    public const string LienAscendant = "Ascendant";

    public static readonly IReadOnlyList<string> AllowedLiens =
        new[] { LienAssureLuiMeme, LienConjoint, LienEnfant, LienAscendant };

    // « Enfant » is identified by its rang and « Ascendant » by père/mère; the other two liens name exactly one
    // person, so demanding a rang for them would be asking for a value that does not exist.
    public static readonly IReadOnlyList<string> LiensRequiringRang =
        new[] { LienEnfant, LienAscendant };

    /// <summary>
    /// How many digit cells the printed BS1 gives the identifiant unique. The renderer combs the number one
    /// digit per fixed cell, so a longer value has nowhere to put its tail — it used to be dropped silently
    /// (no log, no failure), printing a CNAM identifier cut off mid-way.
    /// </summary>
    public const int IdentifiantUniqueDigits = 10;

    public string? IdentifiantUnique { get; private set; }
    public string? Regime { get; private set; } // one of AllowedRegimes
    public string? AssureFirstName { get; private set; }
    public string? AssureLastName { get; private set; }
    public string? AssureAddress { get; private set; }
    public string? AssurePostalCode { get; private set; }
    public string? MaladeLien { get; private set; } // one of AllowedLiens
    public string? MaladeLienRang { get; private set; } // enfant rang, or père/mère for ascendant

    /// <summary>
    /// How many dependants (« ayants droit ») the insured person declares — the input to the annual-ceiling barème
    /// (<c>CnamPlafond.BaseCeiling</c>). Null means « not recorded », which is read as none.
    /// <para>
    /// ⚠️ It is <b>not</b> derivable from <see cref="MaladeLien"/>: that says how <i>this</i> patient relates to the
    /// insured person, while the ceiling depends on the household's size — and the other dependants may not be
    /// patients of this clinic at all.
    /// </para>
    /// </summary>
    public int? DependantCount { get; private set; }

    /// <summary>
    /// The household's real annual ceiling, when somebody knows it — and the reason the barème in
    /// <c>CnamPlafond</c> can ship as a default despite its figures being sourced rather than officially confirmed.
    /// It always wins over the computed one. Null (or a non-positive value) falls back to the barème.
    /// <para>
    /// This is also where the three supplements the sources report land — dependent parent, dependent disabled
    /// child, pregnancy — since each turns on a fact this product does not record. <c>CnamPlafond</c> names their
    /// amounts so the screen can quote them beside this field.
    /// </para>
    /// </summary>
    public decimal? AnnualCeilingOverride { get; private set; }

    private CnamInfo() { } // For EF Core

    public CnamInfo(
        string? identifiantUnique,
        string? regime,
        string? assureFirstName,
        string? assureLastName,
        string? assureAddress,
        string? assurePostalCode,
        string? maladeLien,
        string? maladeLienRang,
        int? dependantCount = null,
        decimal? annualCeilingOverride = null)
    {
        IdentifiantUnique = identifiantUnique;
        Regime = regime;
        AssureFirstName = assureFirstName;
        AssureLastName = assureLastName;
        AssureAddress = assureAddress;
        AssurePostalCode = assurePostalCode;
        MaladeLien = maladeLien;
        MaladeLienRang = maladeLienRang;
        // Clamped rather than refused: a ceiling of 0 reads on screen as « CNAM refuses this patient », and a
        // negative dependant count has no meaning. Both are what a blank numeric input arrives as.
        DependantCount = dependantCount is { } count && count > 0 ? count : null;
        AnnualCeilingOverride = annualCeilingOverride is { } ceiling && ceiling > 0m ? ceiling : null;
    }

    // True when no CNAM field carries a value — the handler treats this as "no CNAM identity" and clears it.
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(IdentifiantUnique) &&
        string.IsNullOrWhiteSpace(Regime) &&
        string.IsNullOrWhiteSpace(AssureFirstName) &&
        string.IsNullOrWhiteSpace(AssureLastName) &&
        string.IsNullOrWhiteSpace(AssureAddress) &&
        string.IsNullOrWhiteSpace(AssurePostalCode) &&
        string.IsNullOrWhiteSpace(MaladeLien) &&
        string.IsNullOrWhiteSpace(MaladeLienRang) &&
        // The two ceiling fields count: a patient for whom only a dependant count is known still has a CNAM
        // identity worth keeping, and treating it as empty would silently discard it on the next save.
        DependantCount is null &&
        AnnualCeilingOverride is null;

    /// <summary>
    /// The digits of an identifiant unique, with the spaces / dashes a free-text field collects removed —
    /// exactly what the renderer combs into the form's cells, so validation and drawing count the same thing.
    /// </summary>
    public static int CountIdentifiantDigits(string? value)
        => string.IsNullOrEmpty(value) ? 0 : value.Count(char.IsDigit);

    /// <summary>
    /// True when <paramref name="value"/> carries between one and <see cref="IdentifiantUniqueDigits"/> digits —
    /// i.e. it fits the printed comb. A blank value is <b>not</b> valid here: the field itself stays optional on
    /// the patient record (checked by the caller), but a supplied one that cannot be printed in full is worse
    /// than none, since the paper shows a plausible truncated number nobody re-reads.
    /// </summary>
    public static bool IsValidIdentifiantUnique(string? value)
    {
        var digits = CountIdentifiantDigits(value);
        return digits > 0 && digits <= IdentifiantUniqueDigits;
    }

    /// <summary>True when <paramref name="value"/> is one of <see cref="AllowedRegimes"/>, exactly as printed.</summary>
    public static bool IsKnownRegime(string? value)
        => value != null && AllowedRegimes.Contains(value, StringComparer.Ordinal);

    /// <summary>True when <paramref name="value"/> is one of <see cref="AllowedLiens"/>, exactly as printed.</summary>
    public static bool IsKnownLien(string? value)
        => value != null && AllowedLiens.Contains(value, StringComparer.Ordinal);

    /// <summary>True when the lien is one whose BS1 cell also carries a rang (« Enfant » / « Ascendant »).</summary>
    public static bool LienRequiresRang(string? value)
        => value != null && LiensRequiringRang.Contains(value, StringComparer.Ordinal);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return IdentifiantUnique ?? string.Empty;
        yield return Regime ?? string.Empty;
        yield return AssureFirstName ?? string.Empty;
        yield return AssureLastName ?? string.Empty;
        yield return AssureAddress ?? string.Empty;
        yield return AssurePostalCode ?? string.Empty;
        yield return MaladeLien ?? string.Empty;
        yield return MaladeLienRang ?? string.Empty;
        yield return DependantCount ?? -1;
        yield return AnnualCeilingOverride ?? -1m;
    }
}
