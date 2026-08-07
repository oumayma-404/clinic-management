using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// Dental-record act parsing + odontogram-entry building (feature <c>tooth-first-record-entry</c>):
/// AC-6 (a session is no longer restricted to a single dentition — only FDI validity is enforced),
/// AC-2 (several acts on one tooth each produce their own odontogram entry), AC-5 (a mouth-level act charts
/// nothing) and AC-12 (a bad amount is refused on the <c>Result</c> path, never thrown).
/// </summary>
public class DentalRecordActParserTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid RecordId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTime Intervention = new(2026, 7, 26, 9, 0, 0, DateTimeKind.Utc);

    private static DentalActInput Input(
        string name = "Soin de carie / obturation",
        decimal cost = 90m,
        decimal? unitCost = 90m,
        bool isPerTooth = true,
        int[]? teeth = null,
        string? condition = "Obturation",
        string? surfaces = null,
        string? note = null) => new()
        {
            ProcedureName = name,
            Cost = cost,
            UnitCost = unitCost,
            IsPerTooth = isPerTooth,
            ToothNumbers = (teeth ?? new[] { 16 }).ToList(),
            ResultingCondition = condition,
            Surfaces = surfaces,
            Note = note,
        };

    // [AC-6] A permanent tooth and a deciduous one in the same session. The old parser rejected any tooth
    // whose dentition disagreed with the record's IsAdultTeeth flag, so a mixed visit (a child with a
    // permanent 36 and a deciduous 75) could not be recorded at all.
    [Fact]
    public void Parse_Accepts_Mixed_Dentition_Across_Acts()
    {
        var result = DentalRecordActParser.Parse(new[]
        {
            Input(teeth: new[] { 36 }),
            Input(name: "Soin dentaire enfant (dent de lait)", teeth: new[] { 75 }),
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(new[] { 36 }, result.Value[0].ToothNumbers);
        Assert.Equal(new[] { 75 }, result.Value[1].ToothNumbers);
    }

    // [AC-6] …and both dentitions inside a single act.
    [Fact]
    public void Parse_Accepts_Both_Dentitions_Inside_One_Act()
    {
        var result = DentalRecordActParser.Parse(new[] { Input(teeth: new[] { 36, 75 }) });

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 36, 75 }, result.Value!.Single().ToothNumbers);
    }

    // Quadrant-precise FDI validation survives the relaxation: numbers between the quadrants are still refused.
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(49)]
    [InlineData(56)]
    [InlineData(99)]
    public void Parse_Rejects_An_Invalid_Tooth_Number(int tooth)
    {
        var result = DentalRecordActParser.Parse(new[] { Input(teeth: new[] { tooth }) });

        Assert.True(result.IsFailure);
        Assert.Contains(tooth.ToString(), result.Error!);
    }

    [Fact]
    public void Parse_Rejects_A_Blank_Procedure_Name()
    {
        var result = DentalRecordActParser.Parse(new[] { Input(name: "   ") });

        Assert.True(result.IsFailure);
        Assert.Equal("Le nom de l'acte est requis.", result.Error);
    }

    // [AC-12] A bad amount comes back as a failed Result with a French message — not an exception the
    // handler has to catch.
    [Fact]
    public void Parse_Rejects_A_Negative_Cost()
    {
        var result = DentalRecordActParser.Parse(new[] { Input(cost: -1m) });

        Assert.True(result.IsFailure);
        Assert.Equal("Le coût de l'acte ne peut pas être négatif.", result.Error);
    }

    [Fact]
    public void Parse_Rejects_A_Negative_Unit_Cost()
    {
        var result = DentalRecordActParser.Parse(new[] { Input(cost: 10m, unitCost: -1m) });

        Assert.True(result.IsFailure);
        Assert.Equal("Le prix unitaire de l'acte ne peut pas être négatif.", result.Error);
    }

    [Fact]
    public void Parse_Rejects_An_Unknown_Resulting_Condition()
    {
        var result = DentalRecordActParser.Parse(new[] { Input(condition: "Pourrie") });

        Assert.True(result.IsFailure);
        Assert.Equal("État de dent invalide.", result.Error);
    }

    [Fact]
    public void Parse_Maps_A_Known_Resulting_Condition_Case_Insensitively()
    {
        var result = DentalRecordActParser.Parse(new[] { Input(condition: "traitementdecanal") });

        Assert.True(result.IsSuccess);
        Assert.Equal(ToothCondition.TraitementDeCanal, result.Value!.Single().ResultingCondition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Maps_A_Missing_Resulting_Condition_To_Null(string? condition)
    {
        var result = DentalRecordActParser.Parse(new[] { Input(condition: condition) });

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Single().ResultingCondition);
    }

    // [AC-3] The pricing provenance reaches the aggregate untouched — the parser validates, it does not price.
    [Fact]
    public void Parse_Carries_The_Pricing_Provenance()
    {
        var result = DentalRecordActParser.Parse(new[]
        {
            Input(cost: 180m, unitCost: 90m, isPerTooth: true, teeth: new[] { 16, 26 }),
        });

        Assert.True(result.IsSuccess);
        var act = result.Value!.Single();
        Assert.Equal(180m, act.Cost);
        Assert.Equal(90m, act.UnitCost);
        Assert.True(act.IsPerTooth);
    }

    // [AC-2] Two procedures on the same tooth in one session produce two independent odontogram entries —
    // the behaviour the dentist asked for.
    [Fact]
    public void BuildToothStates_Emits_Separate_Entries_For_Two_Acts_On_The_Same_Tooth()
    {
        var parsed = DentalRecordActParser.Parse(new[]
        {
            Input(name: "Traitement de canal (dévitalisation)", teeth: new[] { 16 }, condition: "TraitementDeCanal"),
            Input(name: "Couronne / bridge (par élément)", teeth: new[] { 16 }, condition: "Couronne"),
        });

        var states = DentalRecordActParser
            .BuildToothStates(parsed.Value!, PatientId, ClinicId, Intervention, RecordId)
            .ToList();

        Assert.Equal(2, states.Count);
        Assert.All(states, s => Assert.Equal(16, s.ToothNumber));
        Assert.All(states, s => Assert.Equal(RecordId, s.DentalRecordId));
        Assert.All(states, s => Assert.Equal(Intervention, s.TreatmentDate));
        Assert.All(states, s => Assert.Equal(ToothStateSource.Treatment, s.Source));
        Assert.Contains(states, s => s.Condition == ToothCondition.TraitementDeCanal);
        Assert.Contains(states, s => s.Condition == ToothCondition.Couronne);
    }

    [Fact]
    public void BuildToothStates_Emits_One_Entry_Per_Act_Per_Tooth()
    {
        var parsed = DentalRecordActParser.Parse(new[] { Input(teeth: new[] { 16, 26 }) });

        var states = DentalRecordActParser
            .BuildToothStates(parsed.Value!, PatientId, ClinicId, Intervention, RecordId)
            .ToList();

        Assert.Equal(new[] { 16, 26 }, states.Select(s => s.ToothNumber));
        Assert.All(states, s => Assert.Equal(PatientId, s.PatientId));
    }

    [Fact]
    public void BuildToothStates_Carries_Surfaces_And_Note_Onto_Every_Tooth()
    {
        var parsed = DentalRecordActParser.Parse(new[]
        {
            Input(teeth: new[] { 16, 26 }, surfaces: "mo", note: "  composite  "),
        });

        var states = DentalRecordActParser
            .BuildToothStates(parsed.Value!, PatientId, ClinicId, Intervention, RecordId)
            .ToList();

        Assert.All(states, s => Assert.Equal("MO", s.Surfaces));
        Assert.All(states, s => Assert.Equal("composite", s.Note));
    }

    // An act that changes nothing (consultation, détartrage) must not add odontogram noise.
    [Theory]
    [InlineData(null)]
    [InlineData("Sain")]
    public void BuildToothStates_Skips_Acts_Without_A_Real_Condition(string? condition)
    {
        var parsed = DentalRecordActParser.Parse(new[]
        {
            Input(name: "Détartrage", teeth: new[] { 16 }, condition: condition),
        });

        Assert.Empty(DentalRecordActParser.BuildToothStates(parsed.Value!, PatientId, ClinicId, Intervention, RecordId));
    }

    // [AC-5] A mouth-level act charts nothing even when it carries a resulting condition — there is no tooth.
    [Fact]
    public void BuildToothStates_Emits_Nothing_For_An_Act_With_No_Teeth()
    {
        var parsed = DentalRecordActParser.Parse(new[]
        {
            Input(name: "Détartrage", teeth: Array.Empty<int>(), condition: "Obturation"),
        });

        Assert.Empty(DentalRecordActParser.BuildToothStates(parsed.Value!, PatientId, ClinicId, Intervention, RecordId));
    }
}
