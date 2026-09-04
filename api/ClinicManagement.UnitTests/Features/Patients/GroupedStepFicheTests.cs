using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// <b>One fiche closes every step the séance carried.</b>
///
/// <para>« Préparation + empreinte dans la même séance, puis le scellement » is the client's own description of
/// what he needed, and it is the reason <c>Appointment.SetProcedures</c>' duplicate guards were relaxed to key
/// on (act, step). Booking it works. <b>Recording it did not.</b> A fiche carries one
/// <c>TreatmentPlanItemStepId</c>, so saving it advanced the préparation and left the empreinte « à planifier »
/// against an appointment already in the past — at which point the act row offers « Enregistrer la fiche » for
/// a séance whose fiche exists, a second fiche opens for one visit, its link resolves back to the already-done
/// préparation, and <c>MarkDone</c> refuses it as belonging to another record. A dead end, reached by doing
/// exactly what the feature was built for, and invisible to every layer: no error, plausible screens, a bridge
/// that cannot be finished.</para>
///
/// <para>⚠️ The steps are read from the <b>appointment's own procedure rows</b>, not from the request. That is
/// what makes this true for any client — and it is the reason these tests drive <c>DentalRecordLinker</c>
/// directly rather than a handler: the rule is « which steps did this séance carry out? », and the answer lives
/// in one place for both the create and the update path to share.</para>
/// </summary>
public class GroupedStepFicheTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid FicheOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FicheTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Day1 = new(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = new(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>An accepted devis whose one act is cut into préparation · empreinte · scellement.</summary>
    private static (TreatmentPlan Plan, TreatmentPlanItem Item) BridgeWithThreeSteps()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Bridge");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Bridge 4 dents", 1000m, Guid.NewGuid(), Array.Empty<int>()),
        });
        plan.Accept("2026-0004");

        var item = plan.Items.Single();
        plan.SetItemSteps(item.Id, new[]
        {
            new TreatmentPlanItemStepInput(null, "Préparation", 60),
            new TreatmentPlanItemStepInput(null, "Empreinte", 30),
            new TreatmentPlanItemStepInput(null, "Scellement", 30),
        });

        return (plan, plan.Items.Single());
    }

    /// <summary>A séance booked for the named steps of that act, as `SetProcedures` really stores it.</summary>
    private static Appointment SeanceFor(TreatmentPlanItem item, params Guid[] stepIds)
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, null, Day1, TimeSpan.FromMinutes(90));

        appointment.SetProcedures(stepIds.Select(stepId => new AppointmentProcedureInput(
            ProcedureTypeId: item.ProcedureTypeId,
            ProcedureName: "Bridge 4 dents",
            DurationMinutes: 30,
            ColorHex: null,
            AgreedCost: null,
            TreatmentPlanItemId: item.Id,
            TreatmentPlanItemStepId: stepId)));

        return appointment;
    }

    private static Mock<ITreatmentPlanRepository> PlansHolding(TreatmentPlan plan)
    {
        var plans = new Mock<ITreatmentPlanRepository>();
        plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        return plans;
    }

    private static Mock<IAppointmentRepository> AppointmentsHolding(params Appointment[] appointments)
    {
        var repository = new Mock<IAppointmentRepository>();
        foreach (var appointment in appointments)
        {
            repository
                .Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(appointment);
        }
        return repository;
    }

    // ── the case that was unrecordable ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Seance_Carrying_Two_Steps_Closes_Both_On_One_Fiche()
    {
        var (plan, item) = BridgeWithThreeSteps();
        var preparation = item.Steps[0];
        var empreinte = item.Steps[1];
        var seance = SeanceFor(item, preparation.Id, empreinte.Id);

        var result = await DentalRecordLinker.LinkPlanItemAsync(
            PlansHolding(plan).Object,
            AppointmentsHolding(seance).Object,
            plan.Id, item.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None,
            // What the browser sends: the FIRST step of the séance. Everything else is derived.
            treatmentPlanItemStepId: preparation.Id,
            appointmentId: seance.Id);

        Assert.True(result.IsSuccess);

        var after = plan.Items.Single();
        Assert.True(after.Steps[0].IsDone);
        Assert.True(after.Steps[1].IsDone);
        Assert.False(after.Steps[2].IsDone);
        // Both point at the fiche that recorded them — one visit, one record, two steps.
        Assert.Equal(FicheOne, after.Steps[0].LinkedDentalRecordId);
        Assert.Equal(FicheOne, after.Steps[1].LinkedDentalRecordId);
        // The act is under way, not finished: the scellement is still to come.
        Assert.Equal(TreatmentPlanItemStatus.InProgress, after.Status);
        Assert.Equal(2, after.StepsDone);
        Assert.Equal(after.Steps[2].Id, after.NextStep?.Id);
    }

    /// <summary>
    /// The other half of the same story, and the one that proves the dead end is gone: the remaining step is
    /// recordable afterwards, by its own séance and its own fiche.
    /// </summary>
    [Fact]
    public async Task The_Remaining_Step_Is_Still_Recordable_By_Its_Own_Fiche()
    {
        var (plan, item) = BridgeWithThreeSteps();
        var first = SeanceFor(item, item.Steps[0].Id, item.Steps[1].Id);

        await DentalRecordLinker.LinkPlanItemAsync(
            PlansHolding(plan).Object, AppointmentsHolding(first).Object,
            plan.Id, item.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None,
            item.Steps[0].Id, first.Id);

        var scellement = plan.Items.Single().Steps[2];
        var second = SeanceFor(plan.Items.Single(), scellement.Id);

        var result = await DentalRecordLinker.LinkPlanItemAsync(
            PlansHolding(plan).Object, AppointmentsHolding(first, second).Object,
            plan.Id, item.Id, PatientId, ClinicId, FicheTwo, Day2, CancellationToken.None,
            scellement.Id, second.Id);

        Assert.True(result.IsSuccess);

        var after = plan.Items.Single();
        Assert.All(after.Steps, s => Assert.True(s.IsDone));
        Assert.Equal(TreatmentPlanItemStatus.Done, after.Status);
        // Three steps, two fiches — the refusal this whole feature exists to remove.
        Assert.Equal(FicheOne, after.Steps[0].LinkedDentalRecordId);
        Assert.Equal(FicheOne, after.Steps[1].LinkedDentalRecordId);
        Assert.Equal(FicheTwo, after.Steps[2].LinkedDentalRecordId);
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
    }

    /// <summary>
    /// Re-saving the fiche — which the patient page does routinely — must be a no-op on the steps, not a
    /// refusal. Each step already names THIS record, and <c>MarkDone</c> only refuses a <i>different</i> one.
    /// </summary>
    [Fact]
    public async Task Re_Saving_The_Same_Fiche_Changes_Nothing_And_Refuses_Nothing()
    {
        var (plan, item) = BridgeWithThreeSteps();
        var seance = SeanceFor(item, item.Steps[0].Id, item.Steps[1].Id);
        var plans = PlansHolding(plan);
        var appointments = AppointmentsHolding(seance);

        for (var pass = 0; pass < 2; pass++)
        {
            var result = await DentalRecordLinker.LinkPlanItemAsync(
                plans.Object, appointments.Object,
                plan.Id, item.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None,
                item.Steps[0].Id, seance.Id);
            Assert.True(result.IsSuccess);
        }

        var after = plan.Items.Single();
        Assert.Equal(2, after.StepsDone);
        Assert.Equal(TreatmentPlanItemStatus.InProgress, after.Status);
    }

    // ── everything the derivation must NOT change ────────────────────────────────────────────────────

    /// <summary>
    /// A fiche with no visit behind it — recorded straight from the patient page — must behave exactly as
    /// before: the named step and nothing else.
    /// </summary>
    [Fact]
    public async Task A_Fiche_With_No_Appointment_Closes_Only_The_Step_It_Names()
    {
        var (plan, item) = BridgeWithThreeSteps();

        var result = await DentalRecordLinker.LinkPlanItemAsync(
            PlansHolding(plan).Object, AppointmentsHolding().Object,
            plan.Id, item.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None,
            item.Steps[1].Id, appointmentId: null);

        Assert.True(result.IsSuccess);
        var after = plan.Items.Single();
        Assert.False(after.Steps[0].IsDone);
        Assert.True(after.Steps[1].IsDone);
        Assert.False(after.Steps[2].IsDone);
    }

    /// <summary>
    /// A séance booked for the act as a WHOLE — which is every booking made before steps existed — still takes
    /// the act-level path, and for a stepped act that advances its next pending step rather than declaring the
    /// bridge finished. The property the migration's safety rests on.
    /// </summary>
    [Fact]
    public async Task A_Seance_Booked_For_The_Whole_Act_Advances_Its_Next_Step_Only()
    {
        var (plan, item) = BridgeWithThreeSteps();
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, null, Day1, TimeSpan.FromMinutes(60));
        appointment.SetProcedures(new[]
        {
            new AppointmentProcedureInput(
                ProcedureTypeId: item.ProcedureTypeId,
                ProcedureName: "Bridge 4 dents",
                DurationMinutes: 60,
                ColorHex: null,
                AgreedCost: null,
                TreatmentPlanItemId: item.Id,
                TreatmentPlanItemStepId: null),
        });

        var result = await DentalRecordLinker.LinkPlanItemAsync(
            PlansHolding(plan).Object, AppointmentsHolding(appointment).Object,
            plan.Id, item.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None,
            treatmentPlanItemStepId: null, appointmentId: appointment.Id);

        Assert.True(result.IsSuccess);
        var after = plan.Items.Single();
        Assert.Equal(1, after.StepsDone);
        Assert.True(after.Steps[0].IsDone);
        Assert.Equal(TreatmentPlanItemStatus.InProgress, after.Status);
    }

    /// <summary>
    /// Another act's séance contributes nothing. The appointment may legitimately carry several devis acts, so
    /// the filter is on <c>TreatmentPlanItemId</c> — without it, recording one act of a grouped séance would
    /// close the steps of every other act booked into it.
    /// </summary>
    [Fact]
    public async Task Steps_Of_A_Different_Act_In_The_Same_Seance_Are_Left_Alone()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Deux bridges");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Bridge haut", 1000m, Guid.NewGuid(), Array.Empty<int>()),
            new TreatmentPlanItemInput(null, "Bridge bas", 1000m, Guid.NewGuid(), Array.Empty<int>()),
        });
        plan.Accept("2026-0005");

        foreach (var line in plan.Items)
        {
            plan.SetItemSteps(line.Id, new[]
            {
                new TreatmentPlanItemStepInput(null, "Préparation", 60),
                new TreatmentPlanItemStepInput(null, "Scellement", 30),
            });
        }

        var haut = plan.Items.First();
        var bas = plan.Items.Last();

        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, null, Day1, TimeSpan.FromMinutes(120));
        appointment.SetProcedures(new[]
        {
            new AppointmentProcedureInput(
                ProcedureTypeId: haut.ProcedureTypeId, ProcedureName: "Bridge haut", DurationMinutes: 60,
                ColorHex: null, AgreedCost: null, TreatmentPlanItemId: haut.Id,
                TreatmentPlanItemStepId: haut.Steps[0].Id),
            new AppointmentProcedureInput(
                ProcedureTypeId: bas.ProcedureTypeId, ProcedureName: "Bridge bas", DurationMinutes: 60,
                ColorHex: null, AgreedCost: null, TreatmentPlanItemId: bas.Id,
                TreatmentPlanItemStepId: bas.Steps[0].Id),
        });

        var result = await DentalRecordLinker.LinkPlanItemAsync(
            PlansHolding(plan).Object, AppointmentsHolding(appointment).Object,
            plan.Id, haut.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None,
            haut.Steps[0].Id, appointment.Id);

        Assert.True(result.IsSuccess);
        Assert.True(plan.Items.First(i => i.Id == haut.Id).Steps[0].IsDone);
        Assert.All(plan.Items.First(i => i.Id == bas.Id).Steps, s => Assert.False(s.IsDone));
    }

    /// <summary>
    /// A cross-tenant appointment contributes nothing and does <b>not</b> fail the save: the fiche is the
    /// clinical record, and it must stay recordable even when the visit it names cannot be read.
    /// </summary>
    [Fact]
    public async Task A_Foreign_Appointment_Is_Ignored_Rather_Than_Failing_The_Fiche()
    {
        var (plan, item) = BridgeWithThreeSteps();
        var foreign = new Appointment(
            Guid.NewGuid(), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), PatientId, null, Day1,
            TimeSpan.FromMinutes(60));

        var result = await DentalRecordLinker.LinkPlanItemAsync(
            PlansHolding(plan).Object, AppointmentsHolding(foreign).Object,
            plan.Id, item.Id, PatientId, ClinicId, FicheOne, Day1, CancellationToken.None,
            item.Steps[0].Id, foreign.Id);

        Assert.True(result.IsSuccess);
        var after = plan.Items.Single();
        Assert.True(after.Steps[0].IsDone);
        Assert.Equal(1, after.StepsDone);
    }
}
