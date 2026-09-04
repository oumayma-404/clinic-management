using System;
using System.Linq;
using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Booking a séance that carries out one or more <b>steps</b> of a devis act.
///
/// <para><b>This class exists for one guard in particular.</b> <c>SetProcedures</c> held two duplicate rules, and
/// <i>both</i> refused the client's headline use case: « préparation et empreinte dans la même séance ». Those are
/// two steps of one bridge, so they resolve to the same catalogue act <b>and</b> to the same plan line — tripping
/// the « same act twice is a mis-click » check and the « same devis act twice » check at once. Relaxing them is
/// the feature; relaxing them too far puts a real mis-click on the agenda silently.</para>
///
/// <para>The other property here is the ctor invariant: a step row must always carry its act's id, because four
/// existing reads key off <c>LinkedTreatmentPlanItemIds</c> and a step-only row drops out of all of them —
/// including <c>VisitClosure</c>'s « couvert par le devis », which would make a séance of planned work read as
/// unbilled.</para>
/// </summary>
public class AppointmentStepBookingTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Bridge = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid PlanItem = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid OtherPlanItem = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid StepPrep = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StepEmpreinte = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Appointment Appointment() => new(
        id: Guid.NewGuid(),
        clinicId: ClinicId,
        patientId: PatientId,
        doctorId: null,
        appointmentDateTime: new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc),
        duration: TimeSpan.FromMinutes(60));

    private static AppointmentProcedureInput Act(
        Guid? procedureTypeId, string name, Guid? itemId = null, Guid? stepId = null) =>
        new(procedureTypeId, name, 30, "#0B6B5F", null, itemId, stepId);

    // ── the client's headline case ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The test this whole class is for.</b> Two steps of one bridge in one séance — same catalogue act, same
    /// devis line, different steps. Before the guard change this threw « L'acte … est déjà présent dans ce
    /// rendez-vous », which is the feature being refused by its own domain.
    /// </summary>
    [Fact]
    public void Two_Steps_Of_One_Act_Can_Share_A_Seance()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[]
        {
            Act(Bridge, "Bridge 4 dents", PlanItem, StepPrep),
            Act(Bridge, "Bridge 4 dents", PlanItem, StepEmpreinte),
        });

        Assert.Equal(2, appointment.Procedures.Count);
        Assert.Equal(new[] { StepPrep, StepEmpreinte }, appointment.LinkedTreatmentPlanItemStepIds);
        // The act's own id still reaches the reads that key off it — once, deduped.
        Assert.Equal(new[] { PlanItem }, appointment.LinkedTreatmentPlanItemIds);
        // Duration is still the sum of the acts booked, so a two-step séance is a 60-minute slot.
        Assert.Equal(60, appointment.TotalProcedureDurationMinutes);
    }

    // ── what the relaxation must NOT let through ─────────────────────────────────────────────────────

    /// <summary>The same step twice is a mis-click, exactly as the same act twice was.</summary>
    [Fact]
    public void The_Same_Step_Twice_Is_Still_Refused()
    {
        var appointment = Appointment();

        var ex = Assert.Throws<InvalidOperationException>(() => appointment.SetProcedures(new[]
        {
            Act(Bridge, "Bridge 4 dents", PlanItem, StepPrep),
            Act(Bridge, "Bridge 4 dents", PlanItem, StepPrep),
        }));

        Assert.Contains("même étape", ex.Message);
    }

    /// <summary>
    /// The pre-existing rule, untouched for rows that name no step — which is every booking the product made
    /// before this feature.
    /// </summary>
    [Fact]
    public void The_Same_Act_Twice_With_No_Steps_Is_Still_Refused()
    {
        var appointment = Appointment();

        var ex = Assert.Throws<InvalidOperationException>(() => appointment.SetProcedures(new[]
        {
            Act(Bridge, "Bridge 4 dents"),
            Act(Bridge, "Bridge 4 dents"),
        }));

        Assert.Contains("déjà présent", ex.Message);
    }

    /// <summary>And so is the same devis line twice, when neither row names a step.</summary>
    [Fact]
    public void The_Same_Devis_Act_Twice_With_No_Steps_Is_Still_Refused()
    {
        var appointment = Appointment();

        var ex = Assert.Throws<InvalidOperationException>(() => appointment.SetProcedures(new[]
        {
            Act(Bridge, "Bridge 4 dents", PlanItem),
            Act(null, "Bridge 4 dents", PlanItem),
        }));

        Assert.Contains("deux fois", ex.Message);
    }

    /// <summary>
    /// « Tout le bridge » and « le scellement du bridge » in one séance contradict each other: the first says the
    /// act finishes here, the second that one step of it does.
    /// </summary>
    [Fact]
    public void One_Act_Booked_Both_Whole_And_By_Step_Is_Refused()
    {
        var appointment = Appointment();

        var ex = Assert.Throws<InvalidOperationException>(() => appointment.SetProcedures(new[]
        {
            Act(Bridge, "Bridge 4 dents", PlanItem),
            Act(Bridge, "Bridge 4 dents", PlanItem, StepPrep),
        }));

        Assert.Contains("en entier et par étape", ex.Message);
    }

    /// <summary>The order of the two rows must not decide it — the rule is symmetric.</summary>
    [Fact]
    public void The_Whole_And_By_Step_Refusal_Does_Not_Depend_On_Row_Order()
    {
        var appointment = Appointment();

        var ex = Assert.Throws<InvalidOperationException>(() => appointment.SetProcedures(new[]
        {
            Act(Bridge, "Bridge 4 dents", PlanItem, StepPrep),
            Act(Bridge, "Bridge 4 dents", PlanItem),
        }));

        Assert.Contains("en entier et par étape", ex.Message);
    }

    /// <summary>Two different devis acts in one séance is ordinary grouping and stays allowed.</summary>
    [Fact]
    public void Two_Different_Devis_Acts_Still_Group_Into_One_Seance()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[]
        {
            Act(Bridge, "Bridge 4 dents", PlanItem, StepPrep),
            Act(null, "Couronne 26", OtherPlanItem),
        });

        Assert.Equal(2, appointment.Procedures.Count);
        Assert.Equal(new[] { PlanItem, OtherPlanItem }, appointment.LinkedTreatmentPlanItemIds.Order());
    }

    // ── the ctor invariant ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A step with no act behind it would carry planned work that <c>LinkedTreatmentPlanItemIds</c> cannot see —
    /// so the devis read-back, the plan projection, the plan timeline and <c>VisitClosure</c>'s
    /// « couvert par le devis » would all miss it, and the séance would read as unbilled while carrying a priced
    /// act.
    /// </summary>
    [Fact]
    public void A_Step_Cannot_Be_Booked_Without_Its_Act()
    {
        var ex = Assert.Throws<ArgumentException>(() => new AppointmentProcedure(
            Guid.NewGuid(), Guid.NewGuid(), Bridge, "Bridge 4 dents", 30, "#0B6B5F",
            agreedCost: null, treatmentPlanItemId: null, sequenceNumber: 0,
            treatmentPlanItemStepId: StepPrep));

        Assert.Contains("sans l'acte du devis", ex.Message);
    }

    /// <summary>
    /// A séance with no step at all keeps the empty set — which is what every appointment written before this
    /// feature holds, and what any read ignoring the new collection sees.
    /// </summary>
    [Fact]
    public void An_Ordinary_Seance_Links_No_Steps()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[] { Act(Bridge, "Détartrage") });

        Assert.Empty(appointment.LinkedTreatmentPlanItemStepIds);
    }

    /// <summary>
    /// A continuation séance carries <c>AgreedCost = 0</c> — « déjà facturé sur le devis ». Zero has always been
    /// a real negotiated answer here (an act offered), so it needs no new money state; what matters is that it
    /// survives <c>SetProcedures</c> onto the row the fiche will read.
    /// </summary>
    [Fact]
    public void A_Continuation_Seance_Carries_A_Zero_Price_Onto_The_Row()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[]
        {
            new AppointmentProcedureInput(Bridge, "Bridge 4 dents", 30, "#0B6B5F", 0m, PlanItem, StepEmpreinte),
        });

        var row = appointment.Procedures.Single();
        Assert.Equal(0m, row.AgreedCost);
        Assert.Equal(StepEmpreinte, row.TreatmentPlanItemStepId);
        Assert.Equal(PlanItem, row.TreatmentPlanItemId);
    }
}
