using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain.Entities;

/// <summary>
/// Per-tooth pricing provenance on a dental act, and the record totals derived from it (feature
/// <c>tooth-first-record-entry</c>): AC-3 (a procedure applied to several teeth), AC-4/AC-5 (a mouth-level
/// act is never per-tooth), AC-8 (<c>Cost</c> is stored as sent and never recomputed, so an edited record
/// round-trips) and AC-15 (the record total equals the sum of its acts to the millime). Pure domain, no mocks.
/// </summary>
public class DentalRecordActPricingTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RecordId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime Intervention = new(2026, 7, 26, 9, 0, 0, DateTimeKind.Utc);

    private static DentalRecordActInput Act(
        string name = "Soin de carie / obturation",
        decimal cost = 180m,
        decimal? unitCost = 90m,
        bool isPerTooth = true,
        int[]? teeth = null,
        ToothCondition? condition = ToothCondition.Obturation,
        string? surfaces = null,
        string? note = null) =>
        new(null, name, cost, unitCost, isPerTooth, teeth ?? new[] { 16, 26 }, condition, surfaces, note);

    private static DentalRecord NewRecord() => new(RecordId, PatientId, ClinicId, Intervention, 0m, true);

    private static DentalRecordAct Build(DentalRecordActInput input) => new(Guid.NewGuid(), RecordId, input);

    // [AC-3] One procedure across several teeth keeps its total AND how that total was reached, so the editor
    // can reopen it as "unit × teeth" and the invoice can bill it as a quantity.
    [Fact]
    public void Act_Stores_The_Total_And_Its_PerTooth_Provenance()
    {
        var act = Build(Act(cost: 180m, unitCost: 90m, isPerTooth: true, teeth: new[] { 16, 26 }));

        Assert.Equal(180m, act.Cost);
        Assert.Equal(90m, act.UnitCost);
        Assert.True(act.IsPerTooth);
        Assert.Equal(new[] { 16, 26 }, act.ToothNumbers);
    }

    // [AC-8] Cost is authoritative: the domain never recomputes it from UnitCost × teeth, so a fee the
    // dentist adjusted by hand survives a save/reload/save cycle unchanged.
    [Fact]
    public void Act_Cost_Is_Not_Recomputed_From_Unit_Times_Teeth()
    {
        var act = Build(Act(cost: 500m, unitCost: 90m, isPerTooth: true, teeth: new[] { 16, 26 }));

        Assert.Equal(500m, act.Cost);
        Assert.Equal(90m, act.UnitCost);
    }

    // [AC-8] A legacy act (no captured unit price, flat fee) is preserved as exactly that — nothing infers
    // per-tooth pricing from the stored total.
    [Fact]
    public void Act_Without_A_Unit_Price_Stays_A_Flat_Fee()
    {
        var act = Build(Act(cost: 220m, unitCost: null, isPerTooth: false, teeth: new[] { 16, 26 }));

        Assert.Equal(220m, act.Cost);
        Assert.Null(act.UnitCost);
        Assert.False(act.IsPerTooth);
    }

    // [AC-4 / AC-5] A mouth-level act (détartrage, panoramique) has nothing to multiply, so per-tooth pricing
    // is refused even when the caller asks for it.
    [Fact]
    public void Act_With_No_Teeth_Is_Never_PerTooth()
    {
        var act = Build(Act(name: "Détartrage", cost: 60m, unitCost: 60m, isPerTooth: true,
            teeth: Array.Empty<int>(), condition: null));

        Assert.False(act.IsPerTooth);
        Assert.Empty(act.ToothNumbers);
        Assert.Equal(60m, act.Cost);
    }

    // Both money values round through InvoiceCalculator (millime, away from zero) — the single rounding authority.
    [Fact]
    public void Act_Rounds_Cost_And_UnitCost_To_The_Millime()
    {
        var act = Build(Act(cost: 180.0005m, unitCost: 90.0004m));

        Assert.Equal(180.001m, act.Cost);
        Assert.Equal(90m, act.UnitCost);
    }

    // [AC-15] The derived record total equals the sum of its acts to the millime — the column is
    // decimal(18,3), so a third decimal must survive rather than being rounded away.
    [Fact]
    public void Record_Cost_Equals_Sum_Of_Act_Costs_With_Millimes()
    {
        var record = NewRecord();

        record.SetActs(new[]
        {
            Act(cost: 330.003m, unitCost: 110.001m, isPerTooth: true, teeth: new[] { 16, 26, 36 }),
            Act(name: "Détartrage", cost: 60.500m, unitCost: 60.500m, isPerTooth: false,
                teeth: Array.Empty<int>(), condition: null),
        });

        Assert.Equal(390.503m, record.Cost);
        Assert.Equal(record.Acts.Sum(a => a.Cost), record.Cost);
    }

    // [AC-2] Several procedures on one tooth in a single session: both acts are kept, and the derived flat
    // tooth list still lists that tooth exactly once.
    [Fact]
    public void SetActs_Keeps_Multiple_Acts_On_One_Tooth_And_Dedupes_The_Derived_Teeth()
    {
        var record = NewRecord();

        record.SetActs(new[]
        {
            Act(name: "Traitement de canal (dévitalisation)", cost: 150m, unitCost: 150m, teeth: new[] { 16 },
                condition: ToothCondition.TraitementDeCanal),
            Act(name: "Couronne / bridge (par élément)", cost: 500m, unitCost: 500m, teeth: new[] { 16 },
                condition: ToothCondition.Couronne),
        });

        Assert.Equal(2, record.Acts.Count);
        Assert.Single(record.Teeth);
        Assert.Equal(16, record.Teeth.First().ToothNumber);
        Assert.Equal(650m, record.Cost);
    }

    // [AC-6] A record may hold both dentitions — the derived tooth list carries them side by side.
    [Fact]
    public void SetActs_Accepts_A_Mixed_Dentition_Session()
    {
        var record = NewRecord();

        record.SetActs(new[]
        {
            Act(cost: 90m, unitCost: 90m, teeth: new[] { 36 }),
            Act(name: "Soin dentaire enfant (dent de lait)", cost: 60m, unitCost: 60m, teeth: new[] { 75 }),
        });

        Assert.Equal(new[] { 36, 75 }, record.Teeth.Select(t => t.ToothNumber).OrderBy(t => t));
    }

    // Replacing the acts fully rebuilds the derived state — no leftovers from the previous call.
    [Fact]
    public void SetActs_Replaces_Previous_Acts_And_Teeth()
    {
        var record = NewRecord();
        record.SetActs(new[] { Act(cost: 180m, teeth: new[] { 16, 26 }) });

        record.SetActs(new[] { Act(name: "Extraction simple", cost: 60m, unitCost: 60m, teeth: new[] { 48 },
            condition: ToothCondition.ExtraitAbsent) });

        Assert.Single(record.Acts);
        Assert.Single(record.Teeth);
        Assert.Equal(48, record.Teeth.First().ToothNumber);
        Assert.Equal(60m, record.Cost);
    }

    // The stored summary is the distinct act names, in order — it feeds the record list and the AI summary.
    [Fact]
    public void SetActs_Derives_A_Distinct_Procedure_Summary()
    {
        var record = NewRecord();

        record.SetActs(new[]
        {
            Act(name: "Extraction simple", cost: 60m, unitCost: 60m, teeth: new[] { 18 },
                condition: ToothCondition.ExtraitAbsent),
            Act(name: "Extraction simple", cost: 60m, unitCost: 60m, teeth: new[] { 28 },
                condition: ToothCondition.ExtraitAbsent),
            Act(name: "Détartrage", cost: 60m, unitCost: 60m, teeth: Array.Empty<int>(), condition: null),
        });

        Assert.Equal("Extraction simple, Détartrage", record.ProcedureType);
    }

    // "Sain" is the implicit default, not a recordable outcome — it collapses to no resulting condition.
    [Fact]
    public void Act_Treats_Sain_As_No_Resulting_Condition()
    {
        var act = Build(Act(condition: ToothCondition.Sain));

        Assert.Null(act.ResultingCondition);
    }

    [Fact]
    public void Act_Rejects_A_Negative_Cost() =>
        Assert.Throws<ArgumentException>(() => Build(Act(cost: -1m)));

    [Fact]
    public void Act_Rejects_A_Negative_Unit_Cost() =>
        Assert.Throws<ArgumentException>(() => Build(Act(cost: 10m, unitCost: -1m)));

    [Fact]
    public void Act_Rejects_A_Blank_Procedure_Name() =>
        Assert.Throws<ArgumentException>(() => Build(Act(name: "   ")));

    [Fact]
    public void Act_Rejects_An_Invalid_Tooth_Number() =>
        Assert.Throws<ArgumentException>(() => Build(Act(teeth: new[] { 19 })));
}
