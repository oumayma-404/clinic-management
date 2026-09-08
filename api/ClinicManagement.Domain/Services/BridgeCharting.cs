using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// <b>Which tooth of a bridge is a pilier and which is a pontique</b> — the one place that question is answered.
///
/// <para>A fixed bridge is not one state spread over its teeth: the <b>piliers</b> (abutments) are crowned teeth
/// that keep their own roots, and the <b>pontiques</b> are suspended replacements with no root under them at all.
/// The odontogram already draws them differently and joins the run with a travée, because that is what a bridge
/// looks like on a chart.</para>
///
/// <para>⚠️ <b>It cannot be inferred from position, and this type exists because the product tried.</b> « The ends
/// are piliers and the middles are pontiques » is false for the two arrangements a prosthodontist reaches for
/// most often after the simple three-unit case: a <b>pier</b> (intermediate) abutment is a crowned tooth in the
/// MIDDLE of the span, and a <b>cantilever</b> hangs a pontique off one end past the last abutment. Reading the
/// span's geometry would chart both of those backwards — silently, on a record that outlives the visit.</para>
///
/// <para>⚠️ <b>Which is why an act says so, and never this type.</b> <c>DentalRecordAct.PonticToothNumbers</c>
/// carries the dentist's own answer; everything here is vocabulary and a fold, with no geometry in it.</para>
/// </summary>
public static class BridgeCharting
{
    /// <summary>
    /// The conditions for which « which of these teeth is a pontique? » is a question worth asking.
    ///
    /// <para><see cref="ToothCondition.Bridge"/> is in the set although it predates the split: it is what existing
    /// rows hold and what a clinic that has not met the new vocabulary still picks, and marking a pontique on such
    /// an act is exactly how it becomes precise.</para>
    /// </summary>
    public static readonly IReadOnlySet<ToothCondition> Units = new HashSet<ToothCondition>
    {
        ToothCondition.Bridge,
        ToothCondition.BridgePilier,
        ToothCondition.BridgePontique,
        ToothCondition.BridgePilierImplant,
    };

    /// <summary>True when <paramref name="condition"/> is one element of a bridge.</summary>
    public static bool IsUnit(ToothCondition? condition) => condition is not null && Units.Contains(condition.Value);

    /// <summary>
    /// The state one tooth of an act ends in. <b>Four branches, and the fourth is the one that keeps history
    /// intact:</b>
    ///
    /// <list type="number">
    /// <item>the tooth is a marked pontique ⇒ <see cref="ToothCondition.BridgePontique"/>;</item>
    /// <item>the tooth is a marked implant pilier ⇒ <see cref="ToothCondition.BridgePilierImplant"/>;</item>
    /// <item>some role was stated on this act ⇒ the act's own condition, with the unspecific
    ///   <see cref="ToothCondition.Bridge"/> <b>promoted</b> to <see cref="ToothCondition.BridgePilier"/>
    ///   (stating one role is what makes the rest of the span precise);</item>
    /// <item><b>no role stated at all ⇒ the act's condition, untouched.</b></item>
    /// </list>
    ///
    /// <para>⚠️ <b>Branch 4 is load-bearing.</b> An act with neither list populated charts exactly as it did
    /// before any of this existed — that is what keeps every historical record, and every bridge a dentist does
    /// not bother to detail, drawing the way it always did. The split is opt-in per act, and marking one tooth
    /// is the opt-in. « Both lists empty » is the test, never « the pontique list is empty ».</para>
    ///
    /// <para>⚠️ <b><paramref name="implantPilierTeeth"/> has no default value, deliberately.</b>
    /// <c>TreatmentPlanItemStepInput</c>'s fourth parameter defaults to null, so a three-argument copy compiled,
    /// read correctly, and silently erased an osseointegration wait from every step of an act. A required
    /// parameter makes every call site answer the question at compile time instead.</para>
    /// </summary>
    /// <param name="actCondition">The act's own resulting condition.</param>
    /// <param name="ponticTeeth">The teeth of this act the dentist marked as pontiques; may be empty.</param>
    /// <param name="implantPilierTeeth">The teeth of this act the dentist marked as implant-borne piliers; may be empty.</param>
    /// <param name="tooth">The tooth being charted.</param>
    public static ToothCondition ConditionFor(
        ToothCondition actCondition,
        IReadOnlyCollection<int> ponticTeeth,
        IReadOnlyCollection<int> implantPilierTeeth,
        int tooth)
    {
        if (!IsUnit(actCondition)) return actCondition;
        // Branch 4: nothing was stated, so nothing is inferred.
        if (ponticTeeth.Count == 0 && implantPilierTeeth.Count == 0) return actCondition;

        if (ponticTeeth.Contains(tooth)) return ToothCondition.BridgePontique;
        if (implantPilierTeeth.Contains(tooth)) return ToothCondition.BridgePilierImplant;

        // Branch 3: a role was stated somewhere on this act, so an unmarked tooth is a plain pilier — but only
        // the unspecific `Bridge` is promoted. An act already charted as a specific variant keeps its own.
        return actCondition == ToothCondition.Bridge ? ToothCondition.BridgePilier : actCondition;
    }
}
