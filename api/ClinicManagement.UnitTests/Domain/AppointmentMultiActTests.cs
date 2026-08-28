using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.TreatmentPlans;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Pins multi-act séances: an appointment holds a <b>list</b> of acts, and the three scalars it used to hold on
/// its own (<c>ProcedureTypeId</c>/<c>ProcedureDurationMinutes</c>/<c>ProcedureColorHex</c>) plus
/// <c>TreatmentPlanItemId</c> are now a **derived snapshot of the first row**.
/// <para>
/// The derivation is the load-bearing part, and the reason these tests exist: every existing read still keys off
/// those scalars (the agenda's colour, the fiche de soins proposal, `IsUsedByFutureAppointments`, the Google sync).
/// If the list and the scalars can disagree, a séance shows one act on the calendar and a different one in the
/// dialog that edits it.
/// </para>
/// </summary>
public class AppointmentMultiActTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Detartrage = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Obturation = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Extraction = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PlanItemA = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
    private static readonly Guid PlanItemB = Guid.Parse("b1b1b1b1-b1b1-b1b1-b1b1-b1b1b1b1b1b1");

    private static Appointment Appointment(DateTime? at = null) => new(
        Guid.NewGuid(), ClinicId, PatientId, doctorId: null,
        at ?? new DateTime(2026, 3, 12, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));

    private static AppointmentProcedureInput Act(
        Guid procedureTypeId, string name, int minutes, string colour, Guid? planItemId = null) =>
        new(procedureTypeId, name, minutes, colour, planItemId);

    // ---- The derived lead-act snapshot -------------------------------------------------------------------

    [Fact]
    public void SetProcedures_Derives_The_Lead_Act_Snapshot_From_The_First_Row()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[]
        {
            Act(Detartrage, "Détartrage", 30, "#4F83CC"),
            Act(Obturation, "Obturation", 45, "#2A9D8F"),
        });

        Assert.Equal(2, appointment.Procedures.Count);
        Assert.Equal(Detartrage, appointment.ProcedureTypeId);
        Assert.Equal(30, appointment.ProcedureDurationMinutes);
        Assert.Equal("#4F83CC", appointment.ProcedureColorHex);
    }

    // The séance's length is the SUM. A three-act visit that inherited only the first act's 30 minutes would
    // collide with whatever is booked after it on every calendar it appears on.
    [Fact]
    public void TotalProcedureDurationMinutes_Sums_Every_Act()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[]
        {
            Act(Detartrage, "Détartrage", 30, "#4F83CC"),
            Act(Obturation, "Obturation", 45, "#2A9D8F"),
            Act(Extraction, "Extraction", 20, "#E76F51"),
        });

        Assert.Equal(95, appointment.TotalProcedureDurationMinutes);
    }

    // A link-only act (a hand-typed devis line) contributes no duration, because nothing anywhere knows how long
    // it takes. It must still be counted as an act and still carry its link.
    [Fact]
    public void A_Link_Only_Act_Carries_Its_Plan_Link_And_No_Duration()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[] { new AppointmentProcedureInput(null, "Facette céramique", null, null, PlanItemA) });

        var row = Assert.Single(appointment.Procedures);
        Assert.Null(row.ProcedureTypeId);
        Assert.Equal("Facette céramique", row.ProcedureName);
        Assert.Equal(PlanItemA, row.TreatmentPlanItemId);
        Assert.Equal(0, appointment.TotalProcedureDurationMinutes);
        Assert.Equal(PlanItemA, appointment.TreatmentPlanItemId);
    }

    [Fact]
    public void SetProcedures_Assigns_Sequence_Numbers_In_The_Given_Order()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[]
        {
            Act(Obturation, "Obturation", 45, "#2A9D8F"),
            Act(Detartrage, "Détartrage", 30, "#4F83CC"),
        });

        Assert.Equal(new[] { 0, 1 }, appointment.Procedures.Select(p => p.SequenceNumber));
        Assert.Equal(new Guid?[] { Obturation, Detartrage }, appointment.Procedures.Select(p => p.ProcedureTypeId));
    }

    [Fact]
    public void SetProcedures_Replaces_The_Whole_List()
    {
        var appointment = Appointment();
        appointment.SetProcedures(new[] { Act(Detartrage, "Détartrage", 30, "#4F83CC") });

        appointment.SetProcedures(new[] { Act(Extraction, "Extraction", 20, "#E76F51") });

        Assert.Equal(Extraction, Assert.Single(appointment.Procedures).ProcedureTypeId);
        Assert.Equal(Extraction, appointment.ProcedureTypeId);
    }

    // An empty list is a real instruction (« ce rendez-vous n'a plus d'acte »), and it must clear the derived
    // scalars too — leaving a stale ProcedureTypeId behind would keep painting the card with a removed act.
    [Fact]
    public void SetProcedures_With_No_Acts_Clears_The_Derived_Snapshot()
    {
        var appointment = Appointment();
        appointment.SetProcedures(new[] { Act(Detartrage, "Détartrage", 30, "#4F83CC", PlanItemA) });

        appointment.SetProcedures(Array.Empty<AppointmentProcedureInput>());

        Assert.Empty(appointment.Procedures);
        Assert.Null(appointment.ProcedureTypeId);
        Assert.Null(appointment.ProcedureDurationMinutes);
        Assert.Null(appointment.ProcedureColorHex);
        Assert.Null(appointment.TreatmentPlanItemId);
    }

    // ---- Duplicates ------------------------------------------------------------------------------------

    // Refused, not deduped: the user picked it twice, and quietly dropping one leaves them looking at a séance
    // that does not match what they selected. « Deux obturations » is a per-tooth quantity on the fiche de soins.
    [Fact]
    public void SetProcedures_Refuses_The_Same_Act_Twice()
    {
        var appointment = Appointment();

        var ex = Assert.Throws<InvalidOperationException>(() => appointment.SetProcedures(new[]
        {
            Act(Detartrage, "Détartrage", 30, "#4F83CC"),
            Act(Detartrage, "Détartrage", 30, "#4F83CC"),
        }));

        Assert.Contains("Détartrage", ex.Message);
    }

    [Fact]
    public void SetProcedures_Refuses_The_Same_Plan_Step_Twice()
    {
        var appointment = Appointment();

        Assert.Throws<InvalidOperationException>(() => appointment.SetProcedures(new[]
        {
            Act(Detartrage, "Détartrage", 30, "#4F83CC", PlanItemA),
            Act(Obturation, "Obturation", 45, "#2A9D8F", PlanItemA),
        }));
    }

    // ---- Plan links ------------------------------------------------------------------------------------

    // The point of the whole feature: two devis acts grouped into one visit are two links, not one.
    [Fact]
    public void LinkedTreatmentPlanItemIds_Reports_Every_Grouped_Step()
    {
        var appointment = Appointment();

        appointment.SetProcedures(new[]
        {
            Act(Detartrage, "Détartrage", 30, "#4F83CC", PlanItemA),
            Act(Obturation, "Obturation", 45, "#2A9D8F", PlanItemB),
        });

        Assert.Equal(
            new HashSet<Guid> { PlanItemA, PlanItemB },
            appointment.LinkedTreatmentPlanItemIds.ToHashSet());
        // The scalar is the FIRST linked step — what the pre-existing single-link reads keep seeing.
        Assert.Equal(PlanItemA, appointment.TreatmentPlanItemId);
    }

    // Rows written before the collection existed have a scalar link and no child rows. They must keep resolving.
    [Fact]
    public void LinkedTreatmentPlanItemIds_Includes_The_Legacy_Scalar_Link()
    {
        var appointment = Appointment();
        appointment.SetTreatmentPlanItem(PlanItemA);

        Assert.Equal(new[] { PlanItemA }, appointment.LinkedTreatmentPlanItemIds);
    }

    // ---- SetProcedureType (the one-act path) -----------------------------------------------------------

    [Fact]
    public void SetProcedureType_Keeps_The_Collection_In_Step()
    {
        var appointment = Appointment();
        appointment.SetProcedures(new[]
        {
            Act(Detartrage, "Détartrage", 30, "#4F83CC"),
            Act(Obturation, "Obturation", 45, "#2A9D8F"),
        });

        appointment.SetProcedureType(Extraction, 20, "#E76F51", "Extraction");

        var row = Assert.Single(appointment.Procedures);
        Assert.Equal(Extraction, row.ProcedureTypeId);
        Assert.Equal(Extraction, appointment.ProcedureTypeId);
    }

    // Clearing the act does not mean the patient stopped coming in for that devis step; SetTreatmentPlanItem owns
    // that decision. This was true before the collection existed and must stay true.
    [Fact]
    public void SetProcedureType_Preserves_The_Plan_Link()
    {
        var appointment = Appointment();
        appointment.SetProcedures(new[] { Act(Detartrage, "Détartrage", 30, "#4F83CC", PlanItemA) });

        appointment.SetProcedureType(null, null, null);

        Assert.Empty(appointment.Procedures);
        Assert.Null(appointment.ProcedureTypeId);
        Assert.Equal(PlanItemA, appointment.TreatmentPlanItemId);
    }

    // ---- RefreshProcedureSnapshot ---------------------------------------------------------------------

    // `UpdateProcedureTypeCommand` calls this when a procedure is renamed or recoloured. It used to call
    // SetProcedureType, which now means "this visit has exactly one act" — so renaming a procedure would have
    // deleted the other acts of every séance using it.
    [Fact]
    public void RefreshProcedureSnapshot_Re_Snapshots_Without_Dropping_The_Other_Acts()
    {
        var appointment = Appointment();
        appointment.SetProcedures(new[]
        {
            Act(Detartrage, "Détartrage", 30, "#4F83CC"),
            Act(Obturation, "Obturation", 45, "#2A9D8F"),
        });

        appointment.RefreshProcedureSnapshot(Obturation, "Obturation composite", "#6BAA75");

        Assert.Equal(2, appointment.Procedures.Count);
        var refreshed = appointment.Procedures.Single(p => p.ProcedureTypeId == Obturation);
        Assert.Equal("Obturation composite", refreshed.ProcedureName);
        Assert.Equal("#6BAA75", refreshed.ColorHex);
        // The lead act is a different procedure, so its colour is untouched.
        Assert.Equal("#4F83CC", appointment.ProcedureColorHex);
    }

    [Fact]
    public void RefreshProcedureSnapshot_Recolours_The_Lead_Act_Scalar()
    {
        var appointment = Appointment();
        appointment.SetProcedures(new[] { Act(Detartrage, "Détartrage", 30, "#4F83CC") });

        appointment.RefreshProcedureSnapshot(Detartrage, "Détartrage", "#E9A23B");

        Assert.Equal("#E9A23B", appointment.ProcedureColorHex);
    }

    // ---- ProcedureType deletion guard ------------------------------------------------------------------

    // A procedure booked as the SECOND act of a future visit is just as much in use. Matching only the lead-act
    // scalar would hard-delete it out from under that booking.
    [Fact]
    public void IsUsedByFutureAppointments_Sees_A_Non_Lead_Act()
    {
        var obturation = new ProcedureType(Obturation, ClinicId, "Obturation", 45, ColorHex.FromString("#2A9D8F"));
        var appointment = Appointment(DateTime.UtcNow.AddDays(7));
        appointment.SetProcedures(new[]
        {
            Act(Detartrage, "Détartrage", 30, "#4F83CC"),
            Act(Obturation, "Obturation", 45, "#2A9D8F"),
        });

        Assert.True(obturation.IsUsedByFutureAppointments(new[] { appointment }));
    }

    // ---- The devis read-back ---------------------------------------------------------------------------

    /// <summary>
    /// The projection must resolve <b>both</b> acts of a grouped séance. Keying on the parent scalar left the
    /// second one reading « À planifier » forever — offering to book a visit the patient is already coming to.
    /// </summary>
    [Fact]
    public async Task Workflow_Projection_Resolves_Every_Act_Of_A_Grouped_Seance()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Plan");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(PlanItemA, "Détartrage", 60m, Detartrage, Array.Empty<int>()),
            new TreatmentPlanItemInput(PlanItemB, "Obturation", 90m, Obturation, Array.Empty<int>()),
        });

        var appointment = Appointment(new DateTime(2026, 4, 2, 9, 0, 0, DateTimeKind.Utc));
        appointment.SetProcedures(new[]
        {
            Act(Detartrage, "Détartrage", 30, "#4F83CC", PlanItemA),
            Act(Obturation, "Obturation", 45, "#2A9D8F", PlanItemB),
        });

        var appointments = new Mock<IAppointmentRepository>();
        appointments
            .Setup(r => r.GetByTreatmentPlanItemIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { appointment });
        var invoices = new Mock<IInvoiceRepository>();
        invoices
            .Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

        var workflow = await TreatmentPlanWorkflowProjection.BuildAsync(
            new[] { plan }, ClinicId, appointments.Object, invoices.Object,
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), default);

        Assert.Equal(appointment.Id, workflow.ScheduledByItemId[PlanItemA].Id);
        Assert.Equal(appointment.Id, workflow.ScheduledByItemId[PlanItemB].Id);
    }

    // ---- Reconcile (the wire contract) -----------------------------------------------------------------

    // The list is newer and strictly more expressive, so it wins. Applying both would let the single-act path
    // collapse the list it had just set.
    [Fact]
    public void Reconcile_Prefers_The_List_Over_The_Single_Act_Field()
    {
        var reconciled = AppointmentProcedureSelection.Reconcile(
            new List<AppointmentProcedureRequest> { new() { ProcedureTypeId = Obturation } },
            Detartrage,
            PlanItemA);

        Assert.Equal(Obturation, Assert.Single(reconciled).ProcedureTypeId);
    }

    [Fact]
    public void Reconcile_Promotes_A_Single_Act_To_A_One_Element_List()
    {
        var reconciled = AppointmentProcedureSelection.Reconcile(null, Detartrage, PlanItemA);

        var only = Assert.Single(reconciled);
        Assert.Equal(Detartrage, only.ProcedureTypeId);
        Assert.Equal(PlanItemA, only.TreatmentPlanItemId);
    }

    // Booking a hand-typed devis line sends only `treatmentPlanItemId`. Returning an empty list for it would let
    // SetProcedures derive a null link — losing the very thing the booking was for.
    [Fact]
    public void Reconcile_Keeps_A_Plan_Link_That_Has_No_Procedure()
    {
        var reconciled = AppointmentProcedureSelection.Reconcile(null, null, PlanItemA);

        var only = Assert.Single(reconciled);
        Assert.Null(only.ProcedureTypeId);
        Assert.Equal(PlanItemA, only.TreatmentPlanItemId);
    }

    [Fact]
    public void Reconcile_Yields_Nothing_For_A_Busy_Slot()
    {
        Assert.Empty(AppointmentProcedureSelection.Reconcile(null, null, null));
    }
}
