using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// The catalogue's step protocol reaching a devis act — the half of « multi-séance » that makes it effortless
/// rather than merely possible.
///
/// <para>Without it the feature is complete and useless: the dentist configures « Couronne / bridge →
/// préparation · empreinte · scellement » once in the catalogue, and then retypes those three étapes on every
/// devis, for every crown, forever. It shipped in exactly that state — <c>DefaultSteps</c> had an editor, a
/// column, a DTO field, three seeded protocols and no consumer — and the only witness was opening the page.
/// <see cref="ClinicManagement.UnitTests.Common.StepProtocolCoverageTests"/> is the guard that the wiring
/// stays; these are the tests that the rule itself is right.</para>
///
/// <para>⚠️ The dangerous direction is <b>over</b>-application, not under: the protocol is a default and the
/// devis is the fact, so re-applying it over an act whose steps a dentist edited would silently discard a
/// clinical decision, and applying it to work already recorded would throw and take the whole amendment down.
/// Most of what follows pins the refusals.</para>
/// </summary>
public class TreatmentPlanStepProtocolTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid FicheOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Day1 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private static readonly ProcedureStepTemplate[] BridgeProtocol =
    {
        new("Préparation", 60),
        new("Empreinte", 30),
        new("Scellement", 30),
    };

    /// <summary>
    /// A catalogue act, optionally carrying a protocol, optionally belonging to another practice.
    /// </summary>
    private static ProcedureType Procedure(
        Guid id,
        IEnumerable<ProcedureStepTemplate>? protocol = null,
        Guid? clinicId = null)
        => new(
            id,
            clinicId ?? ClinicId,
            "Couronne / bridge (par élément)",
            60,
            ColorHex.FromString("#4F83CC"),
            defaultSteps: protocol);

    private static Mock<IProcedureTypeRepository> Catalogue(params ProcedureType[] procedures)
    {
        var repository = new Mock<IProcedureTypeRepository>();
        repository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => procedures.FirstOrDefault(p => p.Id == id));
        return repository;
    }

    /// <summary>An accepted plan holding one act per (designation, procedureTypeId) pair given.</summary>
    private static TreatmentPlan AcceptedPlan(params (string Designation, Guid? ProcedureTypeId)[] acts)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis");
        plan.SetItems(acts.Select(a => new TreatmentPlanItemInput(
            null, a.Designation, 1000m, a.ProcedureTypeId, Array.Empty<int>())));
        plan.Accept("2026-0001");
        return plan;
    }

    // ── the point of the whole thing ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_Act_Whose_Procedure_Has_A_Protocol_Arrives_Cut_Into_Its_Steps()
    {
        var procedureTypeId = Guid.NewGuid();
        var plan = AcceptedPlan(("Bridge 4 dents", procedureTypeId));
        var catalogue = Catalogue(Procedure(procedureTypeId, BridgeProtocol));

        await TreatmentPlanStepProtocol.ApplyAsync(plan, ClinicId, catalogue.Object, CancellationToken.None);

        var item = plan.Items.Single();
        Assert.Equal(
            new[] { "Préparation", "Empreinte", "Scellement" },
            item.Steps.Select(s => s.Label).ToArray());
        // Dense 0..n-1, in the protocol's own order — verify-schema's plan-step-sequence-dense asserts exactly
        // that over the real table (« a gap, a duplicate or a non-zero start »), and a protocol applied out of
        // order would be the one writer able to break it.
        Assert.Equal(new[] { 0, 1, 2 }, item.Steps.Select(s => s.SequenceNumber).ToArray());
        Assert.Equal(new int?[] { 60, 30, 30 }, item.Steps.Select(s => s.EstimatedDurationMinutes).ToArray());
        // Nothing is done yet, so the act and the plan are still merely planned.
        Assert.Equal(TreatmentPlanItemStatus.Planned, item.Status);
    }

    [Fact]
    public async Task Every_Act_Of_The_Devis_Gets_Its_Own_Protocol_And_The_Catalogue_Is_Read_Once_Per_Procedure()
    {
        var crown = Guid.NewGuid();
        var plan = AcceptedPlan(
            ("Couronne 16", crown),
            ("Couronne 26", crown),
            ("Couronne 36", crown));
        var catalogue = Catalogue(Procedure(crown, BridgeProtocol));

        await TreatmentPlanStepProtocol.ApplyAsync(plan, ClinicId, catalogue.Object, CancellationToken.None);

        Assert.All(plan.Items, i => Assert.Equal(3, i.Steps.Count));
        // Three acts, one procedure, one lookup — the pricing twin's cache rule, and the reason it is here.
        catalogue.Verify(
            r => r.GetByIdAsync(crown, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── fills only a blank act ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Steps_A_Dentist_Already_Edited_Are_Never_Replaced_By_The_Catalogue()
    {
        var procedureTypeId = Guid.NewGuid();
        var plan = AcceptedPlan(("Bridge 4 dents", procedureTypeId));
        var item = plan.Items.Single();

        // The practice does this bridge in two visits, not three.
        plan.SetItemSteps(item.Id, new[]
        {
            new TreatmentPlanItemStepInput(null, "Préparation + empreinte", 90),
            new TreatmentPlanItemStepInput(null, "Scellement", 30),
        });

        var catalogue = Catalogue(Procedure(procedureTypeId, BridgeProtocol));
        await TreatmentPlanStepProtocol.ApplyAsync(plan, ClinicId, catalogue.Object, CancellationToken.None);

        Assert.Equal(
            new[] { "Préparation + empreinte", "Scellement" },
            plan.Items.Single().Steps.Select(s => s.Label).ToArray());
    }

    /// <summary>
    /// The one that would be an outage rather than a wrong screen: <c>SetSteps</c> refuses to cut a finished
    /// step-less act into steps, so re-applying over recorded work throws — and the amendment that called it
    /// dies with it, taking the acts the dentist was actually adding.
    /// </summary>
    [Fact]
    public async Task An_Act_Already_Carried_Out_Is_Left_Alone_Rather_Than_Made_To_Throw()
    {
        var procedureTypeId = Guid.NewGuid();
        var plan = AcceptedPlan(("Bridge 4 dents", procedureTypeId));
        var item = plan.Items.Single();
        plan.MarkItemDone(item.Id, Day1, FicheOne);
        Assert.Equal(TreatmentPlanItemStatus.Done, plan.Items.Single().Status);

        var catalogue = Catalogue(Procedure(procedureTypeId, BridgeProtocol));

        await TreatmentPlanStepProtocol.ApplyAsync(plan, ClinicId, catalogue.Object, CancellationToken.None);

        var after = plan.Items.Single();
        Assert.Empty(after.Steps);
        Assert.Equal(TreatmentPlanItemStatus.Done, after.Status);
        Assert.Equal(FicheOne, after.LinkedDentalRecordId);
    }

    [Fact]
    public async Task A_Hand_Typed_Line_With_No_Procedure_Is_Not_Touched_And_Costs_No_Lookup()
    {
        var plan = AcceptedPlan(("Réparation prothèse (hors nomenclature)", null));
        var catalogue = Catalogue();

        await TreatmentPlanStepProtocol.ApplyAsync(plan, ClinicId, catalogue.Object, CancellationToken.None);

        Assert.Empty(plan.Items.Single().Steps);
        catalogue.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// The common case, and the reason this must be a silent no-op rather than anything visible: only prosthetic
    /// work is seeded with a protocol, so most acts of most devis reach here and define none. « Soin de carie »
    /// is deliberately un-seeded — the client asked for it not to be.
    /// </summary>
    [Fact]
    public async Task A_Procedure_With_No_Protocol_Leaves_Its_Act_Whole()
    {
        var procedureTypeId = Guid.NewGuid();
        var plan = AcceptedPlan(("Soin de carie", procedureTypeId));
        var catalogue = Catalogue(Procedure(procedureTypeId));

        await TreatmentPlanStepProtocol.ApplyAsync(plan, ClinicId, catalogue.Object, CancellationToken.None);

        var item = plan.Items.Single();
        Assert.Empty(item.Steps);
        Assert.False(item.HasSteps);
        Assert.Equal(TreatmentPlanItemStatus.Planned, item.Status);
    }

    // ── tenancy ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Defence in depth over the query filter, exactly as <c>TreatmentPlanItemPricing</c> makes before reading a
    /// fee: a procedure id belonging to another practice must contribute nothing, not that practice's protocol.
    /// </summary>
    [Fact]
    public async Task Another_Practices_Procedure_Contributes_No_Protocol()
    {
        var procedureTypeId = Guid.NewGuid();
        var plan = AcceptedPlan(("Bridge 4 dents", procedureTypeId));
        var catalogue = Catalogue(Procedure(procedureTypeId, BridgeProtocol, clinicId: OtherClinicId));

        await TreatmentPlanStepProtocol.ApplyAsync(plan, ClinicId, catalogue.Object, CancellationToken.None);

        Assert.Empty(plan.Items.Single().Steps);
    }

    [Fact]
    public async Task A_Procedure_The_Catalogue_No_Longer_Holds_Is_Not_An_Error()
    {
        var plan = AcceptedPlan(("Bridge 4 dents", Guid.NewGuid()));
        var catalogue = Catalogue();

        await TreatmentPlanStepProtocol.ApplyAsync(plan, ClinicId, catalogue.Object, CancellationToken.None);

        Assert.Empty(plan.Items.Single().Steps);
    }

    // ── the amendment case the client described ───────────────────────────────────────────────────────

    /// <summary>
    /// The dentist's own scenario, one amendment later: a bridge is half done and « ajouter une couronne » is
    /// pressed. The new act must arrive with its protocol and the bridge must be untouched — including the fiche
    /// its first step is attached to, which is the whole reason steps carry their own link.
    /// </summary>
    [Fact]
    public async Task An_Act_Added_To_A_Live_Devis_Gets_Its_Protocol_While_Work_In_Progress_Is_Untouched()
    {
        var procedureTypeId = Guid.NewGuid();
        var plan = AcceptedPlan(("Bridge 4 dents", procedureTypeId));
        var bridge = plan.Items.Single();
        plan.SetItemSteps(bridge.Id, BridgeProtocol
            .Select(s => new TreatmentPlanItemStepInput(null, s.Label, s.DurationMinutes)));
        plan.MarkItemStepDone(bridge.Id, bridge.Steps[0].Id, Day1, FicheOne);
        Assert.Equal(TreatmentPlanItemStatus.InProgress, plan.Items.Single(i => i.Id == bridge.Id).Status);

        plan.AddItems(new[]
        {
            new TreatmentPlanItemInput(null, "Couronne 26", 800m, procedureTypeId, Array.Empty<int>()),
        });

        var catalogue = Catalogue(Procedure(procedureTypeId, BridgeProtocol));
        await TreatmentPlanStepProtocol.ApplyAsync(plan, ClinicId, catalogue.Object, CancellationToken.None);

        var bridgeAfter = plan.Items.Single(i => i.Id == bridge.Id);
        Assert.Equal(3, bridgeAfter.Steps.Count);
        Assert.Equal(TreatmentPlanItemStatus.InProgress, bridgeAfter.Status);
        Assert.Equal(FicheOne, bridgeAfter.Steps[0].LinkedDentalRecordId);

        var crown = plan.Items.Single(i => i.DesignationFr == "Couronne 26");
        Assert.Equal(
            new[] { "Préparation", "Empreinte", "Scellement" },
            crown.Steps.Select(s => s.Label).ToArray());
        Assert.Equal(TreatmentPlanItemStatus.Planned, crown.Status);
    }
}
