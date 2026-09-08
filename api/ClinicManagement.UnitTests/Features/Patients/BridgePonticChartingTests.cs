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
        int[]? pontics = null) =>
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
            PonticToothNumbers: pontics);

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
}
