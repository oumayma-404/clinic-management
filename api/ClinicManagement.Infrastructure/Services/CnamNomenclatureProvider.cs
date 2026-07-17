using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// In-code CNAM dental nomenclature (Tunisia). Static reference data — no DB table, no migration.
/// Registered as a Singleton (immutable, shared across clinics).
///
/// ⚠ PENDING VERIFICATION: the codes acte, lettres clés and coefficients below are best-effort
/// starter defaults meant to cover every category, NOT an authoritative copy of the current CNAM
/// dental convention. Verify and complete them against the official CNAM nomenclature/convention
/// before relying on them clinically. Admin editing of this catalogue is a later feature.
/// </summary>
public class CnamNomenclatureProvider : ICnamNomenclatureProvider
{
    // Category labels (French) — the five buckets the UI filters by.
    private static class Categories
    {
        public const string Consultation = "Consultation";
        public const string SoinsConservateurs = "Soins conservateurs";
        public const string ChirurgieExtraction = "Chirurgie/Extraction";
        public const string Prothese = "Prothèse";
        public const string Radiologie = "Radiologie";
    }

    // Lettres clés used by the dental nomenclature (drive the indicative reimbursement estimate).
    private static class Cles
    {
        public const string CD = "CD";   // Consultation dentaire
        public const string CDS = "CDS"; // Consultation dentaire de spécialiste
        public const string VD = "VD";   // Visite dentaire
        public const string D = "D";     // Actes de soins dentaires
        public const string RD = "RD";   // Radiologie dentaire
    }

    private static readonly IReadOnlyList<CnamNomenclatureEntryDto> Entries = new List<CnamNomenclatureEntryDto>
    {
        // Consultation
        Entry("CONS", "Consultation dentaire", Cles.CD, 1, Categories.Consultation),
        Entry("CONS-SPE", "Consultation de spécialiste (ODF, parodontologie, chirurgie)", Cles.CDS, 1, Categories.Consultation),
        Entry("VIS-DOM", "Visite à domicile", Cles.VD, 1, Categories.Consultation),

        // Soins conservateurs
        Entry("DETART", "Détartrage (deux arcades)", Cles.D, 10, Categories.SoinsConservateurs),
        Entry("OBT-1F", "Obturation d'une face (amalgame ou composite)", Cles.D, 8, Categories.SoinsConservateurs),
        Entry("OBT-2F", "Obturation de deux faces", Cles.D, 12, Categories.SoinsConservateurs),
        Entry("OBT-3F", "Obturation de trois faces ou plus", Cles.D, 15, Categories.SoinsConservateurs),
        Entry("PULP", "Coiffage pulpaire / traitement d'une dent temporaire", Cles.D, 8, Categories.SoinsConservateurs),
        Entry("ENDO-MONO", "Traitement endodontique — dent monoradiculée", Cles.D, 20, Categories.SoinsConservateurs),
        Entry("ENDO-PLURI", "Traitement endodontique — dent pluriradiculée", Cles.D, 30, Categories.SoinsConservateurs),
        Entry("SCELLT", "Scellement de sillons", Cles.D, 5, Categories.SoinsConservateurs),

        // Chirurgie / Extraction
        Entry("EXT-SIMPLE", "Extraction d'une dent permanente", Cles.D, 10, Categories.ChirurgieExtraction),
        Entry("EXT-TEMP", "Extraction d'une dent temporaire", Cles.D, 6, Categories.ChirurgieExtraction),
        Entry("EXT-COMPLEXE", "Extraction complexe / dent incluse", Cles.D, 25, Categories.ChirurgieExtraction),
        Entry("ALVEOL", "Alvéolectomie / régularisation de crête", Cles.D, 20, Categories.ChirurgieExtraction),
        Entry("KYST", "Énucléation d'un kyste", Cles.D, 30, Categories.ChirurgieExtraction),
        Entry("FREIN", "Frénectomie", Cles.D, 15, Categories.ChirurgieExtraction),

        // Prothèse
        Entry("PROTH-1", "Prothèse adjointe partielle — 1 à 3 dents", Cles.D, 30, Categories.Prothese),
        Entry("PROTH-COMPL", "Prothèse adjointe complète (une arcade)", Cles.D, 60, Categories.Prothese),
        Entry("COURONNE", "Couronne coulée / céramo-métallique", Cles.D, 40, Categories.Prothese),
        Entry("BRIDGE-EL", "Bridge — élément intermédiaire", Cles.D, 40, Categories.Prothese),
        Entry("INLAY", "Inlay-core / reconstitution corono-radiculaire", Cles.D, 25, Categories.Prothese),
        Entry("REPAR-PROTH", "Réparation de prothèse", Cles.D, 10, Categories.Prothese),

        // Radiologie
        Entry("RETRO", "Radiographie rétro-alvéolaire", Cles.RD, 1, Categories.Radiologie),
        Entry("PANO", "Radiographie panoramique (orthopantomogramme)", Cles.RD, 5, Categories.Radiologie),
        Entry("TELERX", "Téléradiographie", Cles.RD, 5, Categories.Radiologie),
    };

    public IReadOnlyList<CnamNomenclatureEntryDto> GetAll() => Entries;

    private static CnamNomenclatureEntryDto Entry(
        string codeActe, string designationFr, string lettreCle, decimal coefficient, string category) =>
        new()
        {
            CodeActe = codeActe,
            DesignationFr = designationFr,
            LettreCle = lettreCle,
            Coefficient = coefficient,
            Category = category
        };
}
