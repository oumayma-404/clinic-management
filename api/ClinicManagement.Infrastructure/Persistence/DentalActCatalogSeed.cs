using System.Security.Cryptography;
using System.Text;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Canonical provisional seed for the global dental act catalog (chapitre <c>DCH</c> of the CNAM
/// "Liste des actes"). Single source of truth shared by the <c>AddDentalCore</c> migration (which inserts
/// these rows) and the seed-integrity unit tests — so the two can never drift. Every row seeds with
/// <c>IsProvisional = true</c> ("à vérifier") and no <c>Coefficient</c> (the cotation lives in the NGAP
/// arrêté, not the acts list) until an admin confirms/completes it. Lettre clé is "D" for every act.
/// Source: <c>features/dental-core/dental-nomenclature-source.md</c>.
/// </summary>
public static class DentalActCatalogSeed
{
    // Fixed seed timestamp (migrations are deterministic; no DateTime.Now in a seed).
    public static readonly DateTime SeededAtUtc = new(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc);

    // Category labels (French) — the six DCH sections the UI filters by.
    public const string SoinsConservateurs = "Soins conservateurs";
    public const string SoinsChirurgicaux = "Soins chirurgicaux";
    public const string Parodontologie = "Parodontologie";
    public const string Pedodontie = "Pédodontie";
    public const string OrthopedieDentoFaciale = "Orthopédie dento-faciale";
    public const string Prothese = "Prothèse";
    public const string Consultation = "Consultation";

    // Every dental act is cotée with the "D" lettre clé (CNAM dental key).
    public const string LettreCle = "D";

    public sealed record ActSeed(Guid Id, string CodeActe, string DesignationFr, string Category, bool RequiresAccordPrealable);

    public static IReadOnlyList<ActSeed> Acts { get; } = BuildActs();

    private static List<ActSeed> BuildActs()
    {
        // (Code, Désignation, Catégorie, AccordPréalable)
        var raw = new (string Code, string Designation, string Cat, bool Ap)[]
        {
            // Section I — Soins conservateurs, obturations définitives
            ("DCH010010", "Cavité simple — traitement global (obturation)", SoinsConservateurs, false),
            ("DCH010020", "Cavité simple — traitement global, dent permanente enfant < 14 ans", SoinsConservateurs, false),
            ("DCH010030", "Cavité composée — traitement global intéressant deux faces", SoinsConservateurs, false),
            ("DCH010040", "Cavité composée — deux faces, dent permanente enfant < 14 ans", SoinsConservateurs, false),
            ("DCH010050", "Traitement global intéressant trois faces et plus", SoinsConservateurs, false),
            ("DCH010060", "Pulpotomie / pulpectomie coronaire avec obturation de la chambre pulpaire (traitement global)", SoinsConservateurs, false),
            ("DCH010070", "Coiffage pulpaire / pulpectomie coronaire simple (hors obturation définitive)", SoinsConservateurs, false),
            ("DCH010080", "Pulpectomie coronaire et radiculaire, obturation des canaux — groupe incisivo-canin", SoinsConservateurs, false),
            ("DCH010090", "Pulpectomie coronaire et radiculaire — groupe prémolaire", SoinsConservateurs, false),
            ("DCH010100", "Pulpectomie coronaire et radiculaire — groupe molaire", SoinsConservateurs, false),

            // Section II — Soins chirurgicaux
            ("DCH020010", "Résection de capuchon muqueux d'une dent de sagesse", SoinsChirurgicaux, false),
            ("DCH020020", "Incision d'abcès et drainage", SoinsChirurgicaux, false),
            ("DCH020030", "Extraction dentaire simple — groupe incisivo-canin", SoinsChirurgicaux, false),
            ("DCH020040", "Extraction dentaire simple — groupe prémolaire", SoinsChirurgicaux, false),
            ("DCH020050", "Extraction dentaire simple — groupe molaire", SoinsChirurgicaux, false),
            ("DCH020060", "Extraction de plusieurs dents dans une même séance", SoinsChirurgicaux, false),
            ("DCH020070", "Extraction multiple — chacune des suivantes, groupe incisivo-canin", SoinsChirurgicaux, false),
            ("DCH020080", "Extraction multiple — chacune des suivantes, groupe prémolaire", SoinsChirurgicaux, false),
            ("DCH020090", "Extraction lors d'accidents inflammatoires/osseux aigus — majoration pour la première", SoinsChirurgicaux, false),
            ("DCH020100", "Extraction lors d'accidents aigus — majoration pour chacune des suivantes", SoinsChirurgicaux, false),
            ("DCH020110", "Extraction de la/des racine(s) d'une dent par alvéolectomie", SoinsChirurgicaux, false),
            ("DCH020120", "Extraction d'une dent en malposition", SoinsChirurgicaux, false),
            ("DCH020130", "Tamponnement alvéolaire pour hémorragie post-opératoire (séance autre que l'extraction)", SoinsChirurgicaux, false),
            ("DCH020140", "Extraction chirurgicale d'une dent incluse ou enclavée", SoinsChirurgicaux, false),
            ("DCH020150", "Extraction chirurgicale d'une canine incluse", SoinsChirurgicaux, false),
            ("DCH020160", "Extraction chirurgicale d'un odontoïde ou dent incluse/enclavée", SoinsChirurgicaux, false),
            ("DCH020170", "Dent en désinclusion, couronne partiellement/entièrement sous-muqueuse", SoinsChirurgicaux, false),
            ("DCH020180", "Dent en désinclusion, couronne sous-muqueuse position palatine ou linguale", SoinsChirurgicaux, false),
            ("DCH020190", "Dent ectopique et incluse (coroné, gonion, branche montante, bord basilaire)", SoinsChirurgicaux, false),
            ("DCH020200", "Germectomie", SoinsChirurgicaux, false),
            ("DCH020210", "Germectomie d'une dent de sagesse", SoinsChirurgicaux, false),
            ("DCH020220", "Extraction chirurgicale d'une dent permanente incluse (trait. radiculaire, réimplantation, contention) — une dent", SoinsChirurgicaux, false),
            ("DCH020230", "Extraction chirurgicale d'une dent permanente incluse — deux dents", SoinsChirurgicaux, false),
            ("DCH020240", "Dégagement chirurgical de la couronne d'une dent permanente incluse", SoinsChirurgicaux, false),
            ("DCH020250", "Régularisation localisée d'une crête alvéolaire", SoinsChirurgicaux, false),
            ("DCH020260", "Régularisation étendue de la crête alvéolaire (y compris suture)", SoinsChirurgicaux, false),
            ("DCH020270", "Régularisation de crête (hémimaxillaire ou canine à canine)", SoinsChirurgicaux, false),
            ("DCH020280", "Curetage périapical par trépanation vestibulaire, avec/sans résection apicale", SoinsChirurgicaux, false),
            ("DCH020290", "Exérèse kyste de petit volume par voie alvéolaire élargie", SoinsChirurgicaux, false),
            ("DCH020300", "Exérèse kyste étendu aux apex de deux dents (trépanation osseuse)", SoinsChirurgicaux, false),
            ("DCH020310", "Exérèse kyste étendu à un segment important du maxillaire", SoinsChirurgicaux, false),
            ("DCH020320", "Exérèse kyste corono-dentaire", SoinsChirurgicaux, false),
            ("DCH020330", "Cure d'un kyste par marsupialisation", SoinsChirurgicaux, false),
            ("DCH020340", "Chirurgie pré-prothétique — désinsertion musculaire vestibulaire partielle", SoinsChirurgicaux, false),
            ("DCH020350", "Désinsertion musculaire étendue à tout le vestibule", SoinsChirurgicaux, false),
            ("DCH020360", "Désinsertion musculaire du plancher de la bouche (section myo-hyoïdiens)", SoinsChirurgicaux, false),
            ("DCH020370", "Approfondissement d'un vestibule par greffe cutanée", SoinsChirurgicaux, false),

            // Section III — Hygiène bucco-dentaire & parodontopathies
            ("DCH030010", "Détartrage complet sus et sous gingival (quel que soit le nombre de séances)", Parodontologie, false),
            ("DCH030020", "Traitement des gingivites : détartrage, curetage, surfaçage radiculaire (4 séances max)", Parodontologie, true),
            ("DCH030030", "Gingivectomie partielle", Parodontologie, true),
            ("DCH030040", "Gingivectomie étendue à une hémi-arcade ou canine à canine", Parodontologie, true),
            ("DCH030050", "Intervention à lambeaux (curetage, surfaçage, suture) — de 1 à 3 dents", Parodontologie, true),
            ("DCH030060", "Intervention à lambeaux — par dent supplémentaire", Parodontologie, true),
            ("DCH030070", "Intervention à lambeau + traitement d'une lésion osseuse par comblement et suture", Parodontologie, true),
            ("DCH030080", "Greffe gingivale libre (prélèvement + suture)", Parodontologie, true),
            ("DCH030090", "Hémi-section molaire inférieure / amputation radiculaire molaire supérieure", Parodontologie, false),
            ("DCH030100", "Ligature métallique dans les parodontopathies", Parodontologie, false),
            ("DCH030110", "Attelle métallique dans les parodontopathies", Parodontologie, false),
            ("DCH030120", "Prothèse attelle de contention (quel que soit le nb de dents/crochets)", Parodontologie, false),
            ("DCH030130", "Analyse occlusale avec examen de labo et meulage sélectif", Parodontologie, false),
            ("DCH030140", "Frénectomie (excision du frein labial)", Parodontologie, false),

            // Section IV — Pédodontie / Prévention
            ("DCH040010", "Couronne pédodontique préformée", Pedodontie, false),
            ("DCH040020", "Résine de scellement des puits et fissures (sealants)", Pedodontie, false),
            ("DCH040030", "Application topique de fluor par gouttière préfabriquée (5 séances max), par séance", Pedodontie, false),
            ("DCH040040", "Application topique de fluor par gouttière thermoformée", Pedodontie, false),
            ("DCH040050", "Mainteneur d'espace fixe", Pedodontie, false),
            ("DCH040060", "Appareillage fixe pour blocage d'éruption", Pedodontie, false),
            ("DCH040070", "Guide d'éruption", Pedodontie, false),
            ("DCH040080", "Appareil d'interception mobile", Pedodontie, false),

            // Section V — Orthopédie dento-faciale
            ("DCH050010", "Examen + prise d'empreintes, diagnostic et durée probable du traitement", OrthopedieDentoFaciale, true),
            ("DCH050020", "Analyse céphalométrique (en supplément)", OrthopedieDentoFaciale, true),
            ("DCH050030", "Traitement préventif par dispositif orthopédique", OrthopedieDentoFaciale, true),
            ("DCH050040", "Rééducation neuro-musculaire (série de 12 séances renouvelables), par séance", OrthopedieDentoFaciale, true),
            ("DCH050050", "Traitement simple ne dépassant pas 6 mois", OrthopedieDentoFaciale, true),
            ("DCH050060", "Traitement simple ne dépassant pas 12 mois", OrthopedieDentoFaciale, true),
            ("DCH050070", "Dysmorphoses importantes — première année", OrthopedieDentoFaciale, true),
            ("DCH050080", "Dysmorphoses importantes — deuxième année", OrthopedieDentoFaciale, true),
            ("DCH050090", "Dysmorphoses importantes — troisième année", OrthopedieDentoFaciale, true),
            ("DCH050100", "Contention après traitement orthodontique — première année", OrthopedieDentoFaciale, true),
            ("DCH050110", "Contention après traitement orthodontique — deuxième année", OrthopedieDentoFaciale, true),
            ("DCH050120", "Disjonction intermaxillaire rapide (insuffisance respiratoire confirmée)", OrthopedieDentoFaciale, true),
            ("DCH050130", "Mise en place sur l'arcade d'une dent permanente incluse — une dent", OrthopedieDentoFaciale, true),
            ("DCH050140", "Mise en place sur l'arcade d'une dent permanente incluse — deux dents", OrthopedieDentoFaciale, true),
            ("DCH050150", "Orthopédie des malformations (bec de lièvre / division palatine) — forfait annuel", OrthopedieDentoFaciale, true),
            ("DCH050160", "Orthopédie des malformations — en période d'attente", OrthopedieDentoFaciale, true),

            // Section VI — Prothèse dentaire (adjointe)
            //
            // ⚠️ Every row here shipped as `RequiresAccordPrealable = true` and is now `false`. Since **April
            // 2019** dental prostheses are covered *hors plafond* and **without** a demande d'accord préalable —
            // consistent with the convention's art. 7 « ou hors plafond » wording (sourced: Tunisian press;
            // graded **Likely**, no primary arrêté retrieved). The flag now reaches the BS1 editor, so a wrong
            // flag is a wrong warning printed in front of the patient rather than a dormant column.
            //
            // ⚠️ `Parodontologie` and `OrthopedieDentoFaciale` are deliberately left flagged exactly as they
            // were. The convention (art. 24) confirms the *mechanism* in detail, but **which act families need
            // it is fixed by an arrêté conjoint the research could not retrieve** — so the sourced correction is
            // applied and the rest is left alone. Do not invent the list; the admin screen says it is
            // unverified and the flag is editable per clinic.
            ("DCH060010", "Prothèse adjointe — appareillage de 1 à 3 dents", Prothese, false),
            ("DCH060020", "Prothèse adjointe — par dent supplémentaire", Prothese, false),
            ("DCH060030", "Appareillage complet haut et bas", Prothese, false),
            ("DCH060040", "Dent prothétique contre-plaquée sur plaque base plastique (supplément)", Prothese, false),
            ("DCH060050", "Plaque base métallique coulée (supplément)", Prothese, false),
            ("DCH060060", "Dent prothétique contreplaquée/massive soudée sur plaque base métallique (supplément)", Prothese, false),
            ("DCH060070", "Réparation de fracture sur plaque base plastique", Prothese, false),
            ("DCH060080", "Dents/crochets ajoutés/remplacés sur appareil plastique — premier élément", Prothese, false),
            ("DCH060090", "Dents/crochets ajoutés/remplacés — élément suivant", Prothese, false),
            ("DCH060100", "Dents/crochets soudés, ajoutés/remplacés sur appareil métallique (par élément)", Prothese, false),
            ("DCH060110", "Réparation de fracture de la plaque base métallique", Prothese, false),
            ("DCH060120", "Dents/crochets remontés sur plastique après réparation", Prothese, false),
            ("DCH060130", "Rebasage", Prothese, false),
            ("DCH060140", "Prothèse avec attachement (par élément)", Prothese, false),
            ("DCH060150", "Remplacement de facette ou dent à tube", Prothese, false),
        };

        return raw
            .Select(r => new ActSeed(DeterministicGuid($"dental-act:{r.Code}"), r.Code, r.Designation, r.Cat, r.Ap))
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="codeActe"/> is an act this seed once shipped as requiring an accord préalable and
    /// no longer does — i.e. the rows the startup correction may clear on an already-seeded clinic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived from the table above (a Prothèse act now flagged <c>false</c>) rather than a second hand-written
    /// list of the fifteen <c>DCH06…</c> codes, which would have to be kept in step with it by hand.
    /// </para>
    /// <para>
    /// ⚠️ Same predicate discipline as <c>CnamCatalogSeed.SupersededLetterValue</c>: this only says the seeded
    /// default *was* wrong. The correction must additionally require the row to be untouched since seeding
    /// (<c>UpdatedAt == null</c>), still provisional, and still carrying the wrong flag — a clinic that has
    /// deliberately re-flagged an act keeps its own answer.
    /// </para>
    /// </remarks>
    public static bool SupersededAccordPrealable(string? codeActe)
    {
        if (string.IsNullOrWhiteSpace(codeActe))
        {
            return false;
        }

        var trimmed = codeActe.Trim();
        return Acts.Any(a =>
            string.Equals(a.CodeActe, trimmed, StringComparison.OrdinalIgnoreCase)
            && a.Category == Prothese
            && !a.RequiresAccordPrealable);
    }

    /// <summary>
    /// The two <b>consultations</b>, which chapitre DCH does not contain — it lists acts on teeth and gums, and a
    /// consultation is priced by the convention's own honoraires table instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Source: <b>Convention sectorielle des médecins dentistes de libre pratique</b>, honoraires conventionnels
    /// table — <c>Cd</c> « Consultation du médecin dentiste » 30,000 DT and <c>Cds</c> « Consultation du médecin
    /// dentiste spécialiste (orthodontiste) » 45,000 DT, both dating from 01/01/2021 and unchanged in CNAM's
    /// December-2022 tariff table. Both lettres clés are defined by the NGAP arrêté du 1er juin 2006, art. 4.
    /// </para>
    /// <para>
    /// ⚠️ <b>The code IS the lettre clé</b>, because the CNAM publishes no code for a consultation and inventing
    /// one is what produced the catalogue this feature retired. <c>Coefficient</c> is 1 so the ordinary
    /// <c>coefficient × VLC × taux</c> estimate lands on the conventional fee with no special case anywhere — the
    /// convention prices these as lettres clés, so this is its model, not a workaround for it.
    /// </para>
    /// <para>
    /// ⚠️ <b>A separate list from <see cref="Acts"/> on purpose.</b> The shipped <c>AddDentalCore</c> migration
    /// iterates <c>Acts</c> and inserts every row with the hard-coded <see cref="LettreCle"/> and a null
    /// coefficient — appending these there would give them lettre clé « D » on a fresh database and nothing at all
    /// on an existing one. <c>ClinicCatalogSeeder</c> tops them up <b>by code</b> instead.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ConsultationSeed> ConsultationActs { get; } = new[]
    {
        new ConsultationSeed("Cd", "Consultation du médecin dentiste", CnamCatalogSeed.CD),
        new ConsultationSeed("Cds", "Consultation du médecin dentiste spécialiste (orthodontiste)", CnamCatalogSeed.CDS),
    };

    /// <summary>A consultation act: its own lettre clé, and a coefficient of 1 (the fee IS the valeur).</summary>
    public sealed record ConsultationSeed(string CodeActe, string DesignationFr, string LettreCle)
    {
        public const decimal Cotation = 1m;
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
