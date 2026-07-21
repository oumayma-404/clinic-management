using System.Security.Cryptography;
using System.Text;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Canonical provisional seed for the global medication catalog. Single source of truth shared by the
/// <c>AddMedicationCatalog</c> migration (which inserts these rows) and the seed-integrity unit tests — so
/// the two can never drift. Every medication seeds with <c>IsProvisional = true</c> ("à vérifier") until an
/// admin confirms it. This is a deliberately small STARTER set of common drugs a Tunisian dental/medical
/// clinic prescribes (analgesics, antibiotics incl. combination products, corticoïdes, bains de bouche,
/// gastro) — NOT the full national formulary; admins extend it in the UI. Ids are deterministic (stable
/// across machines / re-generations — no <c>Guid.NewGuid()</c> churn in a committed migration).
/// </summary>
public static class MedicationCatalogSeed
{
    // Fixed seed timestamp (migrations are deterministic; no DateTime.Now in a seed).
    public static readonly DateTime SeededAtUtc = new(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);

    public sealed record MedicationSeed(Guid Id, string BrandName, string Form, string Strength, IReadOnlyList<string> Dcis);
    public sealed record IngredientSeed(Guid Id, Guid MedicationId, string Dci);

    public static IReadOnlyList<MedicationSeed> Medications { get; } = BuildMedications();
    public static IReadOnlyList<IngredientSeed> Ingredients { get; } = BuildIngredients();

    private static List<MedicationSeed> BuildMedications()
    {
        var raw = new (string Key, string Brand, string Form, string Strength, string[] Dcis)[]
        {
            // Antalgiques / antipyrétiques
            ("doliprane-1000", "Doliprane", "Comprimé", "1000 mg", new[] { "Paracétamol" }),
            ("doliprane-500", "Doliprane", "Comprimé", "500 mg", new[] { "Paracétamol" }),
            ("efferalgan-1g", "Efferalgan", "Comprimé effervescent", "1 g", new[] { "Paracétamol" }),

            // Anti-inflammatoires non stéroïdiens (AINS)
            ("brufen-400", "Brufen", "Comprimé", "400 mg", new[] { "Ibuprofène" }),
            ("voltarene-50", "Voltarène", "Comprimé", "50 mg", new[] { "Diclofénac" }),
            ("profenid-100", "Profénid", "Comprimé", "100 mg", new[] { "Kétoprofène" }),

            // Antibiotiques
            ("clamoxyl-1g", "Clamoxyl", "Comprimé", "1 g", new[] { "Amoxicilline" }),
            ("augmentin-1g", "Augmentin", "Comprimé", "1 g", new[] { "Amoxicilline", "Acide clavulanique" }),
            ("augmentin-500", "Augmentin", "Sachet", "500 mg/62,5 mg", new[] { "Amoxicilline", "Acide clavulanique" }),
            ("flagyl-500", "Flagyl", "Comprimé", "500 mg", new[] { "Métronidazole" }),
            ("birodogyl", "Birodogyl", "Comprimé", "", new[] { "Spiramycine", "Métronidazole" }),
            ("rovamycine-3m", "Rovamycine", "Comprimé", "3 MUI", new[] { "Spiramycine" }),
            ("dalacine-300", "Dalacine", "Gélule", "300 mg", new[] { "Clindamycine" }),
            ("zinnat-500", "Zinnat", "Comprimé", "500 mg", new[] { "Céfuroxime" }),
            ("pyostacine-500", "Pyostacine", "Comprimé", "500 mg", new[] { "Pristinamycine" }),

            // Corticoïdes
            ("solupred-20", "Solupred", "Comprimé orodispersible", "20 mg", new[] { "Prednisolone" }),
            ("celestene-2", "Célestène", "Comprimé", "2 mg", new[] { "Bétaméthasone" }),

            // Antiseptiques / bains de bouche
            ("eludril", "Eludril", "Solution pour bain de bouche", "", new[] { "Chlorhexidine" }),
            ("hextril", "Hextril", "Solution pour bain de bouche", "", new[] { "Hexétidine" }),

            // Antispasmodique / gastro
            ("spasfon-80", "Spasfon", "Comprimé", "80 mg", new[] { "Phloroglucinol" }),
            ("mopral-20", "Mopral", "Gélule", "20 mg", new[] { "Oméprazole" }),
            ("inexium-40", "Inexium", "Comprimé", "40 mg", new[] { "Ésoméprazole" }),

            // Chroniques fréquents
            ("amlor-5", "Amlor", "Gélule", "5 mg", new[] { "Amlodipine" }),
            ("glucophage-850", "Glucophage", "Comprimé", "850 mg", new[] { "Metformine" }),
            ("kardegic-160", "Kardégic", "Sachet", "160 mg", new[] { "Acide acétylsalicylique" }),
        };

        return raw
            .Select(r => new MedicationSeed(
                DeterministicGuid($"medication:{r.Key}"), r.Brand, r.Form, r.Strength, r.Dcis))
            .ToList();
    }

    private static List<IngredientSeed> BuildIngredients()
    {
        var list = new List<IngredientSeed>();
        foreach (var med in Medications)
        {
            foreach (var dci in med.Dcis)
            {
                list.Add(new IngredientSeed(
                    DeterministicGuid($"medication-dci:{med.Id}:{dci}"), med.Id, dci));
            }
        }
        return list;
    }

    /// <summary>Stable GUID derived from a key string (MD5) so the seed ids are identical on every machine
    /// and across re-generations.</summary>
    public static Guid DeterministicGuid(string key)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
