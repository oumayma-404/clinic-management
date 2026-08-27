namespace ClinicManagement.Domain.Services;

/// <summary>
/// The <b>valeurs de la lettre clé</b> fixed by the CNAM dentist convention currently in force — the single
/// authority on what each lettre clé is worth in dinars.
/// </summary>
/// <remarks>
/// <para>
/// Source: <b>Convention sectorielle des médecins dentistes de libre pratique</b> (CNAM + STMDLP, décembre 2020),
/// approuvée par l'<b>arrêté du ministre des affaires sociales du 3 février 2021</b>, JORT 2021-014. The values
/// took effect <b>01/01/2021</b>.
/// </para>
/// <para>
/// ⚠️ <b>Why this is a shared table and not five numbers inside the seed.</b> The seed had shipped
/// <c>Cd 7</c> / <c>Cds 10</c> / <c>D 1,200</c> — values older even than the convention's own "previous"
/// figures (Cd 18,000, D 1,700). Since the indicative estimate is <c>coefficient × VLC × taux</c>, every
/// reimbursement figure the software showed a patient was understated by roughly 60–75 %. The fix is not only
/// to correct the numbers: a clinic seeded before the correction still holds the old ones in its own rows, so
/// the admin screen has to be able to say « la convention en vigueur fixe cette valeur à 30,000 DT » and offer
/// the correction. Two places therefore need the same table — the seed (Infrastructure) and the letter-values
/// read (Application) — which is why it lives in Domain, the one project both reference. A second copy in the
/// browser is what produced the duplicated CNAM calculator this repo already had to delete.
/// </para>
/// <para>
/// ⚠️ <see cref="ValueFor"/> returns <c>null</c> for a lettre clé the convention text did not settle
/// (<c>Vd</c>, <c>Rd</c>): those keep their seeded value and their « à vérifier » flag. A null is « we do not
/// know », which the UI must render as such — inventing a figure here would be the same class of defect as the
/// stale one, only harder to notice.
/// </para>
/// </remarks>
public static class CnamConventionTariffs
{
    /// <summary>The date the values below took effect (used to date the admin screen's prompt).</summary>
    public static readonly DateOnly InForceSince = new(2021, 1, 1);

    /// <summary>
    /// The convention revises the lettres clés against SMIG/CPI every three years. Surfaced on the admin screen
    /// so the next staleness is <i>expected</i> — the shipped defect was not that a number moved, it was that
    /// nothing on any screen suggested one ever would.
    /// </summary>
    public const int RevisionIntervalYears = 3;

    /// <summary>Human-readable provenance, shown beside the prompt so an admin can check the primary text.</summary>
    public const string Source =
        "Convention sectorielle des médecins dentistes de libre pratique (CNAM/STMDLP), "
        + "arrêté du 3 février 2021 — JORT 2021-014";

    // Keyed on the lettre clé as stored (see CnamCatalogSeed's CD/CDS/D/VD/RD constants). Ordinal-ignore-case
    // because the convention writes them « Cd »/« Cds »/« D » and the catalogue stores them uppercase.
    private static readonly Dictionary<string, decimal> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CD"] = 30.000m,  // consultation du médecin dentiste
        ["CDS"] = 45.000m, // consultation du spécialiste / de l'orthodontiste
        ["D"] = 3.000m,    // acte de soins dentaires
    };

    /// <summary>
    /// The dinar value the convention in force fixes for <paramref name="lettreCle"/>, or <c>null</c> when the
    /// convention text did not settle it — see the remark on unverified keys.
    /// </summary>
    public static decimal? ValueFor(string? lettreCle)
        => lettreCle != null && Values.TryGetValue(lettreCle, out var value) ? value : null;
}
