using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.ProcedureTypes;

/// <summary>
/// A deliberately **general** starter set of the dental procedures a Tunisian private practice performs — one
/// row per act a dentist actually books and bills, not per clinical variant (practitioner feedback: the earlier
/// 43-row list split hairs — « 1 face » vs « 2-3 faces », mono- vs pluriradiculaire, céramo-métal vs zircone —
/// which read as noise). Broad coverage, NOT limited to CNAM-reimbursed acts (couronnes, implants, blanchiment,
/// facettes, orthodontie…). Each row prefills a clinic's <see cref="ProcedureType"/> menu with a typical
/// Tunisian private-practice price (TND) and duration; every value is fully editable by the clinic afterwards,
/// and clinics wanting a finer breakdown add their own rows. Prices are indicative midpoints (they vary widely
/// by city/tier), meant as a starting point. Used both to seed a new clinic on creation and to backfill an
/// existing clinic's menu on demand.
///
/// <para><b>⚠️ No row may sit below the CNOMDT floor.</b> The Conseil National de l'Ordre des Médecins Dentistes
/// de Tunisie publishes a <i>barème d'honoraires minimums</i> (adopted 27 December 2020), and article 30 of the
/// code de déontologie forbids a dentist charging under it. Two rows shipped below it — détartrage at 60 against
/// a floor of 90, and blanchiment at 400 against 500 — so the product's own defaults invited every new clinic to
/// break that rule. <c>ProcedureTypeCatalogSeedFloorTests</c> holds the whole list against the barème now.</para>
///
/// <para>⚠️ The list is 19 rows <b>on purpose</b>. It was 43 and was cut on practitioner feedback for splitting
/// hairs — « 1 face » vs « 2-3 faces », mono- vs pluriradiculaire, céramo-métal vs zircone (<c>feef4d8a</c>).
/// Re-adding a clinical <i>variant</i> of a row that already exists is re-opening a closed decision; a genuinely
/// distinct act that has no row at all is a different question.</para>
/// </summary>
public static class ProcedureTypeCatalogSeed
{
    /// <summary>
    /// One starter act. <paramref name="ResultingCondition"/> is <b>tri-state</b>: <c>null</c> takes the
    /// discipline's default from <see cref="CategoryResultingConditions"/>, and <c>Sain</c> means « this act
    /// charts nothing » — the entity already reads <c>Sain</c> as no state, so the two say different things.
    ///
    /// <para>⚠️ It exists because three acts would otherwise be mis-charted by their own discipline: an
    /// inlay-core is filed under Prothèse fixe but does not put a crown on the tooth, draining an abscess is
    /// filed under Chirurgie but does not remove it, and a bone graft is filed under Implantologie but is not an
    /// implant. Each would have written a state the patient's odontogram does not have.</para>
    /// </summary>
    public sealed record SeedRow(
        string Name,
        int DurationMinutes,
        decimal DefaultCost,
        string Category,
        ToothCondition? ResultingCondition = null,
        ProcedureStepTemplate[]? DefaultSteps = null);

    /// <summary>
    /// Suggested step protocols for the acts a cabinet cannot do in one séance.
    /// <para>
    /// ⚠️ <b>They ship only where the multi-séance shape is not in question</b> — a seeded protocol is the vendor
    /// putting clinical words in a practitioner's mouth. <b>Soin de carie is NOT seeded</b> — « curetage,
    /// scellement définitif » was raised and then set aside (« on ne le propose pas pour le moment »), and a
    /// cabinet that wants it adds it to its own catalogue in two clicks.
    /// </para>
    /// <para>
    /// Every step is a <i>suggestion</i>: it is copied onto a devis line and owned there, so a bridge that takes
    /// five séances for this patient is edited on the plan, not here. Durations sum to more than the act's own
    /// <c>DurationMinutes</c> on purpose — that field is one sitting at the chair, these are several.
    /// </para>
    /// </summary>
    /*
     * ═══ Protocoles par défaut — les séances qu'un acte demande ═════════════════════════════════════════
     *
     * Each array is the sequence of APPOINTMENTS a dentist books for that act, and every one of them was
     * researched against clinical sources rather than invented. Sources, per discipline: HAS 2018
     * (assainissement parodontal — the quadrant/sextant split is verbatim from it), Université Constantine 3
     * faculté de médecine dentaire (reconstitution corono-radiculaire, PPAC, gingivectomies, avulsion), a 2022
     * thèse de chirurgie dentaire de l'Université de Lille (rebasage), the SFSCMFCO's *Recommandations de
     * Bonne Pratique* (gouttière occlusale, grade B), the ITI consensus (implant loading protocols),
     * Cochrane 2022 (endodontie une vs deux séances), Les Cahiers de Prothèse (essai d'armature) and the
     * francophone clinical press.
     *
     * ⚠️ **Many acts have NO protocol, and that is the finding rather than an omission.** A consultation, a
     * radiographie, une extraction simple, un détartrage, un scellement de sillons, une couronne pédodontique
     * préformée (« en une seule séance, sans intervention du laboratoire ») and — against the textbook reflex —
     * **un traitement de canal** are single-séance acts: Cochrane finds no difference in success between one and
     * two visits over 47 studies, and the French clinical position is that one séance is the default. Giving
     * those a protocol would put a second appointment in front of every dentist who books one, which is the
     * opposite of the help this feature is for. (A functional review called the canal the catalogue's clearest
     * gap on the strength of the two-sitting molar habit; the sourced position above is the answer, and it is
     * the reason the act stays bare.)
     *
     * ⚠️ **A second séance almost always has one of two physical causes: a laboratory in the loop, or a
     * medicated / biological interval.** That is the rule to apply when a new act is added here — not a habit
     * of splitting work into stages.
     *
     * ⚠️ **The durations are defaults a practice edits, and they are the weakest half of this data.** Clinical
     * literature publishes protocols, not minutes; only a handful of these figures are source-stated (couronne
     * séance 1 ≈ 1 h 30, facette préparation and collage 2–3 h, surfaçage 45–60 min per quadrant, blanchiment
     * au fauteuil 30–90 min). The rest are reasoned from what the séance contains. A clinic's own history is a
     * better authority, which is why every step here is editable per act in « Types de procédures ».
     *
     * ⚠️ **The third figure is the INTERVAL, and it is the better-sourced half.** The literature above states
     * elapsed time far more precisely than it states chair time — « les séances sont espacées d'une semaine
     * environ », « la réévaluation est à 8 semaines minimum », « dépose du pansement ou des sutures à 7–10
     * jours », three to six months of ostéointégration — and the schema had nowhere to put any of it, so every
     * one of those findings was discarded at the model boundary. Consequences, all of them silent: the worklist
     * alarmed at a flat fortnight, so an implant progressing exactly to its own protocol read as abandoned for
     * ten of its twelve weeks; there was no « pas encore due » state at all; and booking the next séance could
     * offer no date though the protocol knew roughly when it should be. `MinDaysAfterPrevious` is a **minimum**,
     * never a due date, and `null` means the interval is clinically free rather than zero.
     */

    /// <summary>
    /// Couronne / bridge. <b>Préparation and empreinte are ONE séance</b> — the provisoire is made chairside in
    /// the same sitting (« la taille de la dent, la fabrication au fauteuil d'une couronne provisoire et la
    /// prise d'empreinte, ce rendez-vous durera environ 1 h 30 »), which is why this is not four steps.
    /// <para>
    /// The armature try-in is <b>genuinely optional and source-stated as such</b> (Cahiers de Prothèse n°157):
    /// skipped on a unitary crown, the norm on an extended bridge. It is seeded because deleting a step a
    /// practice does not need is easier than knowing to add one it does — and this act covers both.
    /// </para>
    /// </summary>
    private static readonly ProcedureStepTemplate[] FixedProsthesisSteps =
    [
        new("Préparation + empreinte", 90),
        new("Essai de l'armature", 30, MinDaysAfterPrevious: 7),
        new("Essayage + scellement définitif", 45, MinDaysAfterPrevious: 7),
    ];

    /// <summary>
    /// Inlay-core. Verbatim from the Constantine 3 5ᵉ-année course.
    /// <para>
    /// ⚠️ <b>Séance 2 doubles as séance 1 of the couronne</b>: the faux moignon is cemented and the crown
    /// impression, provisoire and teinte are taken in the same sitting. Inlay-core + couronne is therefore
    /// <b>3 séances, not 4</b> — chaining the two protocols naively over-books the patient by one visit.
    /// </para>
    /// </summary>
    private static readonly ProcedureStepTemplate[] InlayCoreSteps =
    [
        new("Préparation canalaire + empreinte", 60),
        new("Essayage + scellement du faux moignon", 45, MinDaysAfterPrevious: 7),
    ];

    /// <summary>
    /// Facette. Three sources give three counts (2–3, 4, 4–6) and the disagreement is entirely about the
    /// projet esthétique, which one of them calls « optionnelle » outright. Four is the most common.
    /// <para>
    /// ⚠️ <b>The durations are PER ÉLÉMENT, and they were per case.</b> « Préparation + provisoires 150 min » is
    /// a six-to-eight-tooth appointment, but the act is priced per element like « Couronne / bridge (par
    /// élément) » — so a six-veneer smile is six lines, and the devis proposed 24 séances and about forty hours
    /// of chair time for work that is two long appointments. One veneer is roughly 45–60 min to prepare and 45
    /// to bond; the act's name now says which unit it is quoted in, matching the discipline the rest of the
    /// catalogue keeps.
    /// </para>
    /// </summary>
    private static readonly ProcedureStepTemplate[] VeneerSteps =
    [
        new("Bilan esthétique + empreintes", 60),
        // « mock-up » was the only anglicism in the set, and these labels are printed on a devis the patient
        // reads. « Projet esthétique » is the standard French phrase for the same appointment.
        new("Validation du projet esthétique", 45, MinDaysAfterPrevious: 10),
        new("Préparation + provisoires", 60, MinDaysAfterPrevious: 7),
        new("Collage définitif", 45, MinDaysAfterPrevious: 10),
    ];

    /// <summary>
    /// Prothèse amovible. Each séance is separated by real laboratory work — the PEI, then the maquettes
    /// d'occlusion, then the montage des dents — so the count is forced by the lab, not chosen.
    /// <para>
    /// ⚠️ A hard biological constraint the protocol cannot express: <b>4 to 6 weeks minimum</b> between the last
    /// extraction and the empreinte secondaire. A prosthesis impressed a week after extractions will not fit.
    /// </para>
    /// <para>The contrôle is seeded because « plusieurs séances de retouches » is the documented norm and the
    /// first one is booked at delivery: 24–48 h, then a week, then a month.</para>
    /// </summary>
    private static readonly ProcedureStepTemplate[] RemovableProsthesisSteps =
    [
        new("Empreinte primaire", 30),
        // The 4–6 weeks the note above calls a hard biological constraint, stated as data now rather than as
        // prose the schema had nowhere to keep: a prosthesis impressed a week after extractions will not fit.
        new("Empreinte secondaire", 45, MinDaysAfterPrevious: 28),
        new("Rapports intermaxillaires", 45, MinDaysAfterPrevious: 7),
        new("Essai des dents en cire", 30, MinDaysAfterPrevious: 7),
        new("Mise en bouche", 45, MinDaysAfterPrevious: 7),
        new("Contrôle et retouches", 30, MinDaysAfterPrevious: 2),
    ];

    /// <summary>
    /// Rebasage. The <b>indirect</b> method is seeded because the Lille thesis calls it « la méthode de
    /// référence » — but the <b>direct</b> one is explicitly « réalisé au cabinet, directement en bouche, en une
    /// séance », so a practice that works that way deletes the second step.
    /// <para>⚠️ Between the two séances the patient has <b>no denture</b>. That is a scheduling constraint, and
    /// it is precisely why the direct method exists for geriatric patients.</para>
    /// </summary>
    private static readonly ProcedureStepTemplate[] DentureRelineSteps =
    [
        new("Empreinte de rebasage", 30),
        new("Remise de la prothèse rebasée", 30, MinDaysAfterPrevious: 2),
    ];

    /// <summary>
    /// Gouttière occlusale. The réglage at delivery is the norm, not the exception (« Le plus souvent, des
    /// réglages sont nécessaires, ce qui est tout à fait normal »), and the SFSCMFCO's grade-B recommendation
    /// prescribes « un port de sommeil durant 2 mois, avec un suivi régulier » — which is what makes the third
    /// séance protocol rather than an upsell.
    /// </summary>
    private static readonly ProcedureStepTemplate[] OcclusalSplintSteps =
    [
        new("Empreintes + enregistrement occlusal", 45),
        new("Pose et réglage occlusal", 45, MinDaysAfterPrevious: 10),
        // « un port de sommeil durant 2 mois, avec un suivi régulier » — SFSCMFCO, grade B.
        new("Contrôle et réglage", 30, MinDaysAfterPrevious: 21),
    ];

    /// <summary>
    /// Implant — <b>the surgical phase only</b>, which is what this 1 500 DT act sells.
    /// <para>
    /// ⚠️ <b>It used to run to six séances, and the last three were a second act the catalogue sells
    /// separately.</b> « Désenfouissement · Empreinte implantaire · Pose de la couronne » is the prosthetic
    /// phase, and « Couronne / bridge (par élément) » is 500 DT with its own three-step protocol — so an implant
    /// line proposed six visits that included a crown nobody had put on the devis. The séance count is what a
    /// patient reads as *what I am paying for*, so the split is at « Contrôle post-opératoire » and the crown is
    /// its own line, quoted and billed as one.
    /// </para>
    /// <para>
    /// The 3–6 month wait before the prosthetic phase is ostéointégration and is the ITI's own definition of
    /// conventional loading (« the prosthesis is attached in a second procedure after a healing period of 3 to 6
    /// months »). It now sits on the crown line's own first step rather than being lost between two acts.
    /// <b>Mise en charge immédiate</b> collapses the two into one sitting — legitimate, and expressed by booking
    /// both acts into one séance, which the workspace offers.
    /// </para>
    /// </summary>
    private static readonly ProcedureStepTemplate[] ImplantSteps =
    [
        new("Bilan pré-implantaire", 45),
        new("Pose de l'implant", 90, MinDaysAfterPrevious: 7),
        new("Contrôle post-opératoire", 20, MinDaysAfterPrevious: 8),
    ];

    /// <summary>
    /// The prosthetic phase of an implant — the crown, quoted on its own line at its own fee.
    /// <para>
    /// The three séances the implant act used to swallow, with ostéointégration stated where it belongs: 90 days
    /// before the désenfouissement, the shorter end of the ITI's 3–6 months, so the worklist waits rather than
    /// alarming. A practice working « en un temps » deletes the first step.
    /// </para>
    /// </summary>
    private static readonly ProcedureStepTemplate[] ImplantCrownSteps =
    [
        new("Désenfouissement", 30, MinDaysAfterPrevious: 90),
        new("Empreinte implantaire", 30, MinDaysAfterPrevious: 14),
        new("Pose de la couronne", 30, MinDaysAfterPrevious: 10),
    ];

    /// <summary>Greffe osseuse / comblement de sinus. One operative visit plus the suture check; the 4–9 month
    /// wait it imposes is on the <i>implant</i> that follows, not inside this act. Where residual bone height
    /// allows, the implant is placed in the same séance as the graft and this act adds no visit at all.</summary>
    private static readonly ProcedureStepTemplate[] BoneGraftSteps =
    [
        new("Greffe osseuse", 90),
        new("Contrôle post-opératoire", 20, MinDaysAfterPrevious: 8),
    ];

    /// <summary>
    /// Gingivectomie. Dépose du pansement parodontal or of the sutures at 7–10 days (verbatim), which the laser
    /// technique may remove the need for.
    /// <para>
    /// ⚠️ <b>« Réévaluation parodontale » was step 1 here and is step 5 of « Traitement parodontal ».</b> Quote
    /// both — which is the normal sequence, since the non-surgical phase is a prerequisite — and the same visit
    /// was proposed twice, on two lines, at two prices. It is also arguably not a *stage* of a gingivectomie at
    /// all: a réévaluation before the act is the decision whether to do it, which the catalogue already sells as
    /// « Consultation / examen bucco-dentaire » and « Contrôle / suivi ». The prerequisite is real and stays in
    /// this note; what it is not is a séance of this act.
    /// </para>
    /// </summary>
    private static readonly ProcedureStepTemplate[] GingivectomySteps =
    [
        new("Gingivectomie", 60),
        // « Dépose du pansement » alone is clinically correct and opaque to a patient reading the devis PDF.
        new("Dépose du pansement parodontal", 20, MinDaysAfterPrevious: 7),
    ];

    /// <summary>Frénectomie. Sutures are resorbable, so the second séance is a healing check at 10–15 days
    /// rather than a removal appointment.</summary>
    private static readonly ProcedureStepTemplate[] FrenectomySteps =
    [
        new("Frénectomie", 30),
        // Resorbable sutures, so this is a healing check at 10–15 days, not a removal appointment.
        new("Contrôle post-opératoire", 15, MinDaysAfterPrevious: 10),
    ];

    /// <summary>
    /// Incision d'abcès. ⚠️ The drain comes out at <b>1 to 2 days</b> — by far the shortest post-operative
    /// interval in this catalogue, and the reason a single « contrôle à 7 jours » habit would be clinically
    /// wrong here. The causal tooth is a <i>separate act</i> (an extraction or a traitement de canal), done in
    /// the same sitting where possible; it is deliberately not a step of this one.
    /// </summary>
    private static readonly ProcedureStepTemplate[] AbscessDrainageSteps =
    [
        new("Incision et drainage", 30),
        // 1 to 2 days — by far the shortest post-operative interval in this catalogue, and the reason a
        // single « contrôle à 7 jours » habit would be clinically wrong here.
        new("Contrôle et retrait du drain", 15, MinDaysAfterPrevious: 1),
    ];

    /// <summary>
    /// Traitement parodontal. <b>Split by anatomy, not by stage</b>, and the split is verbatim from the HAS:
    /// « l'assainissement est effectué en plusieurs séances par quadrant ou par sextant ; chaque séance dure
    /// entre 45 minutes et 1 heure, et les séances sont espacées d'une semaine environ ».
    /// <para>
    /// Quadrants are seeded because they are the shorter of the two standard splits; a practice working by
    /// sextant adds two. The réévaluation is at <b>8 weeks minimum</b> — not one week — and is what decides
    /// whether a second DSR or surgery follows.
    /// </para>
    /// <para>The « désinfection globale » alternative compresses this to one or two long séances. HAS found no
    /// consistent evidence it is superior and the SFPIO does not recommend it systematically, so it is not the
    /// default here.</para>
    /// </summary>
    private static readonly ProcedureStepTemplate[] PeriodontalTherapySteps =
    [
        new("Surfaçage 1er quadrant", 60),
        // « les séances sont espacées d'une semaine environ » — HAS 2018, verbatim.
        new("Surfaçage 2e quadrant", 60, MinDaysAfterPrevious: 7),
        new("Surfaçage 3e quadrant", 60, MinDaysAfterPrevious: 7),
        new("Surfaçage 4e quadrant", 60, MinDaysAfterPrevious: 7),
        // « la réévaluation est à 8 semaines minimum » — HAS 2018, and NOT one week. A whole treatment reading
        // as neglected for seven of those eight weeks is what the interval column exists to stop.
        new("Réévaluation parodontale", 30, MinDaysAfterPrevious: 56),
    ];

    /// <summary>
    /// Retraitement endodontique — the one endodontic act where two séances is the realistic default, because
    /// of the mature bacterial biofilm. 14 days of hydroxyde de calcium between them. A single-séance
    /// retreatment is legitimate (equal periapical healing at 18 months in an RCT), so the second step is
    /// deletable.
    /// </summary>
    private static readonly ProcedureStepTemplate[] EndoRetreatmentSteps =
    [
        new("Dépose et désinfection", 105),
        // 14 days of hydroxyde de calcium between the two séances.
        new("Réobturation canalaire", 50, MinDaysAfterPrevious: 14),
    ];

    /// <summary>Mainteneur d'espace fixe. « Deux séances sont nécessaires » — verbatim — and the cause is
    /// unambiguous: a prosthetics lab sits between the impression and the cementation.</summary>
    private static readonly ProcedureStepTemplate[] SpaceMaintainerSteps =
    [
        new("Empreinte mainteneur d'espace", 40),
        new("Scellement du mainteneur", 25, MinDaysAfterPrevious: 7),
    ];

    /// <summary>
    /// Orthodontie multi-attaches — <b>the four milestones, deliberately not the monthly activations</b>.
    /// <para>
    /// ⚠️ A practice does not enumerate a two-year treatment as twenty rows, and three independent lines of
    /// evidence say so: clinically the treatment is described in phases with the active phase written as
    /// « visites mensuelles » generically; the billing unit is the <b>semestre</b> (capped at six, i.e. three
    /// years) with surveillance visits counted against a per-semester allowance; and orthodontic software
    /// schedules those visits as <i>recurring</i> rather than pre-listing them. Enumerating them would be wrong
    /// clinically (the count is not knowable at the start) and wrong on the money (the fee is per semester).
    /// </para>
    /// <para>The activations are booked as « Séance orthodontique (contrôle / activation) », which is its own
    /// act in this catalogue and is single-séance by definition, every 4–8 weeks.</para>
    /// </summary>
    private static readonly ProcedureStepTemplate[] OrthodonticSteps =
    [
        new("Bilan orthodontique", 60),
        new("Pose de l'appareillage", 90, MinDaysAfterPrevious: 14),
        // The active phase, stated as an interval rather than as twenty rows: about eighteen months, so the
        // worklist waits instead of reporting a treatment running exactly to plan as forgotten.
        new("Dépose de l'appareillage", 75, MinDaysAfterPrevious: 540),
        new("Pose de la contention", 40, MinDaysAfterPrevious: 1),
    ];

    /// <summary>
    /// Extraction chirurgicale. The sutures come out at 7–10 days, which is the same post-operative check
    /// « Incision d'abcès » at 40 DT already carried while this 200 DT act carried none.
    /// </summary>
    private static readonly ProcedureStepTemplate[] SurgicalExtractionSteps =
    [
        new("Extraction chirurgicale", 60),
        new("Dépose des sutures", 15, MinDaysAfterPrevious: 7),
    ];

    /// <summary>
    /// Contention post-orthodontique — empreinte, then pose once the laboratory has made the fil ou la
    /// gouttière. Exactly the shape of « Mainteneur d'espace fixe », which has carried this protocol all along:
    /// a prosthetics lab sits between the two séances, which is the seed's own stated test for a second visit.
    /// </summary>
    private static readonly ProcedureStepTemplate[] RetainerSteps =
    [
        new("Empreinte de contention", 30),
        new("Pose de la contention", 30, MinDaysAfterPrevious: 7),
    ];

    /// <summary>
    /// Blanchiment ambulatoire — empreintes, remise des gouttières, contrôle de la teinte. The shape of
    /// « Gouttière occlusale », which has carried a three-step protocol all along.
    /// <para>
    /// A cabinet doing <b>blanchiment au fauteuil</b> (30–90 min, one sitting) deletes all three: this is the
    /// ambulatory protocol, and it is the one that needs a lab and an interval.
    /// </para>
    /// </summary>
    private static readonly ProcedureStepTemplate[] WhiteningSteps =
    [
        new("Empreintes pour gouttières", 30),
        new("Remise des gouttières", 20, MinDaysAfterPrevious: 7),
        new("Contrôle de la teinte", 20, MinDaysAfterPrevious: 14),
    ];

    /// <summary>
    /// Category → palette colour (must be a value <see cref="ColorHex"/> accepts; the picker's palette is served
    /// from there, so there is no second copy to keep in step).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Every category must own a distinct hex, and two pairs did not.</b> « Esthétique » shipped on
    /// « Orthodontie »'s <c>#FB7185</c> and « Pédodontie » on « Parodontologie »'s <c>#6BAA75</c>, so four
    /// disciplines rendered as two colours: a facette and a séance orthodontique were the same pink in the agenda,
    /// and a détartrage and a soin d'enfant the same green. The colour is the only thing distinguishing two
    /// appointment blocks at a glance, so the collision cost exactly the capability it exists to provide — and it
    /// was invisible in code review, because each line is correct on its own.
    /// <para>
    /// The replacements are the <i>Clair</i> nuance of the same hue family the collision was in, so the discipline
    /// still reads as related to its neighbour rather than as an unrelated new colour:
    /// « Esthétique » → rose Clair, « Pédodontie » → vert Clair. <c>CategoryColoursAreDistinctTests</c> is the
    /// derived guard; a thirteenth category that reuses a hex fails there rather than in a cabinet's agenda.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Category → palette **hue family** (a key of <see cref="ColorHex.GetPalette"/>). The act's own colour is a
    /// <i>tone</i> of it, picked per act — see <see cref="ColourFor"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Every category must own a distinct family, and two pairs did not.</b> « Esthétique » shipped on
    /// « Orthodontie »'s rose and « Pédodontie » on « Parodontologie »'s vert, so four disciplines rendered as two
    /// hues: a facette and a séance orthodontique were the same pink in the agenda, and a détartrage and a soin
    /// d'enfant the same green. The first fix moved the two onto the <i>Clair</i> nuance of the family they
    /// collided with — which un-collided the categories but spent the nuance that the acts inside them now need.
    /// So the two move to the palette's remaining free families instead: Pédodontie → olive, Esthétique → indigo.
    /// <para>
    /// The other ten keep the family whose <i>Moyen</i> tone they already carried, so the first act of each
    /// discipline is the colour that discipline has always been.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> CategoryFamilies = new()
    {
        ["Consultation"] = "slate",
        ["Radiologie"] = "sky",
        ["Soins conservateurs"] = "teal",
        ["Endodontie"] = "blue",
        ["Parodontologie"] = "green",
        ["Chirurgie/Extraction"] = "coral",
        ["Prothèse fixe"] = "violet",
        ["Prothèse amovible"] = "mint",
        ["Implantologie"] = "amber",
        ["Orthodontie"] = "rose",
        ["Esthétique"] = "indigo",
        ["Pédodontie"] = "olive",
    };

    /// <summary>
    /// Which tone each successive act of a discipline takes: <b>Moyen, then Foncé, then Clair</b>, cycling.
    ///
    /// <para>Moyen first so the first act of every discipline keeps the colour that discipline already had — the
    /// change adds distinctions, it does not repaint what a clinic recognises. Indices are into a family's
    /// <c>Tones</c> list, which the palette orders Clair · Moyen · Foncé.</para>
    /// </summary>
    private static readonly int[] ToneOrder = [1, 2, 0];

    private const string FallbackColor = "#6C757D";

    // Default odontogram state a procedure of each category produces (editable per procedure; overridable per act).
    // Coarse defaults — categories mixing states (e.g. Pédodontie, Prothèse fixe with bridges) rely on the
    // admin/per-act override. Categories not listed produce no tooth-state change.
    private static readonly Dictionary<string, ToothCondition?> CategoryResultingConditions = new()
    {
        ["Soins conservateurs"] = ToothCondition.Obturation,
        ["Endodontie"] = ToothCondition.TraitementDeCanal,
        ["Chirurgie/Extraction"] = ToothCondition.ExtraitAbsent,
        ["Prothèse fixe"] = ToothCondition.Couronne,
        ["Implantologie"] = ToothCondition.Implant,
    };

    public static IReadOnlyList<SeedRow> Rows { get; } = new List<SeedRow>
    {
        new("Consultation / examen bucco-dentaire", 30, 40m, "Consultation"),
        new("Contrôle / suivi", 15, 0m, "Consultation"),
        new("Radiographie rétro-alvéolaire", 10, 20m, "Radiologie"),
        new("Radiographie panoramique", 15, 40m, "Radiologie"),
        new("Soin de carie / obturation", 40, 90m, "Soins conservateurs"),
        new("Traitement de canal (dévitalisation)", 60, 150m, "Endodontie"),
        new("Détartrage", 30, 90m, "Parodontologie"),
        new("Traitement parodontal (surfaçage / curetage)", 45, 120m, "Parodontologie",
            DefaultSteps: PeriodontalTherapySteps),
        new("Extraction simple", 30, 60m, "Chirurgie/Extraction"),
        new("Extraction chirurgicale (sagesse / dent incluse)", 60, 200m, "Chirurgie/Extraction",
            DefaultSteps: SurgicalExtractionSteps),
        new("Couronne / bridge (par élément)", 60, 500m, "Prothèse fixe",
            DefaultSteps: FixedProsthesisSteps),
        new("Prothèse amovible (partielle / complète)", 60, 800m, "Prothèse amovible",
            DefaultSteps: RemovableProsthesisSteps),
        new("Réparation / rebasage de prothèse", 30, 120m, "Prothèse amovible",
            DefaultSteps: DentureRelineSteps),
        new("Implant dentaire", 60, 1500m, "Implantologie",
            DefaultSteps: ImplantSteps),
        new("Traitement orthodontique (multi-attaches)", 60, 3500m, "Orthodontie",
            DefaultSteps: OrthodonticSteps),
        new("Séance orthodontique (contrôle / activation)", 30, 80m, "Orthodontie"),
        new("Blanchiment dentaire", 60, 500m, "Esthétique",
            DefaultSteps: WhiteningSteps),
        // « par élément » like « Couronne / bridge », because the fee and the séances are per tooth: without
        // the suffix a six-veneer smile was six lines of a case-shaped protocol, quoting 24 séances.
        new("Facette (par élément)", 60, 700m, "Esthétique",
            DefaultSteps: VeneerSteps),
        new("Soin dentaire enfant (dent de lait)", 30, 60m, "Pédodontie"),

        /*
         * ── Actes distincts, ajoutés après relecture du barème de l'Ordre ───────────────────────────────────
         *
         * ⚠️ Each of these is an act with NO row above, never a grade of one that has. The list was cut 43 → 19
         * on practitioner feedback for splitting hairs (« 1 face » vs « 2-3 faces », mono- vs pluriradiculaire,
         * céramo-métal vs zircone), and that decision stands: nothing here re-opens it. What the cut also took
         * out, as collateral, were procedures a dentist books and bills in their own right — a coiffage is not
         * an obturation, an inlay-core is not a couronne, a scellement de sillons is not a soin.
         *
         * Prices are the CNOMDT barème d'honoraires minimums (27/12/2020) where it covers the act, and marked
         * « estimation » where it does not. They are floors, not recommendations — see the class docstring.
         */
        new("Coiffage pulpaire", 30, 30m, "Soins conservateurs",
            // Charts nothing: the barème's own line is « à l'exclusion de l'obturation définitive ». Left on its
            // discipline's default it produced `Obturation`, tied with « Soin de carie » on the first rung of the
            // carie ladder, and a tie is what makes the odontogram stop pre-filling the plan line at all.
            ToothCondition.Sain),                                                           // barème 30
        new("Retraitement endodontique", 90, 250m, "Endodontie",                        // estimation
            DefaultSteps: EndoRetreatmentSteps),
        new("Inlay-core (reconstitution corono-radiculaire)", 45, 80m, "Prothèse fixe",
            // Charts nothing: the core is placed, the crown that covers it is a separate act.
            ToothCondition.Sain,                                                        // barème 80
            DefaultSteps: InlayCoreSteps),
        new("Couronne provisoire", 30, 60m, "Prothèse fixe",
            // Charts nothing: the odontogram records what the tooth carries lastingly, and a provisoire is by
            // definition replaced. It also tied with the definitive crown on the Couronne and Bridge ladders.
            ToothCondition.Sain),                                                           // barème 60
        new("Extraction de racine (alvéolectomie)", 40, 60m, "Chirurgie/Extraction"),   // barème 60
        new("Incision d'abcès et drainage", 20, 40m, "Chirurgie/Extraction",
            // Charts nothing: the tooth stays. Its discipline's default would have recorded it as extracted.
            ToothCondition.Sain,                                                        // estimation
            DefaultSteps: AbscessDrainageSteps),
        new("Gingivectomie", 45, 50m, "Parodontologie",                                 // barème 50 (partielle)
            DefaultSteps: GingivectomySteps),
        new("Frénectomie", 45, 100m, "Parodontologie",                                  // estimation
            DefaultSteps: FrenectomySteps),
        new("Greffe osseuse / comblement", 60, 700m, "Implantologie",
            // Charts nothing: preparing the bone is not placing an implant.
            ToothCondition.Sain,                                                        // barème 700
            DefaultSteps: BoneGraftSteps),
        new("Scellement de sillons", 30, 80m, "Pédodontie"),                            // barème 80
        new("Application de fluor (par arcade)", 20, 200m, "Pédodontie"),               // barème 200
        new("Couronne pédodontique préformée", 40, 110m, "Pédodontie"),                 // barème 110
        new("Mainteneur d'espace fixe", 40, 160m, "Pédodontie",                         // barème 160
            DefaultSteps: SpaceMaintainerSteps),
        new("Gouttière occlusale (bruxisme)", 45, 400m, "Prothèse amovible",             // barème 400
            DefaultSteps: OcclusalSplintSteps),
        new("Contention post-orthodontique", 30, 300m, "Orthodontie",                   // estimation
            DefaultSteps: RetainerSteps),
        // The prosthetic phase of an implant, which « Implant dentaire » used to swallow as its last three
        // séances — so a 1 500 DT line proposed six visits including a crown nobody had quoted. A genuinely
        // distinct act with no row of its own, not a variant of one that has: « Couronne / bridge (par
        // élément) » is préparation + empreinte on a natural tooth and cannot express a désenfouissement.
        new("Couronne sur implant", 45, 500m, "Implantologie",                          // estimation
            DefaultSteps: ImplantCrownSteps),
    };

    /// <summary>
    /// Build fresh <see cref="ProcedureType"/> entities for a clinic from the starter rows.
    /// <para>
    /// ⚠️ Every argument is <b>named</b>, and that is not tidying. This call used to pass <c>r.Category</c>
    /// positionally into the constructor's <c>description</c> slot — there was no category column to put it in —
    /// so nineteen acts per clinic carried their discipline in a field the act form labels « Description
    /// (optionnel) », and the catalogue picker had to group on it while documenting that it was not allowed to
    /// trust it. Now that both parameters exist and are adjacent nullable strings, positional arguments are one
    /// transposition away from re-creating exactly that bug silently.
    /// </para>
    /// </summary>
    public static IEnumerable<ProcedureType> CreateFor(Guid clinicId)
    {
        // How many acts of this discipline have already been built — the act's index inside its own category, and
        // what picks its tone. Counted here rather than precomputed so `Rows` stays a plain readable list.
        var seenPerCategory = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var r in Rows)
        {
            seenPerCategory.TryGetValue(r.Category, out var indexInCategory);
            seenPerCategory[r.Category] = indexInCategory + 1;

            yield return new ProcedureType(
                id: Guid.NewGuid(),
                clinicId: clinicId,
                name: r.Name,
                defaultDurationMinutes: r.DurationMinutes,
                color: ColorHex.FromString(ColourFor(r.Category, indexInCategory)),
                // A seeded act has no description — the starter row carries a name, a price and a discipline, and
                // inventing prose for it would be putting words in the clinic's mouth.
                description: null,
                defaultCost: r.DefaultCost,
                // The row's own answer wins; only a row that gives none falls back to its discipline's default.
                resultingCondition: r.ResultingCondition
                    ?? (CategoryResultingConditions.TryGetValue(r.Category, out var condition) ? condition : null),
                category: r.Category,
                // Only three rows carry one — see the protocol arrays above for why the vendor seeds so few.
                defaultSteps: r.DefaultSteps);
        }
    }

    /// <summary>
    /// The discipline's hue, at the tone this act's position calls for.
    ///
    /// <para>⚠️ Read out of <see cref="ColorHex.GetPalette"/> rather than written here: that value object is the
    /// sole authority on which colours exist, and a second table of hexes is how a seeded act ends up carrying one
    /// the picker cannot offer back.</para>
    ///
    /// <para>⚠️ A family has three tones, and « Pédodontie » has five acts — so the tones <b>cycle</b>, and the
    /// fourth act of a discipline repeats the first's colour. That is the honest ceiling of a palette built to
    /// keep a discipline readable as one hue: the alternative is borrowing an unrelated family, which buys
    /// distinctness by destroying the thing the colour is for. At most two acts share a colour, where before
    /// every act of a discipline did.</para>
    /// </summary>
    private static string ColourFor(string category, int indexInCategory)
    {
        if (!CategoryFamilies.TryGetValue(category, out var familyKey)) return FallbackColor;

        var family = ColorHex.GetPalette().FirstOrDefault(f => f.Key == familyKey);
        if (family is null || family.Tones.Count == 0) return FallbackColor;

        var tone = ToneOrder[indexInCategory % ToneOrder.Length];
        return family.Tones[tone % family.Tones.Count].Hex;
    }
}
