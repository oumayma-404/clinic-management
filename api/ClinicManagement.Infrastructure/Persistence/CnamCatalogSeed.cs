using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Canonical provisional seed for the global CNAM catalog + VLC values (FR-5.1 / FR-5.2). Single source
/// of truth shared by the <c>AddCnamCatalog</c> migration (which inserts these rows) and the seed-integrity
/// unit tests — so the two can never drift. These are the former in-code <c>CnamNomenclatureProvider</c>
/// defaults; every row seeds with <c>IsProvisional = true</c> ("à vérifier") until an admin confirms them
/// against the current CNAM dentist convention.
/// </summary>
public static class CnamCatalogSeed
{
    // Fixed seed timestamp (migrations are deterministic; no DateTime.Now in a seed).
    public static readonly DateTime SeededAtUtc = new(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);

    // Category labels (French) — the five buckets the UI filters by.
    public const string Consultation = "Consultation";
    public const string SoinsConservateurs = "Soins conservateurs";
    public const string ChirurgieExtraction = "Chirurgie/Extraction";
    public const string Prothese = "Prothèse";
    public const string Radiologie = "Radiologie";

    // Lettres clés used by the dental nomenclature (drive the indicative reimbursement estimate).
    public const string CD = "CD";   // Consultation dentaire
    public const string CDS = "CDS"; // Consultation dentaire de spécialiste
    public const string VD = "VD";   // Visite dentaire
    public const string D = "D";     // Actes de soins dentaires
    public const string RD = "RD";   // Radiologie dentaire

    public sealed record EntrySeed(Guid Id, string CodeActe, string DesignationFr, string LettreCle, decimal Coefficient, string Category);
    public sealed record LetterValueSeed(Guid Id, string LettreCle, decimal Value);

    /// <summary>
    /// What this seed shipped for each lettre clé <b>before</b> the convention values were wired in — kept, not
    /// deleted, because it is the only way a correction can tell « nobody has ever touched this row » from
    /// « an admin deliberately entered this ». See <see cref="SupersededLetterValue"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These figures were stale on the day they shipped: the convention in force since 01/01/2021 fixes Cd at
    /// 30,000 DT and D at 3,000 DT, and its own text records the *previous* values as Cd 18,000 / D 1,700 — so
    /// <c>7</c> and <c>1.200</c> predate even those. The indicative estimate is <c>coefficient × VLC × taux</c>,
    /// which understated every reimbursement figure shown to a patient by roughly 60–75 %.
    /// </para>
    /// <para>
    /// ⚠️ <b>Declared above <see cref="LetterValues"/> deliberately.</b> Static field initializers run in
    /// <i>textual</i> order, so with this below the property, <c>BuildLetterValues()</c> ran against a null array
    /// and the type initializer threw — which meant every read of the seed (and therefore application startup)
    /// failed. Do not move it down for tidiness.
    /// </para>
    /// </remarks>
    private static readonly (string Cle, decimal Value)[] LegacyLetterValues =
    {
        (CD, 7m),
        (CDS, 10m),
        (VD, 10m),
        (D, 1.200m),
        (RD, 2m),
    };

    public static IReadOnlyList<EntrySeed> Entries { get; } = BuildEntries();
    public static IReadOnlyList<LetterValueSeed> LetterValues { get; } = BuildLetterValues();

    private static List<EntrySeed> BuildEntries()
    {
        var raw = new (string Code, string Designation, string Cle, decimal Coef, string Cat)[]
        {
            // Consultation
            ("CONS", "Consultation dentaire", CD, 1, Consultation),
            ("CONS-SPE", "Consultation de spécialiste (ODF, parodontologie, chirurgie)", CDS, 1, Consultation),
            ("VIS-DOM", "Visite à domicile", VD, 1, Consultation),

            // Soins conservateurs
            ("DETART", "Détartrage (deux arcades)", D, 10, SoinsConservateurs),
            ("OBT-1F", "Obturation d'une face (amalgame ou composite)", D, 8, SoinsConservateurs),
            ("OBT-2F", "Obturation de deux faces", D, 12, SoinsConservateurs),
            ("OBT-3F", "Obturation de trois faces ou plus", D, 15, SoinsConservateurs),
            ("PULP", "Coiffage pulpaire / traitement d'une dent temporaire", D, 8, SoinsConservateurs),
            ("ENDO-MONO", "Traitement endodontique — dent monoradiculée", D, 20, SoinsConservateurs),
            ("ENDO-PLURI", "Traitement endodontique — dent pluriradiculée", D, 30, SoinsConservateurs),
            ("SCELLT", "Scellement de sillons", D, 5, SoinsConservateurs),

            // Chirurgie / Extraction
            ("EXT-SIMPLE", "Extraction d'une dent permanente", D, 10, ChirurgieExtraction),
            ("EXT-TEMP", "Extraction d'une dent temporaire", D, 6, ChirurgieExtraction),
            ("EXT-COMPLEXE", "Extraction complexe / dent incluse", D, 25, ChirurgieExtraction),
            ("ALVEOL", "Alvéolectomie / régularisation de crête", D, 20, ChirurgieExtraction),
            ("KYST", "Énucléation d'un kyste", D, 30, ChirurgieExtraction),
            ("FREIN", "Frénectomie", D, 15, ChirurgieExtraction),

            // Prothèse
            ("PROTH-1", "Prothèse adjointe partielle — 1 à 3 dents", D, 30, Prothese),
            ("PROTH-COMPL", "Prothèse adjointe complète (une arcade)", D, 60, Prothese),
            ("COURONNE", "Couronne coulée / céramo-métallique", D, 40, Prothese),
            ("BRIDGE-EL", "Bridge — élément intermédiaire", D, 40, Prothese),
            ("INLAY", "Inlay-core / reconstitution corono-radiculaire", D, 25, Prothese),
            ("REPAR-PROTH", "Réparation de prothèse", D, 10, Prothese),

            // Radiologie
            ("RETRO", "Radiographie rétro-alvéolaire", RD, 1, Radiologie),
            ("PANO", "Radiographie panoramique (orthopantomogramme)", RD, 5, Radiologie),
            ("TELERX", "Téléradiographie", RD, 5, Radiologie),
        };

        return raw
            .Select(r => new EntrySeed(DeterministicGuid($"cnam-entry:{r.Code}"), r.Code, r.Designation, r.Cle, r.Coef, r.Cat))
            .ToList();
    }

    // The convention's value where it settles one, the older unverified figure otherwise (Vd/Rd — which stay
    // IsProvisional « à vérifier »). Derived rather than retyped: a second hand-written copy of 30,000 / 45,000 /
    // 3,000 here is exactly the drift CnamConventionTariffs exists to prevent.
    private static List<LetterValueSeed> BuildLetterValues()
        => LegacyLetterValues
            .Select(r => new LetterValueSeed(
                DeterministicGuid($"cnam-vlc:{r.Cle}"),
                r.Cle,
                CnamConventionTariffs.ValueFor(r.Cle) ?? r.Value))
            .ToList();

    /// <summary>
    /// The value this seed shipped for <paramref name="lettreCle"/> before the convention correction, or
    /// <c>null</c> when there is nothing to correct — either the convention settles no value for that lettre clé
    /// (<c>Vd</c>/<c>Rd</c>), or the seeded figure already agreed with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the third term of the startup correction's predicate (DEV-4). A correction that fires on
    /// <c>IsProvisional</c> alone would be wrong: <c>CnamLetterValue.SetValue</c> stamps <c>UpdatedAt</c> and
    /// <b>does not</b> clear the provisional flag — only <c>Confirm()</c> does — so an admin who typed their own
    /// valeur de la lettre clé and never pressed « Confirmer » still reads <c>IsProvisional = true</c>. Overwriting
    /// that is worse than leaving a stale default: it replaces a deliberate figure with one nobody asked for.
    /// </para>
    /// <para>
    /// So the correction only ever touches a row that is <b>untouched since seeding</b> (<c>UpdatedAt == null</c>),
    /// still unvouched-for, <b>and</b> still holding the exact number returned here. Everything else is left alone
    /// and surfaced to the admin as a prompt instead.
    /// </para>
    /// </remarks>
    public static decimal? SupersededLetterValue(string? lettreCle)
    {
        if (string.IsNullOrWhiteSpace(lettreCle))
        {
            return null;
        }

        foreach (var (cle, legacy) in LegacyLetterValues)
        {
            if (!string.Equals(cle, lettreCle.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var inForce = CnamConventionTariffs.ValueFor(cle);
            return inForce.HasValue && inForce.Value != legacy ? legacy : null;
        }

        return null;
    }

    /// <summary>
    /// Stable GUID derived from a key string (MD5) so the seed ids are identical on every machine and
    /// across re-generations — no <c>Guid.NewGuid()</c> churn in a committed migration.
    /// </summary>
    public static Guid DeterministicGuid(string key)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
