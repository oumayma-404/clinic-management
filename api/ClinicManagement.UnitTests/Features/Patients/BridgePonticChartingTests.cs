using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// « Which tooth of this bridge is a pilier, and which is a pontique? »
///
/// <para>
/// The defect these pin is silent, clinical and unrepresentable-by-construction: a <c>DentalRecordAct</c>
/// carried ONE <c>ResultingCondition</c> for all of its teeth, so a three-unit bridge entered as one act charted
/// <b>three abutments and no pontic</b> — a bridge that cannot exist — with the odontogramme then drawing a
/// travée across the run to make it look deliberate. Nothing failed anywhere; the fiche saved, the invoice was
/// right, and the chart was wrong.
/// </para>
///
/// <para>
/// ⚠️ The other half of the rule is what these guard hardest: <b>an act with no pontique marked must chart
/// exactly as it always did</b>. That is what makes the whole change safe to ship against a live database —
/// every historical row, and every act that is not a bridge, has an empty list.
/// </para>
///
/// <para>
/// ⚠️ And nothing here infers the shape from position. <see cref="BridgeCharting"/> takes the dentist's own
/// answer, because « the ends are piliers » is false of a pier abutment and of a cantilever alike.
/// </para>
/// </summary>
public class BridgePonticChartingTests
{
    private static DentalRecordActInput Act(
        ToothCondition? condition,
        int[] teeth,
        int[]? pontics = null,
        int[]? implantPiliers = null) =>
        new(
            ProcedureTypeId: null,
            ProcedureName: "Couronne / bridge (par élément)",
            Cost: 900m,
            UnitCost: 300m,
            IsPerTooth: true,
            ToothNumbers: teeth,
            ResultingCondition: condition,
            Surfaces: null,
            Note: null,
            PonticToothNumbers: pontics,
            ImplantPilierToothNumbers: implantPiliers);

    private static Dictionary<int, ToothCondition> Chart(params DentalRecordActInput[] acts) =>
        DentalRecordActParser
            .BuildToothStates(acts, Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 9, 7), Guid.NewGuid())
            .ToDictionary(t => t.ToothNumber, t => t.Condition);

    // ── the fold itself ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_act_with_no_pontic_marked_charts_its_own_condition_unchanged()
    {
        var chart = Chart(Act(ToothCondition.Bridge, [14, 15, 16]));

        Assert.Equal(ToothCondition.Bridge, chart[14]);
        Assert.Equal(ToothCondition.Bridge, chart[15]);
        Assert.Equal(ToothCondition.Bridge, chart[16]);
    }

    [Fact]
    public void Marking_the_middle_tooth_charts_two_piliers_around_one_pontique()
    {
        var chart = Chart(Act(ToothCondition.Bridge, [14, 15, 16], pontics: [15]));

        Assert.Equal(ToothCondition.BridgePilier, chart[14]);
        Assert.Equal(ToothCondition.BridgePontique, chart[15]);
        Assert.Equal(ToothCondition.BridgePilier, chart[16]);
    }

    /// <summary>
    /// A <b>pier</b> (intermediate) abutment: the middle tooth is alive and crowned, and the pontiques are on
    /// either side of it. Any rule that read the span's geometry would chart this exactly backwards.
    /// </summary>
    [Fact]
    public void A_pier_abutment_keeps_the_middle_tooth_a_pilier()
    {
        var chart = Chart(Act(ToothCondition.BridgePilier, [14, 15, 16, 17], pontics: [15, 16]));

        Assert.Equal(ToothCondition.BridgePilier, chart[14]);
        Assert.Equal(ToothCondition.BridgePontique, chart[15]);
        Assert.Equal(ToothCondition.BridgePontique, chart[16]);
        Assert.Equal(ToothCondition.BridgePilier, chart[17]);
    }

    /// <summary>A <b>cantilever</b>: the pontique hangs past the last abutment, at the END of the span.</summary>
    [Fact]
    public void A_cantilever_puts_the_pontique_at_the_end_of_the_span()
    {
        var chart = Chart(Act(ToothCondition.BridgePilier, [13, 14, 15], pontics: [15]));

        Assert.Equal(ToothCondition.BridgePilier, chart[13]);
        Assert.Equal(ToothCondition.BridgePilier, chart[14]);
        Assert.Equal(ToothCondition.BridgePontique, chart[15]);
    }

    [Fact]
    public void A_bridge_crossing_the_midline_is_charted_as_marked_not_as_sorted()
    {
        // 12 · 11 · 21 in the mouth; sorted numerically that is 11 · 12 · 21, so « the middle one » is 12 — the
        // wrong tooth. The dentist marked 11, and 11 is what is charted.
        var chart = Chart(Act(ToothCondition.Bridge, [12, 11, 21], pontics: [11]));

        Assert.Equal(ToothCondition.BridgePilier, chart[12]);
        Assert.Equal(ToothCondition.BridgePontique, chart[11]);
        Assert.Equal(ToothCondition.BridgePilier, chart[21]);
    }

    [Fact]
    public void A_non_bridge_act_ignores_a_pontic_list_entirely()
    {
        // The état was changed from « Bridge » to « Couronne » after the roles were marked — an ordinary edit.
        var chart = Chart(Act(ToothCondition.Couronne, [14, 15, 16], pontics: [15]));

        Assert.Equal(ToothCondition.Couronne, chart[14]);
        Assert.Equal(ToothCondition.Couronne, chart[15]);
        Assert.Equal(ToothCondition.Couronne, chart[16]);
    }

    [Fact]
    public void Two_acts_in_one_seance_are_folded_independently()
    {
        var chart = Chart(
            Act(ToothCondition.Bridge, [14, 15, 16], pontics: [15]),
            Act(ToothCondition.Obturation, [26], pontics: null));

        Assert.Equal(ToothCondition.BridgePilier, chart[14]);
        Assert.Equal(ToothCondition.BridgePontique, chart[15]);
        Assert.Equal(ToothCondition.Obturation, chart[26]);
    }

    // ── what the aggregate stores ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ Normalised, never refused. A client legitimately holds a stale list — the dentist un-taps a tooth, or
    /// changes the act's état — and a 400 for an ordinary edit is a defect, not a guard.
    /// </summary>
    [Fact]
    public void A_pontic_outside_the_acts_own_teeth_is_dropped_rather_than_refused()
    {
        var act = new DentalRecordAct(Guid.NewGuid(), Guid.NewGuid(), Act(ToothCondition.Bridge, [14, 16], [15, 14]));

        Assert.Equal(new[] { 14 }, act.PonticToothNumbers);
    }

    [Fact]
    public void A_pontic_list_on_a_non_bridge_act_is_cleared()
    {
        var act = new DentalRecordAct(Guid.NewGuid(), Guid.NewGuid(), Act(ToothCondition.Obturation, [14, 15], [15]));

        Assert.Empty(act.PonticToothNumbers);
    }

    [Fact]
    public void A_duplicated_pontic_is_stored_once()
    {
        var act = new DentalRecordAct(Guid.NewGuid(), Guid.NewGuid(), Act(ToothCondition.Bridge, [14, 15, 16], [15, 15]));

        Assert.Equal(new[] { 15 }, act.PonticToothNumbers);
    }

    /// <summary>Every act built before this field existed passes `null`, and must store nothing.</summary>
    [Fact]
    public void An_act_constructed_without_the_field_stores_an_empty_list()
    {
        var act = new DentalRecordAct(Guid.NewGuid(), Guid.NewGuid(), Act(ToothCondition.Bridge, [14, 15, 16]));

        Assert.Empty(act.PonticToothNumbers);
    }

    // ── the wire ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_parser_carries_the_pontic_list_through_to_the_aggregate_input()
    {
        var parsed = DentalRecordActParser.Parse(
        [
            new DentalActInput
            {
                ProcedureName = "Couronne / bridge (par élément)",
                Cost = 900m,
                IsPerTooth = true,
                ToothNumbers = [14, 15, 16],
                PonticToothNumbers = [15],
                ResultingCondition = nameof(ToothCondition.Bridge),
            },
        ]);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(new[] { 15 }, parsed.Value!.Single().PonticToothNumbers);
    }

    /// <summary>An FDI number that is not a tooth is a different thing from a stale role, and is refused.</summary>
    [Fact]
    public void The_parser_refuses_a_pontic_that_is_not_a_valid_fdi_tooth()
    {
        var parsed = DentalRecordActParser.Parse(
        [
            new DentalActInput
            {
                ProcedureName = "Couronne / bridge (par élément)",
                Cost = 900m,
                ToothNumbers = [14, 15, 16],
                PonticToothNumbers = [99],
                ResultingCondition = nameof(ToothCondition.Bridge),
            },
        ]);

        Assert.False(parsed.IsSuccess);
        Assert.Contains("99", parsed.Error);
    }

    // ── the vocabulary has one owner ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ <c>Bridge</c> stays in the set although it predates the split: it is what every row charted before
    /// « pilier » / « pontique » existed still carries, and dropping it would make those bridges stop asking the
    /// question — and stop connecting on the chart.
    /// </summary>
    [Fact]
    public void The_legacy_bridge_condition_is_still_a_bridge_unit()
    {
        Assert.True(BridgeCharting.IsUnit(ToothCondition.Bridge));
        Assert.True(BridgeCharting.IsUnit(ToothCondition.BridgePilier));
        Assert.True(BridgeCharting.IsUnit(ToothCondition.BridgePontique));
        Assert.False(BridgeCharting.IsUnit(ToothCondition.Couronne));
        Assert.False(BridgeCharting.IsUnit(null));
    }

    // ── the third role, and the four branches of the fold ───────────────────────────────────

    /// <summary>
    /// A bridge carried by implants: the abutment teeth have a fixture in bone and no root, so charting them as
    /// an ordinary <see cref="ToothCondition.BridgePilier"/> drew a rooted tooth over an implant — and
    /// « does this abutment have a root? » is what a dentist reads off the chart before touching it.
    /// </summary>
    [Fact]
    public void A_marked_implant_pilier_charts_as_one()
    {
        var chart = Chart(Act(ToothCondition.Bridge, [14, 15, 16], pontics: [15], implantPiliers: [14, 16]));

        Assert.Equal(ToothCondition.BridgePilierImplant, chart[14]);
        Assert.Equal(ToothCondition.BridgePontique, chart[15]);
        Assert.Equal(ToothCondition.BridgePilierImplant, chart[16]);
    }

    /// <summary>
    /// Branch 3 in isolation: a role was stated <b>somewhere</b> on the act, so an unmarked tooth is a plain
    /// pilier even though no pontique exists at all. This is the « bridge on implants, nothing missing » case.
    /// </summary>
    [Fact]
    public void An_implant_pilier_alone_promotes_the_unmarked_teeth_to_pilier()
    {
        var chart = Chart(Act(ToothCondition.Bridge, [14, 15, 16], implantPiliers: [14]));

        Assert.Equal(ToothCondition.BridgePilierImplant, chart[14]);
        Assert.Equal(ToothCondition.BridgePilier, chart[15]);
        Assert.Equal(ToothCondition.BridgePilier, chart[16]);
    }

    /// <summary>
    /// Branch 3's other half: the promotion applies only to the unspecific <see cref="ToothCondition.Bridge"/>.
    /// An act already charted as a specific variant keeps its own condition for its unmarked teeth.
    /// </summary>
    [Fact]
    public void A_specific_act_condition_is_not_promoted_for_its_unmarked_teeth()
    {
        var chart = Chart(Act(ToothCondition.BridgePilierImplant, [14, 15, 16], pontics: [15]));

        Assert.Equal(ToothCondition.BridgePilierImplant, chart[14]);
        Assert.Equal(ToothCondition.BridgePontique, chart[15]);
        Assert.Equal(ToothCondition.BridgePilierImplant, chart[16]);
    }

    /// <summary>
    /// ⚠️ <b>Branch 4, and the whole migration's safety rests on it.</b> « Both lists empty » is the test — never
    /// « the pontique list is empty » — so every row written before any of this existed, and every bridge a
    /// dentist does not detail, charts exactly as it always did.
    /// </summary>
    [Fact]
    public void Neither_list_populated_leaves_even_a_specific_condition_untouched()
    {
        var chart = Chart(Act(ToothCondition.BridgePilier, [14, 15, 16]));

        Assert.Equal(ToothCondition.BridgePilier, chart[14]);
        Assert.Equal(ToothCondition.BridgePilier, chart[15]);
        Assert.Equal(ToothCondition.BridgePilier, chart[16]);
    }

    /// <summary>
    /// A tooth in both lists cannot be both, and the aggregate resolves it one way — <b>pontique wins</b> — so
    /// the answer never depends on a fold order nobody reading the record can see.
    /// </summary>
    [Fact]
    public void A_tooth_in_both_lists_is_charted_as_a_pontique()
    {
        var chart = Chart(Act(ToothCondition.Bridge, [14, 15], pontics: [15], implantPiliers: [15]));

        Assert.Equal(ToothCondition.BridgePontique, chart[15]);
    }

    [Fact]
    public void A_non_bridge_act_ignores_an_implant_pilier_list_entirely()
    {
        var chart = Chart(Act(ToothCondition.Couronne, [14, 15], implantPiliers: [14]));

        Assert.Equal(ToothCondition.Couronne, chart[14]);
        Assert.Equal(ToothCondition.Couronne, chart[15]);
    }

    // ── the bridge's identity ───────────────────────────────────────────────────────

    private static List<ToothState> States(params DentalRecordActInput[] acts) =>
        DentalRecordActParser
            .BuildToothStates(acts, Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 9, 7), Guid.NewGuid())
            .ToList();

    /// <summary>
    /// ⚠️ <b>The defect this exists for.</b> Two bridges side by side used to be joined into one bar by the
    /// chart's arch-adjacency scan, and one bridge's « still to place » status then leaked onto the finished
    /// crown beside it. Each act states its own extent instead.
    /// </summary>
    [Fact]
    public void Two_bridge_acts_in_one_fiche_get_two_distinct_groups()
    {
        var states = States(
            Act(ToothCondition.Bridge, [14, 15, 16], pontics: [15]),
            Act(ToothCondition.Bridge, [17, 18]));

        var first = states.Where(t => t.ToothNumber is 14 or 15 or 16).Select(t => t.BridgeGroupId).Distinct().ToList();
        var second = states.Where(t => t.ToothNumber is 17 or 18).Select(t => t.BridgeGroupId).Distinct().ToList();

        Assert.Single(first);
        Assert.Single(second);
        Assert.NotNull(first[0]);
        Assert.NotNull(second[0]);
        Assert.NotEqual(first[0], second[0]);
    }

    /// <summary>
    /// ⚠️ <b>A one-tooth act gets NO group, and that is a regression guard rather than tidiness.</b> Before
    /// <c>PonticToothNumbers</c> existed the only way to record a three-unit bridge was the same procedure
    /// twice, so fiches are still entered as two acts of one tooth each. Give each of those a group and every
    /// one becomes a group of one, both are excluded from <c>bridge-runs.ts</c>' ungrouped fallback, and the
    /// travee simply disappears from a workflow people use.
    /// </summary>
    [Fact]
    public void A_one_tooth_bridge_act_stays_ungrouped()
    {
        var states = States(Act(ToothCondition.Bridge, [14]), Act(ToothCondition.Bridge, [16]));

        Assert.All(states, t => Assert.Null(t.BridgeGroupId));
    }

    [Fact]
    public void A_non_bridge_act_never_carries_a_group()
    {
        var states = States(Act(ToothCondition.Obturation, [14, 15, 16]));

        Assert.All(states, t => Assert.Null(t.BridgeGroupId));
    }

    /// <summary>
    /// The fold is in the aggregate, not only in the parser: a caller handing a group to a non-bridge condition
    /// is normalised rather than refused, because a client legitimately holds a stale value the moment the
    /// dentist changes the act's etat.
    /// </summary>
    [Fact]
    public void A_tooth_state_drops_a_group_it_is_not_entitled_to()
    {
        var couronne = new ToothState(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 16, ToothCondition.Couronne,
            new DateTime(2026, 9, 7), bridgeGroupId: Guid.NewGuid());

        var bridge = new ToothState(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 16, ToothCondition.BridgePilier,
            new DateTime(2026, 9, 7), bridgeGroupId: Guid.NewGuid());

        Assert.Null(couronne.BridgeGroupId);
        Assert.NotNull(bridge.BridgeGroupId);
    }
}
