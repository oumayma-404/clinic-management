namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Persistent clinical state of a single tooth on a patient's odontogram. <c>Sain</c> is the implicit
/// default (a tooth with no recorded state), so a tooth reset to <c>Sain</c> clears its stored row.
///
/// <para>The set mixes two natures on purpose, because a dentist charts them on one diagram: a <b>pathology</b>
/// (<c>Carie</c>, <c>Fracture</c>, …) is a problem to plan work against, while a <b>restoration</b>
/// (<c>Obturation</c>, <c>Couronne</c>, <c>Implant</c>, …) records work already done. Which of them calls for an
/// act is <c>ConditionTreatments.NeedsTreatment</c>, never an assumption about the member's position here.</para>
///
/// <para>⚠️ Stored as an <c>int</c> (<c>HasConversion&lt;int&gt;()</c> on both <c>ToothState.Condition</c> and
/// <c>ProcedureType.ResultingCondition</c>), so members are <b>append-only</b> and a value is never reused:
/// renumbering would silently re-diagnose every tooth already charted.</para>
/// </summary>
public enum ToothCondition
{
    Sain = 0,
    Carie = 1,
    Obturation = 2,
    Couronne = 3,
    TraitementDeCanal = 4,
    Bridge = 5,
    Implant = 6,
    ExtraitAbsent = 7,
    ATraiter = 8,

    /*
     * Added by `odontogram-plan-suggestions`. Each one changes which act gets planned and each already has a
     * treating act in the seeded catalogue — that was the bar for inclusion, not completeness against a survey
     * instrument. They follow WHO *Oral Health Surveys* 5th ed. dentition-status codes where those have an
     * equivalent, noted per member.
     */

    /// <summary>Coronal or root fracture (WHO trauma code « T »). Treated by a restoration, a crown, endodontics
    /// or extraction depending on the line of fracture — hence no single default.</summary>
    Fracture = 9,

    /// <summary>A root left in the arch after the crown is gone. Charted routinely and common here; the act is an
    /// extraction, but whether it is simple or surgical is a judgement about access the app must not make.</summary>
    RacineResiduelle = 10,

    /// <summary>Unerupted or impacted (WHO code 8). ⚠️ Deliberately <b>not</b> treated as work to do: it is very
    /// often a finding to monitor, and counting it under « dents à traiter » is how that figure stops being
    /// believed. It still carries treatments, for when the dentist does plan one.</summary>
    DentIncluse = 11,

    /// <summary>An existing restoration that has failed — secondary caries, a fracture or a marginal defect
    /// (WHO code 2, « filled, with caries »). Distinct from <see cref="Carie"/> because the act is a *reprise*:
    /// the old restoration comes out first.</summary>
    RestaurationDefectueuse = 12,

    /// <summary>Periapical radiolucency or abscess. The endodontic indication; extraction when the tooth cannot
    /// be kept.</summary>
    LesionPeriapicale = 13,

    /// <summary>Periodontal involvement on this tooth — mobility, attachment loss, pocketing. ⚠️ Its acts
    /// (détartrage, surfaçage) are <b>session</b> fees rather than per-tooth ones, which is why a plan line
    /// grouping several teeth must not multiply the tariff by the tooth count.</summary>
    MaladieParodontale = 14,

    /*
     * ── A bridge is not one thing, and saying so is what these two are for ─────────────────────────────────
     *
     * <see cref="Bridge"/> (5) records « this tooth is part of a bridge » and cannot say **which part**. The
     * charting convention has distinguished the two since long before this product — BR for the retainer, BP
     * for the pontic — because they are opposite clinical facts: a retainer is a prepared, living, rooted tooth
     * carrying the prosthesis, and a pontic is an artificial tooth suspended over a gap where no tooth exists.
     *
     * ⚠️ Without the distinction a chart has to **guess**, and every rule for guessing is wrong on a real
     * configuration. « The unit in the middle is the pontic » is false for a **pilier intermédiaire** (pier
     * abutment) — a natural tooth between two edentulous spaces, e.g. terminal 16 and 12 with 14 an abutment
     * and 15 and 13 pontics — and « the pontic sits between two abutments » is false for a **cantilever**,
     * which has abutments at one end only and a pontic attached at one side. Drawing a living abutment as a
     * suspended crown is a false clinical statement on the one diagram a dentist reads at a glance.
     *
     * ⚠️ <see cref="Bridge"/> is **kept and never re-pointed**: values are append-only, rows already carry it,
     * and it stays the honest answer when the practitioner records a bridge without saying which unit is which.
     */

    /// <summary>Bridge retainer (BR) — a prepared natural tooth or implant carrying the prosthesis. It has a
    /// root: it is the tooth the bridge is anchored to.</summary>
    BridgePilier = 15,

    /// <summary>Bridge pontic (BP) — the artificial tooth suspended over an edentulous span. It has no root,
    /// because there is no tooth there.</summary>
    BridgePontique = 16,
}
