using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// The handler half of the reversibility remediation — the six doors that let a devis out of a state it could
/// not previously leave, and the two money refusals that were missing on one side of a pair.
///
/// <para>
/// Every test here corresponds to a defect that shipped silently: an absorbing <c>Cancelled</c>, a void that
/// reported success and changed nothing, a remedy named in French by three refusals and implemented by none,
/// a devis stuck on the wrong patient, and parking that was all-or-nothing in both directions.
/// </para>
/// </summary>
public class PlanReversibilityHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherPatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTime Due = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public PlanReversibilityHandlerTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        NoBridgeInvoice();
        _appointments.Setup(r => r.GetByTreatmentPlanItemIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());
        // MediatR never hands a handler back null; Moq's default does, which surfaces as a generic failure.
        _mediator.Setup(m => m.Send(It.IsAny<UpdateAppointmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentDto>.Success(new AppointmentDto()));
    }

    private void NoBridgeInvoice() =>
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus, decimal TotalTtc, decimal Outstanding)>());

    private void BridgedTo(Guid planId, Guid invoiceId, string? number, InvoiceStatus status) =>
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus, decimal TotalTtc, decimal Outstanding)>
            {
                (planId, invoiceId, number, status, 0m, 0m)
            });

    /// <summary>An accepted 1 000 DT devis of two acts, with its auto-raised lump-sum échéance.</summary>
    private TreatmentPlan AcceptedPlan()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Réhabilitation");
        plan.SetItems(new[]
        {
            ("Couronne", 600m, (IReadOnlyList<int>)new[] { 11 }),
            ("Détartrage", 400m, (IReadOnlyList<int>)new[] { 12 }),
        });
        plan.Accept("2026-0014");
        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        return plan;
    }

    private void PatientIsLoadable(Guid id) =>
        _patients.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(id, ClinicId, "Leïla", "Ben Salah", new DateTime(1990, 1, 1), "Female"));

    // =========================================================================================================
    // C3 — Cancelled is leavable
    // =========================================================================================================

    [Fact]
    public async Task Uncancelling_Restores_The_Devis_Keeps_Its_Number_And_Appends_The_Motif()
    {
        var plan = AcceptedPlan();
        plan.Cancel("Patiente partie à l'étranger");
        PatientIsLoadable(PatientId);

        var handler = new UncancelTreatmentPlanCommandHandler(
            _plans.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<UncancelTreatmentPlanCommandHandler>.Instance);

        var result = await handler.Handle(
            new UncancelTreatmentPlanCommand { Id = plan.Id, Reason = "Elle est revenue" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(TreatmentPlanStatus.Cancelled, plan.Status);
        // The series stays gapless — the number was never released.
        Assert.Equal("2026-0014", plan.Number);
        // The first motif explains a document that may be in the patient's hands; it is appended to, never erased.
        Assert.Contains("Patiente partie à l'étranger", plan.CancellationReason);
        Assert.Contains("Elle est revenue", plan.CancellationReason);
        // The plan carries debt again the instant the status is written, so Σ Amount must equal the total.
        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Uncancelling_A_Live_Devis_Is_Refused_And_Writes_Nothing()
    {
        var plan = AcceptedPlan();

        var handler = new UncancelTreatmentPlanCommandHandler(
            _plans.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<UncancelTreatmentPlanCommandHandler>.Instance);

        var result = await handler.Handle(
            new UncancelTreatmentPlanCommand { Id = plan.Id, Reason = "Oups" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================================================
    // C12 — the void side of the bridge refusal
    // =========================================================================================================

    [Fact]
    public async Task Voiding_A_Payment_On_A_Bridged_Plan_Is_Refused_And_Names_The_Note()
    {
        var plan = AcceptedPlan();
        var installment = plan.Installments.First();
        var payment = plan.RecordInstallmentPayment(installment.Id, 300m, PaymentMethod.Cash, Due);
        BridgedTo(plan.Id, Guid.NewGuid(), "2026-0031", InvoiceStatus.Issued);

        var handler = new VoidInstallmentPaymentCommandHandler(
            _plans.Object, _invoices.Object, _patients.Object, _users.Object,
            _clinicResolver.Object, _clinicContext.Object, _uow.Object,
            NullLogger<VoidInstallmentPaymentCommandHandler>.Instance);

        var result = await handler.Handle(
            new VoidInstallmentPaymentCommand
            {
                PlanId = plan.Id,
                InstallmentId = installment.Id,
                PaymentId = payment.Id,
                Reason = "Chèque sans provision",
            },
            CancellationToken.None);

        // The old behaviour was HTTP 200 « paiement annulé » over an invoice Payment still live in la caisse.
        Assert.True(result.IsFailure);
        Assert.Contains("2026-0031", result.Error);
        Assert.False(payment.IsVoided);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Voiding_A_Payment_On_A_Plan_Whose_Bridge_Was_Cancelled_Still_Works()
    {
        var plan = AcceptedPlan();
        var installment = plan.Installments.First();
        var payment = plan.RecordInstallmentPayment(installment.Id, 300m, PaymentMethod.Cash, Due);
        // A cancelled note is void and represents nothing, so the échéancier is the live money again.
        BridgedTo(plan.Id, Guid.NewGuid(), "2026-0031", InvoiceStatus.Cancelled);
        PatientIsLoadable(PatientId);

        var handler = new VoidInstallmentPaymentCommandHandler(
            _plans.Object, _invoices.Object, _patients.Object, _users.Object,
            _clinicResolver.Object, _clinicContext.Object, _uow.Object,
            NullLogger<VoidInstallmentPaymentCommandHandler>.Instance);

        var result = await handler.Handle(
            new VoidInstallmentPaymentCommand
            {
                PlanId = plan.Id,
                InstallmentId = installment.Id,
                PaymentId = payment.Id,
                Reason = "Erreur de saisie",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(payment.IsVoided);
        Assert.Equal(0m, plan.AmountPaid);
    }

    // =========================================================================================================
    // M1 · M2 — per-act park and restore
    // =========================================================================================================

    [Fact]
    public async Task Parking_An_Act_Drops_Its_Fee_And_Respreads_The_Schedule()
    {
        var plan = AcceptedPlan();
        var couronne = plan.Items.First(i => i.DesignationFr == "Couronne");
        PatientIsLoadable(PatientId);

        var handler = new WithdrawTreatmentPlanItemCommandHandler(
            _plans.Object, _patients.Object, _appointments.Object, _clinicResolver.Object,
            _mediator.Object, _uow.Object, NullLogger<WithdrawTreatmentPlanItemCommandHandler>.Instance);

        var result = await handler.Handle(
            new WithdrawTreatmentPlanItemCommand { PlanId = plan.Id, ItemId = couronne.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(couronne.IsWithdrawn);
        Assert.Equal(400m, plan.TotalPlanned);
        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
        // The plan is NOT closed — that is what makes this different from « Arrêter le traitement ».
        Assert.Equal(TreatmentPlanStatus.Accepted, plan.Status);
    }

    [Fact]
    public async Task Parking_The_Last_Active_Act_Is_Refused_And_Names_The_Stop()
    {
        var plan = AcceptedPlan();
        var couronne = plan.Items.First(i => i.DesignationFr == "Couronne");
        var detartrage = plan.Items.First(i => i.DesignationFr == "Détartrage");
        plan.WithdrawItem(couronne.Id, Due);

        var handler = new WithdrawTreatmentPlanItemCommandHandler(
            _plans.Object, _patients.Object, _appointments.Object, _clinicResolver.Object,
            _mediator.Object, _uow.Object, NullLogger<WithdrawTreatmentPlanItemCommandHandler>.Instance);

        var result = await handler.Handle(
            new WithdrawTreatmentPlanItemCommand { PlanId = plan.Id, ItemId = detartrage.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("arrêtez le traitement", result.Error);
        Assert.False(detartrage.IsWithdrawn);
    }

    [Fact]
    public async Task Restoring_One_Act_Brings_Back_Only_That_Fee()
    {
        var plan = AcceptedPlan();
        var couronne = plan.Items.First(i => i.DesignationFr == "Couronne");
        plan.WithdrawItem(couronne.Id, Due);
        PatientIsLoadable(PatientId);

        var handler = new RestoreTreatmentPlanItemCommandHandler(
            _plans.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<RestoreTreatmentPlanItemCommandHandler>.Instance);

        var result = await handler.Handle(
            new RestoreTreatmentPlanItemCommand { PlanId = plan.Id, ItemId = couronne.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(couronne.IsWithdrawn);
        Assert.Equal(1000m, plan.TotalPlanned);
        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
    }

    /// <summary>
    /// « La patiente revient, mais seulement pour la couronne » — the case the all-or-nothing pair could not
    /// express, and the whole point of M2. Restoring one act must not re-inflate the total with the ones she
    /// declined.
    /// </summary>
    [Fact]
    public async Task Restoring_One_Act_Leaves_The_Others_Parked()
    {
        var plan = AcceptedPlan();
        var couronne = plan.Items.First(i => i.DesignationFr == "Couronne");
        var detartrage = plan.Items.First(i => i.DesignationFr == "Détartrage");
        plan.WithdrawItem(couronne.Id, Due);
        PatientIsLoadable(PatientId);

        var handler = new RestoreTreatmentPlanItemCommandHandler(
            _plans.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<RestoreTreatmentPlanItemCommandHandler>.Instance);

        await handler.Handle(
            new RestoreTreatmentPlanItemCommand { PlanId = plan.Id, ItemId = couronne.Id },
            CancellationToken.None);

        Assert.False(couronne.IsWithdrawn);
        Assert.False(detartrage.IsWithdrawn);
        Assert.Equal(1000m, plan.TotalPlanned);
    }

    // =========================================================================================================
    // M3 — « détachez-la de ce traitement » now names a command
    // =========================================================================================================

    [Fact]
    public async Task Detaching_A_Bridged_Note_Leaves_The_Devis_Standing()
    {
        var plan = AcceptedPlan();
        var invoice = new Invoice(
            Guid.NewGuid(), ClinicId, PatientId, dentalRecordId: null, appointmentId: null,
            treatmentPlanId: plan.Id);
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        PatientIsLoadable(PatientId);

        var handler = new DetachTreatmentPlanNoteCommandHandler(
            _plans.Object, _invoices.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<DetachTreatmentPlanNoteCommandHandler>.Instance);

        var result = await handler.Handle(
            new DetachTreatmentPlanNoteCommand { PlanId = plan.Id, InvoiceId = invoice.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(invoice.TreatmentPlanId);
        // The devis itself is untouched — that is the difference from cancelling or stopping it, which were
        // the only two ways to reach `TreatmentPlanBridgeRelease` before.
        Assert.Equal(TreatmentPlanStatus.Accepted, plan.Status);
        Assert.Equal(1000m, plan.TotalPlanned);
    }

    [Fact]
    public async Task Detaching_A_Note_That_Speaks_For_Nothing_Is_Refused_Rather_Than_Reported_As_Done()
    {
        var plan = AcceptedPlan();
        var unrelated = new Invoice(
            Guid.NewGuid(), ClinicId, PatientId, dentalRecordId: null, appointmentId: null,
            treatmentPlanId: null);
        _invoices.Setup(r => r.GetByIdAsync(unrelated.Id, It.IsAny<CancellationToken>())).ReturnsAsync(unrelated);

        var handler = new DetachTreatmentPlanNoteCommandHandler(
            _plans.Object, _invoices.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<DetachTreatmentPlanNoteCommandHandler>.Instance);

        var result = await handler.Handle(
            new DetachTreatmentPlanNoteCommand { PlanId = plan.Id, InvoiceId = unrelated.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Detaching_A_Carried_Note_Releases_The_Act_It_Was_Holding_At_Zero()
    {
        var plan = AcceptedPlan();
        var couronne = plan.Items.First(i => i.DesignationFr == "Couronne");
        var invoice = new Invoice(
            Guid.NewGuid(), ClinicId, PatientId, dentalRecordId: Guid.NewGuid(), appointmentId: null,
            treatmentPlanId: null);
        plan.MarkItemBilledOnInvoice(couronne.Id, invoice.Id, 600m);
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        PatientIsLoadable(PatientId);

        // The marker holds the line at 0 and `Revise` refuses to re-price it — that refusal's remedy is this.
        Assert.True(couronne.IsBilledElsewhere);
        Assert.Equal(0m, couronne.PlannedCost);

        var handler = new DetachTreatmentPlanNoteCommandHandler(
            _plans.Object, _invoices.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<DetachTreatmentPlanNoteCommandHandler>.Instance);

        var result = await handler.Handle(
            new DetachTreatmentPlanNoteCommand { PlanId = plan.Id, InvoiceId = invoice.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(couronne.IsBilledElsewhere);
        // ⚠️ The price is deliberately NOT invented back: nothing knows what it was before the marker imposed
        // the 0, and a figure guessed onto a devis is worse than a 0 the dentist can see and type over.
        Assert.Equal(0m, couronne.PlannedCost);
    }

    // =========================================================================================================
    // M5 — a devis on the wrong patient
    // =========================================================================================================

    [Fact]
    public async Task Reassigning_Moves_The_Devis_And_Keeps_Its_Number()
    {
        var plan = AcceptedPlan();
        PatientIsLoadable(OtherPatientId);

        var handler = new ReassignTreatmentPlanPatientCommandHandler(
            _plans.Object, _patients.Object, _invoices.Object, _appointments.Object,
            _clinicResolver.Object, _mediator.Object, _uow.Object,
            NullLogger<ReassignTreatmentPlanPatientCommandHandler>.Instance);

        var result = await handler.Handle(
            new ReassignTreatmentPlanPatientCommand { Id = plan.Id, PatientId = OtherPatientId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OtherPatientId, plan.PatientId);
        Assert.Equal("2026-0014", plan.Number);
    }

    [Fact]
    public async Task Reassigning_Is_Refused_Once_A_Seance_Has_Been_Recorded()
    {
        var plan = AcceptedPlan();
        plan.MarkItemDone(plan.Items.First().Id, Due, Guid.NewGuid());
        PatientIsLoadable(OtherPatientId);

        var handler = new ReassignTreatmentPlanPatientCommandHandler(
            _plans.Object, _patients.Object, _invoices.Object, _appointments.Object,
            _clinicResolver.Object, _mediator.Object, _uow.Object,
            NullLogger<ReassignTreatmentPlanPatientCommandHandler>.Instance);

        var result = await handler.Handle(
            new ReassignTreatmentPlanPatientCommand { Id = plan.Id, PatientId = OtherPatientId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PatientId, plan.PatientId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reassigning_Is_Refused_While_A_Live_Note_Names_The_Devis()
    {
        var plan = AcceptedPlan();
        BridgedTo(plan.Id, Guid.NewGuid(), "2026-0031", InvoiceStatus.Issued);
        PatientIsLoadable(OtherPatientId);

        var handler = new ReassignTreatmentPlanPatientCommandHandler(
            _plans.Object, _patients.Object, _invoices.Object, _appointments.Object,
            _clinicResolver.Object, _mediator.Object, _uow.Object,
            NullLogger<ReassignTreatmentPlanPatientCommandHandler>.Instance);

        var result = await handler.Handle(
            new ReassignTreatmentPlanPatientCommand { Id = plan.Id, PatientId = OtherPatientId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("2026-0031", result.Error);
        Assert.Equal(PatientId, plan.PatientId);
    }

    // =========================================================================================================
    // M6 — the practitioner is changeable
    // =========================================================================================================

    [Fact]
    public async Task Setting_The_Doctor_Stores_The_Id_And_Returns_The_Name()
    {
        var plan = AcceptedPlan();
        var doctor = new Doctor(Guid.NewGuid(), ClinicId, "Karim", "Trabelsi", "Dentist");
        _doctors.Setup(r => r.GetByIdAsync(doctor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doctor);
        PatientIsLoadable(PatientId);

        var handler = new SetTreatmentPlanDoctorCommandHandler(
            _plans.Object, _doctors.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<SetTreatmentPlanDoctorCommandHandler>.Instance);

        var result = await handler.Handle(
            new SetTreatmentPlanDoctorCommand { Id = plan.Id, DoctorId = doctor.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(doctor.Id, plan.DoctorId);
        Assert.Equal(doctor.Id, result.Value!.DoctorId);
        Assert.Equal(doctor.FullName, result.Value.DoctorName);
    }

    [Fact]
    public async Task A_Doctor_From_Another_Clinic_Reads_As_Not_Found_And_Is_Never_Stored()
    {
        var plan = AcceptedPlan();
        var foreign = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Autre", "Praticien", "Dentist");
        _doctors.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var handler = new SetTreatmentPlanDoctorCommandHandler(
            _plans.Object, _doctors.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<SetTreatmentPlanDoctorCommandHandler>.Instance);

        var result = await handler.Handle(
            new SetTreatmentPlanDoctorCommand { Id = plan.Id, DoctorId = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(plan.DoctorId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================================================
    // m1 — « Terminer » no longer closes a plan over unfinished work
    // =========================================================================================================

    [Fact]
    public async Task Completing_A_Plan_With_Unrealised_Acts_Is_Refused()
    {
        var plan = AcceptedPlan();

        var handler = new CompleteTreatmentPlanCommandHandler(
            _plans.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<CompleteTreatmentPlanCommandHandler>.Instance);

        var result = await handler.Handle(
            new CompleteTreatmentPlanCommand { Id = plan.Id }, CancellationToken.None);

        // It used to succeed, leaving the patient owing for séances nobody would ever do on a devis badged
        // « Terminé » where every remedy had been withdrawn. « Arrêter le traitement » owns that case.
        Assert.True(result.IsFailure);
        Assert.Equal(TreatmentPlanStatus.Accepted, plan.Status);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Completing_A_Plan_Whose_Work_Is_Finished_Still_Works()
    {
        var plan = AcceptedPlan();
        foreach (var item in plan.Items.ToList())
        {
            plan.MarkItemDone(item.Id, Due, Guid.NewGuid());
        }
        PatientIsLoadable(PatientId);

        // Marking the last act done auto-completes the plan, so the endpoint's own call is the idempotent
        // case — it must not throw « déjà clôturé » at a plan it just closed itself.
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
    }
}
