using System.Security.Cryptography;
using System.Text;

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

    private static List<LetterValueSeed> BuildLetterValues()
    {
        var raw = new (string Cle, decimal Value)[]
        {
            (CD, 7m),
            (CDS, 10m),
            (VD, 10m),
            (D, 1.200m),
            (RD, 2m),
        };

        return raw
            .Select(r => new LetterValueSeed(DeterministicGuid($"cnam-vlc:{r.Cle}"), r.Cle, r.Value))
            .ToList();
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
