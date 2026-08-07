namespace ClinicManagement.Application.DTOs;

// A valeur de la lettre clé (VLC) — the dinar value per lettre clé used in the indicative reimbursement
// estimate (FR-5.2). Per-clinic reference data; readable by any authenticated user, editable by admins only.
public class CnamLetterValueDto
{
    public Guid Id { get; set; }
    public string LettreCle { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public bool IsProvisional { get; set; }

    // ── What the convention in force says, so the admin screen can offer the correction ────────────────
    //
    // The startup pass corrects only rows untouched since seeding (ClinicCatalogSeeder.CorrectSupersededDefaults
    // — DEV-4). A clinic whose admin has already edited a value is deliberately left alone, which means the
    // divergence has to be *visible* somewhere or it is simply lost: these three fields are what let
    // `/cnam-nomenclature` say « la convention en vigueur fixe cette valeur à 30,000 DT » beside the stored one,
    // with an « Appliquer » action, rather than silently overwriting a deliberate entry.
    //
    // ⚠️ All three are **null** for a lettre clé the convention text did not settle (Vd/Rd). A null is « we do not
    // know », and the UI must render it as such — inventing a figure here would be the same class of defect as the
    // stale one, only harder to notice.

    /// <summary>The dinar value the convention currently in force fixes for this lettre clé, if it fixes one.</summary>
    public decimal? ConventionValue { get; set; }

    /// <summary>Human-readable provenance (the arrêté + JORT reference), so an admin can check the primary text.</summary>
    public string? ConventionSource { get; set; }

    /// <summary>
    /// How often the convention revises the lettres clés against SMIG/CPI. Surfaced so the next staleness is
    /// <i>expected</i> — the shipped defect was not that a number moved, it was that nothing on any screen
    /// suggested one ever would.
    /// </summary>
    public int? ConventionRevisionIntervalYears { get; set; }
}
