using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// « Ajouter au devis » — a séance's new act joins the treatment it is being carried out for.
///
/// <para>The reported case is the arithmetic in <see cref="Adding_An_Act_Raises_The_Total_And_Keeps_What_Was_Collected"/>:
/// a 300 DT prothèse with 100 collected, a second act done at the chair, and no way to put its fee anywhere but
/// a note d'honoraires the devis' own balance never mentions.</para>
/// </summary>
public class FichePlanActAdditionsTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid PatientId = Guid.NewGuid();
    private static readonly Guid CarriedProcedure = Guid.NewGuid();
    private static readonly Guid AddedProcedure = Guid.NewGuid();
    private static readonly DateTime DoneOn = new(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The complaint's own plan: « Prothèse provisoire » 300, accepted and numbered, 100 collected.</summary>
    private static TreatmentPlan CollectedPlan()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Prothèses");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Prothèse provisoire", 300m, CarriedProcedure, new[] { 11 }),
        });
        plan.Accept("2026-0001");
        plan.CollectChairside(100m, PaymentMethod.Cash, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            null, Guid.NewGuid());
        return plan;
    }

    private static DentalRecordActInput Act(string name, Guid? procedureTypeId, decimal cost) =>
        new(procedureTypeId, name, cost, null, false, new[] { 11 }, null, null, null);

    /// <summary>The fiche: the devis act being carried out, plus a second act done the same day.</summary>
    private static List<DentalRecordActInput> TwoActFiche() => new()
    {
        Act("Prothèse provisoire", CarriedProcedure, 0m),
        Act("Détartrage", AddedProcedure, 150m),
    };

    private static Mock<ITreatmentPlanRepository> Plans(TreatmentPlan plan)
    {
        var plans = new Mock<ITreatmentPlanRepository>();
        plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        return plans;
    }

    /// <summary>No catalogue protocol anywhere — an added act is one séance by construction.</summary>
    private static Mock<IProcedureTypeRepository> ProcedureTypes()
    {
        var types = new Mock<IProcedureTypeRepository>();
        types.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProcedureType?)null);
        return types;
    }

    /// <param name="representingNote">A note d'honoraires that REPRESENTS the plan, or none.</param>
    private static Mock<IInvoiceRepository> Invoices(Guid? planId = null, InvoiceStatus? representingNote = null)
    {
        var invoices = new Mock<IInvoiceRepository>();
        var links = new List<(Guid, Guid, string?, InvoiceStatus, decimal, decimal)>();
        if (planId is { } id && representingNote is { } status)
        {
            links.Add((id, Guid.NewGuid(), "2026-0042", status, 300m, 300m));
        }
        invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(links);
        return invoices;
    }

    private static Task<FichePlanActAdditions.Applied> ApplyAsync(
        TreatmentPlan plan,
        List<DentalRecordActInput> acts,
        IReadOnlyList<FichePlanActAddition>? requested,
        IReadOnlyList<FicheExtraPlanActs.Extra>? alreadyLinked = null,
        Mock<IInvoiceRepository>? invoices = null) =>
        FichePlanActAdditions.ApplyAsync(
            Plans(plan).Object,
            (invoices ?? Invoices()).Object,
            ProcedureTypes().Object,
            acts,
            plan.Id,
            plan.Items.First().Id,
            requested,
            alreadyLinked ?? Array.Empty<FicheExtraPlanActs.Extra>(),
            ClinicId,
            NullLogger.Instance,
            CancellationToken.None);

    // ── the money ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The reported case, end to end: 300 quoted, 100 collected, a 150 act added at the chair.
    /// <b>Total 450, collected still 100, and the room to collect is 350</b> — the figure the dentist was
    /// refused when they tried to take it (<c>CollectOnTreatmentCommand</c>: <c>delta &gt; plan.Outstanding</c>).
    /// </summary>
    [Fact]
    public async Task Adding_An_Act_Raises_The_Total_And_Keeps_What_Was_Collected()
    {
        var plan = CollectedPlan();

        var applied = await ApplyAsync(plan, TwoActFiche(), new[] { new FichePlanActAddition(1) });

        Assert.Null(applied.Refusal);
        Assert.Equal(450m, plan.TotalPlanned);
        Assert.Equal(100m, plan.AmountPaid);
        Assert.Equal(350m, plan.Outstanding);
    }

    /// <summary>
    /// The échéancier must still sum to the total, or « Solde patient » and « Créances » stop agreeing —
    /// the invariant <c>RespreadSchedule</c> exists to hold.
    /// </summary>
    [Fact]
    public async Task The_Schedule_Still_Sums_To_The_Total()
    {
        var plan = CollectedPlan();

        await ApplyAsync(plan, TwoActFiche(), new[] { new FichePlanActAddition(1) });

        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
    }

    /// <summary>
    /// The devis owns the fee now, so the act is recorded at 0 — otherwise the séance also raises a note
    /// d'honoraires for it and the patient owes the same work on two documents that cannot see each other.
    /// </summary>
    [Fact]
    public async Task The_Added_Act_Is_Priced_Zero_On_The_Fiche()
    {
        var plan = CollectedPlan();

        var applied = await ApplyAsync(plan, TwoActFiche(), new[] { new FichePlanActAddition(1) });

        Assert.Equal(0m, applied.Acts[1].Cost);
        Assert.Equal(150m, plan.Items.Single(i => i.ProcedureTypeId == AddedProcedure).PlannedCost);
    }

    /// <summary>The new devis line is returned as an extra, so the caller links it through the one linker.</summary>
    [Fact]
    public async Task The_New_Line_Comes_Back_As_An_Extra_To_Link()
    {
        var plan = CollectedPlan();

        var applied = await ApplyAsync(plan, TwoActFiche(), new[] { new FichePlanActAddition(1) });

        var added = Assert.Single(applied.Added);
        Assert.Equal(1, added.ActIndex);
        Assert.Equal(AddedProcedure, added.Item.ProcedureTypeId);
        Assert.Null(added.StepId);
    }

    /// <summary>One séance — the act was carried out today, so the catalogue protocol must not be applied.</summary>
    [Fact]
    public async Task The_Added_Act_Has_No_Protocol()
    {
        var plan = CollectedPlan();

        await ApplyAsync(plan, TwoActFiche(), new[] { new FichePlanActAddition(1) });

        Assert.Empty(plan.Items.Single(i => i.ProcedureTypeId == AddedProcedure).Steps);
    }

    // ── idempotency: the one that silently quotes the patient twice ───────────────────────────────────────

    /// <summary>
    /// <b>The re-save.</b> Reopening the fiche to fix a typo sends the same additions again; by then the act
    /// is bound to the devis line the first save created, so <c>FicheExtraPlanActs</c> has claimed its index
    /// and nothing may be added. Without this the patient is quoted the act once per save, in silence.
    /// </summary>
    [Fact]
    public async Task A_Re_Save_Adds_Nothing()
    {
        var plan = CollectedPlan();
        var first = await ApplyAsync(plan, TwoActFiche(), new[] { new FichePlanActAddition(1) });
        var totalAfterFirst = plan.TotalPlanned;

        var second = await ApplyAsync(
            plan, TwoActFiche(), new[] { new FichePlanActAddition(1) }, alreadyLinked: first.Added);

        Assert.Null(second.Refusal);
        Assert.Empty(second.Added);
        Assert.Equal(totalAfterFirst, plan.TotalPlanned);
        Assert.Equal(2, plan.Items.Count);
    }

    /// <summary>
    /// The act the devis ALREADY carries can never be added: its index is claimed by the primary link, so a
    /// client naming it is ignored rather than quoting the prothèse a second time.
    /// </summary>
    [Fact]
    public async Task The_Act_The_Devis_Already_Carries_Is_Never_Added()
    {
        var plan = CollectedPlan();

        var applied = await ApplyAsync(plan, TwoActFiche(), new[] { new FichePlanActAddition(0) });

        Assert.Null(applied.Refusal);
        Assert.Empty(applied.Added);
        Assert.Equal(300m, plan.TotalPlanned);
    }

    /// <summary>The same index twice in one request is one line, not two.</summary>
    [Fact]
    public async Task A_Repeated_Index_Adds_One_Line()
    {
        var plan = CollectedPlan();

        var applied = await ApplyAsync(
            plan, TwoActFiche(), new[] { new FichePlanActAddition(1), new FichePlanActAddition(1) });

        Assert.Single(applied.Added);
        Assert.Equal(450m, plan.TotalPlanned);
    }

    // ── refusals, all of them before the first mutation ───────────────────────────────────────────────────

    /// <summary>
    /// A hand-typed act carries no catalogue identity, so nothing could bind the devis line back to it on the
    /// next save — and an unbound line is re-added every time the fiche is reopened. Refused, not guessed at.
    /// </summary>
    [Fact]
    public async Task A_Hand_Typed_Act_Is_Refused()
    {
        var plan = CollectedPlan();
        var acts = TwoActFiche();
        acts[1] = Act("Acte libre", null, 150m);

        var applied = await ApplyAsync(plan, acts, new[] { new FichePlanActAddition(1) });

        Assert.NotNull(applied.Refusal);
        Assert.Contains("catalogue", applied.Refusal);
        Assert.Equal(300m, plan.TotalPlanned);
        Assert.Empty(applied.Added);
    }

    [Fact]
    public async Task An_Index_Outside_The_Fiche_Is_Refused()
    {
        var plan = CollectedPlan();

        var applied = await ApplyAsync(plan, TwoActFiche(), new[] { new FichePlanActAddition(7) });

        Assert.NotNull(applied.Refusal);
        Assert.Equal(300m, plan.TotalPlanned);
    }

    /// <summary>
    /// A note d'honoraires that represents the devis takes it out of every balance whole
    /// (<c>PlanBillingRules.BilledPlanIds</c>), so an act added to it would be a live debt where nothing looks.
    /// </summary>
    [Theory]
    [InlineData(InvoiceStatus.Issued)]
    [InlineData(InvoiceStatus.Paid)]
    public async Task A_Devis_Billed_On_A_Note_Is_Refused(InvoiceStatus status)
    {
        var plan = CollectedPlan();

        var applied = await ApplyAsync(
            plan, TwoActFiche(), new[] { new FichePlanActAddition(1) },
            invoices: Invoices(plan.Id, status));

        Assert.NotNull(applied.Refusal);
        Assert.Contains("2026-0042", applied.Refusal);
        Assert.Equal(300m, plan.TotalPlanned);
    }

    /// <summary>
    /// A CANCELLED note represents nothing, so the devis still carries its own balance and the act may join it.
    /// The gate is <c>RepresentsItsPlan</c>, never « is there a note ».
    /// </summary>
    [Fact]
    public async Task A_Cancelled_Note_Does_Not_Block_The_Addition()
    {
        var plan = CollectedPlan();

        var applied = await ApplyAsync(
            plan, TwoActFiche(), new[] { new FichePlanActAddition(1) },
            invoices: Invoices(plan.Id, InvoiceStatus.Cancelled));

        Assert.Null(applied.Refusal);
        Assert.Equal(450m, plan.TotalPlanned);
    }

    /// <summary>
    /// <b>The plan must still be live.</b> `AddItems` admits a Completed / Stopped / WrittenOff devis — only
    /// `Cancelled` is refused there — while `MarkItemDone` goes through `EnsureActive` and refuses all three,
    /// so without this refusal the line was added and the échéancier re-spread before the linker threw at the
    /// far end of the save.
    /// </summary>
    [Theory]
    [InlineData(TreatmentPlanStatus.Completed)]
    [InlineData(TreatmentPlanStatus.Stopped)]
    [InlineData(TreatmentPlanStatus.WrittenOff)]
    public async Task A_Treatment_That_Is_No_Longer_Running_Is_Refused(TreatmentPlanStatus status)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Prothèses");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Prothèse provisoire", 300m, CarriedProcedure, new[] { 11 }),
            new TreatmentPlanItemInput(null, "Couronne", 200m, Guid.NewGuid(), new[] { 12 }),
        });
        plan.Accept("2026-0002");

        switch (status)
        {
            case TreatmentPlanStatus.Completed:
                foreach (var item in plan.Items.ToList())
                {
                    plan.MarkItemDone(item.Id, DoneOn, Guid.NewGuid());
                }
                break;
            case TreatmentPlanStatus.Stopped:
                // Delivered work is what makes this a STOP rather than a cancellation.
                plan.MarkItemDone(plan.Items.First().Id, DoneOn, Guid.NewGuid());
                plan.StopTreatment(ClinicClock.ClinicToday());
                break;
            default:
                plan.WriteOff("Irrécouvrable");
                break;
        }

        Assert.Equal(status, plan.Status);
        var totalBefore = plan.TotalPlanned;

        var applied = await ApplyAsync(plan, TwoActFiche(), new[] { new FichePlanActAddition(1) });

        Assert.NotNull(applied.Refusal);
        Assert.Contains("plus en cours", applied.Refusal);
        Assert.Empty(applied.Added);
        Assert.Equal(totalBefore, plan.TotalPlanned);
    }

    /// <summary>
    /// An un-numbered treatment has no échéancier, so the respread would build it one — a « Solde à régler »
    /// on a treatment nobody was quoted for (board row G10). Refused by name until that lands.
    /// </summary>
    [Fact]
    public async Task An_Un_Numbered_Treatment_Is_Refused()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Suivi");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Prothèse provisoire", 300m, CarriedProcedure, new[] { 11 }),
        });

        var applied = await ApplyAsync(plan, TwoActFiche(), new[] { new FichePlanActAddition(1) });

        Assert.NotNull(applied.Refusal);
        Assert.Contains("devis", applied.Refusal);
        Assert.Equal(300m, plan.TotalPlanned);
    }

    // ── the no-ops every existing fiche takes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The ordinary save — no additions requested. It must touch nothing at all: every fiche in the product
    /// takes this path, and `null` and `[]` are the same answer.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task No_Additions_Is_A_Total_No_Op(bool nullRatherThanEmpty)
    {
        var plan = CollectedPlan();
        var acts = TwoActFiche();

        var applied = await ApplyAsync(
            plan, acts, nullRatherThanEmpty ? null : Array.Empty<FichePlanActAddition>());

        Assert.Null(applied.Refusal);
        Assert.Empty(applied.Added);
        Assert.Same(acts, applied.Acts);
        Assert.Equal(300m, plan.TotalPlanned);
        Assert.Single(plan.Items);
    }

    /// <summary>A fiche naming no devis act at all is left alone — there is no treatment to amend.</summary>
    [Fact]
    public async Task A_Fiche_With_No_Devis_Link_Is_Left_Alone()
    {
        var plan = CollectedPlan();
        var acts = TwoActFiche();

        var applied = await FichePlanActAdditions.ApplyAsync(
            Plans(plan).Object, Invoices().Object, ProcedureTypes().Object, acts,
            treatmentPlanId: null, primaryItemId: null,
            new[] { new FichePlanActAddition(1) },
            Array.Empty<FicheExtraPlanActs.Extra>(),
            ClinicId, NullLogger.Instance, CancellationToken.None);

        Assert.Null(applied.Refusal);
        Assert.Empty(applied.Added);
        Assert.Equal(300m, plan.TotalPlanned);
    }

    // ── the derived guard ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Both fiche commands must consume the additions, and a third one must too.</b>
    ///
    /// <para>This repository's dominant defect is a correct rule wired to one call site: `PlanCarriedActPricing`
    /// and `FicheExtraPlanActs` each already have to be called from both fiche commands, and a command that
    /// takes <c>PlanActAdditions</c> on its wire and never applies it would accept the request, answer 200 and
    /// leave the devis untouched — the act then billed on the séance's own note, which is the double document
    /// this whole change removes.</para>
    ///
    /// <para>The candidate set is derived from the commands that declare the property, never a list kept here.</para>
    /// </summary>
    [Fact]
    public void Every_Fiche_Command_Taking_Additions_Applies_Them()
    {
        var commands = Directory
            .EnumerateFiles(
                Path.Combine(SolutionRoot(), "ClinicManagement.Application", "Features", "Patients", "Commands"),
                "*DentalRecordCommand.cs")
            .Select(path => (Name: Path.GetFileName(path), Source: File.ReadAllText(path)))
            .Where(f => Regex.IsMatch(f.Source, @"\bList<FichePlanActAddition>\s+PlanActAdditions\b"))
            .ToList();

        Assert.True(
            commands.Count >= 2,
            $"expected both fiche commands to take PlanActAdditions; found {commands.Count} "
            + "— if the wire moved, this guard is scanning nothing and would pass on an unapplied addition.");

        var missing = commands
            .Where(f => !f.Source.Contains("FichePlanActAdditions.ApplyAsync", StringComparison.Ordinal))
            .Select(f => f.Name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "these fiche commands accept PlanActAdditions and never apply them, so the devis is silently "
            + $"left untouched: {string.Join(", ", missing)}");

        // And the refusal must be read, not only the acts — a swallowed refusal writes the fiche anyway.
        var swallowed = commands
            .Where(f => !f.Source.Contains("additions.Refusal", StringComparison.Ordinal))
            .Select(f => f.Name)
            .ToList();

        Assert.True(
            swallowed.Count == 0,
            $"these fiche commands apply the additions but ignore the refusal: {string.Join(", ", swallowed)}");
    }

    /// <inheritdoc cref="ClinicManagement.UnitTests.Api.StartupBackfillCoverageTests"/>
    private static string SolutionRoot([CallerFilePath] string thisFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClinicManagement.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                $"ClinicManagement.sln not found above '{thisFile}' — this guard would otherwise scan nothing "
                + "and pass, which is indistinguishable from finding no offenders.");
        }

        return directory.FullName;
    }
}
