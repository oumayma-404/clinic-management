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
    };

    /// <summary>True when <paramref name="condition"/> is one element of a bridge.</summary>
    public static bool IsUnit(ToothCondition? condition) => condition is not null && Units.Contains(condition.Value);

    /// <summary>
    /// The state one tooth of an act ends in.
    ///
    /// <para>⚠️ <b>An act with no pontique marked is left exactly as it was</b>, whatever its condition — that is
    /// what keeps every record written before this existed, and every bridge a dentist does not bother to detail,
    /// charting the way it always did. The split is opt-in per act, and marking one tooth is the opt-in.</para>
    /// </summary>
    /// <param name="actCondition">The act's own resulting condition.</param>
    /// <param name="ponticTeeth">The teeth of this act the dentist marked as pontiques; may be empty.</param>
    /// <param name="tooth">The tooth being charted.</param>
    public static ToothCondition ConditionFor(
        ToothCondition actCondition,
        IReadOnlyCollection<int> ponticTeeth,
        int tooth)
    {
        if (ponticTeeth.Count == 0 || !IsUnit(actCondition)) return actCondition;
        return ponticTeeth.Contains(tooth) ? ToothCondition.BridgePontique : ToothCondition.BridgePilier;
    }
}
