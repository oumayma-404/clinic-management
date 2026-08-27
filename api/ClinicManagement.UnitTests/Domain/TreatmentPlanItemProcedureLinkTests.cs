using ClinicManagement.Application.Features.TreatmentPlans;
using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Pins the plan act → <c>ProcedureType</c> link. It exists so that booking an act from a devis can preselect
/// the procedure, which is what gives the appointment its colour and default duration and lets the
/// dental-record modal propose the act when the visit is recorded.
/// <para>
/// Before this link, the plan editor snapshotted a « Mes actes » pick as a free-text line and discarded the
/// procedure's id, so a plan-scheduled appointment carried no <c>ProcedureTypeId</c> at all. These tests pin
/// that the id now survives every path a plan act can be written by — including the two that historically lost
/// data: a draft edit (which rebuilds every line) and a post-acceptance amendment.
/// </para>
/// </summary>
public class TreatmentPlanItemProcedureLinkTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CouronneProcedure = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ImplantProcedure = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DentalActCode = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static TreatmentPlan Plan() => new(Guid.NewGuid(), ClinicId, PatientId, "Plan");

    private static TreatmentPlanItemInput Line(
        string designation,
        decimal cost,
        Guid? procedureTypeId = null,
        Guid? id = null,
        Guid? dentalActCodeId = null,
        string? codeActe = null,
        params int[] teeth)
        => new(id, designation, cost, dentalActCodeId, codeActe, procedureTypeId, teeth);

    [Fact]
    public void SetItems_Persists_The_Procedure_Link()
    {
        var plan = Plan();

        plan.SetItems(new[] { Line("Couronne zircone", 750m, CouronneProcedure, teeth: 16) });

        Assert.Equal(CouronneProcedure, plan.Items.Single().ProcedureTypeId);
    }

    // A CNAM-only or hand-typed line names no procedure. Null is the correct, meaningful value — it is what
    // tells the booking screen to fall back rather than preselect something arbitrary.
    [Fact]
    public void SetItems_Leaves_The_Link_Null_When_No_Procedure_Was_Chosen()
    {
        var plan = Plan();

        plan.SetItems(new[] { Line("Cavité simple", 90m, dentalActCodeId: DentalActCode, codeActe: "DCH010010", teeth: 26) });

        var item = plan.Items.Single();
        Assert.Null(item.ProcedureTypeId);
        Assert.Equal(DentalActCode, item.DentalActCodeId);
    }

    // The procedure link and the CNAM code are independent axes: a service you schedule and sell vs. the
    // regulatory code for one clinical situation. An act may legitimately carry both.
    [Fact]
    public void An_Act_Can_Carry_Both_A_Procedure_And_A_Dental_Act_Code()
    {
        var plan = Plan();

        plan.SetItems(new[]
        {
            Line("Couronne zircone", 750m, CouronneProcedure, dentalActCodeId: DentalActCode, codeActe: "DCH070010", teeth: 16),
        });

        var item = plan.Items.Single();
        Assert.Equal(CouronneProcedure, item.ProcedureTypeId);
        Assert.Equal(DentalActCode, item.DentalActCodeId);
        Assert.Equal("DCH070010", item.CodeActe);
    }

    /// <summary>
    /// Editing a draft rebuilds every line, and an echoed-back id keeps that line's identity. The procedure
    /// link has to survive that rebuild — otherwise the very first edit of a devis would silently strip it and
    /// every act would quietly go back to booking without a procedure.
    /// </summary>
    [Fact]
    public void SetItems_Keeps_The_Link_Through_An_Id_Preserving_Edit()
    {
        var plan = Plan();
        plan.SetItems(new[] { Line("Couronne zircone", 750m, CouronneProcedure, teeth: 16) });
        var originalId = plan.Items.Single().Id;

        // The client echoes the act back with a changed fee, as the plan form does on save.
        plan.SetItems(new[] { Line("Couronne zircone", 800m, CouronneProcedure, id: originalId, teeth: 16) });

        var item = plan.Items.Single();
        Assert.Equal(originalId, item.Id);
        Assert.Equal(CouronneProcedure, item.ProcedureTypeId);
        Assert.Equal(800m, item.PlannedCost);
    }

    // Re-picking the act as a different procedure must actually change the link, not keep the first one.
    [Fact]
    public void SetItems_Replaces_The_Link_When_The_Procedure_Changes()
    {
        var plan = Plan();
        plan.SetItems(new[] { Line("Couronne zircone", 750m, CouronneProcedure, teeth: 16) });
        var originalId = plan.Items.Single().Id;

        plan.SetItems(new[] { Line("Implant dentaire", 1500m, ImplantProcedure, id: originalId, teeth: 16) });

        Assert.Equal(ImplantProcedure, plan.Items.Single().ProcedureTypeId);
    }

    /// <summary>
    /// An act appended to an accepted plan must be bookable with its procedure preselected just like one from
    /// the original devis — the amendment path builds its items separately, so it can drift.
    /// </summary>
    [Fact]
    public void AddItems_Persists_The_Procedure_Link_On_An_Amended_Plan()
    {
        var plan = Plan();
        plan.SetItems(new[] { Line("Couronne zircone", 750m, CouronneProcedure, teeth: 16) });
        plan.Accept("2026-0001");

        plan.AddItems(new[] { Line("Implant dentaire", 1500m, ImplantProcedure, teeth: 21) });

        var added = plan.Items.Single(i => i.DesignationFr == "Implant dentaire");
        Assert.Equal(ImplantProcedure, added.ProcedureTypeId);
    }

    // AddItems always creates a new act, so an echoed id must not be honoured — otherwise an amendment could
    // collide with an existing act's identity.
    [Fact]
    public void AddItems_Ignores_A_Supplied_Id()
    {
        var plan = Plan();
        plan.SetItems(new[] { Line("Couronne zircone", 750m, CouronneProcedure, teeth: 16) });
        plan.Accept("2026-0001");
        var existingId = plan.Items.Single().Id;

        plan.AddItems(new[] { Line("Implant dentaire", 1500m, ImplantProcedure, id: existingId, teeth: 21) });

        Assert.Equal(2, plan.Items.Count);
        Assert.Single(plan.Items, i => i.Id == existingId);
        Assert.NotEqual(existingId, plan.Items.Single(i => i.DesignationFr == "Implant dentaire").Id);
    }

    // The tuple overloads are adapters kept for callers that predate TreatmentPlanItemInput. They must set no
    // procedure link — a caller that cannot express one has not chosen one.
    [Fact]
    public void The_Tuple_Overload_Sets_No_Procedure_Link()
    {
        var plan = Plan();

        plan.SetItems(new[] { ("Couronne", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }) });

        Assert.Null(plan.Items.Single().ProcedureTypeId);
    }

    /// <summary>
    /// The link is only useful if it reaches the client — the booking screen reads it off the DTO. A stored id
    /// that never gets mapped would look exactly like the bug this feature fixes.
    /// </summary>
    [Fact]
    public void The_Dto_Exposes_The_Procedure_Link()
    {
        var plan = Plan();
        plan.SetItems(new[]
        {
            Line("Couronne zircone", 750m, CouronneProcedure, teeth: 16),
            Line("Cavité simple", 90m, dentalActCodeId: DentalActCode, codeActe: "DCH010010", teeth: 26),
        });

        var dto = plan.ToDto("Patient Test");

        Assert.Equal(CouronneProcedure, dto.Items.Single(i => i.DesignationFr == "Couronne zircone").ProcedureTypeId);
        Assert.Null(dto.Items.Single(i => i.DesignationFr == "Cavité simple").ProcedureTypeId);
    }
}
