using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// A note d'honoraires whose act a continuation devis holds at <b>0</b> may not be voided out from under it.
///
/// <para>The money this protects, measured on the reported shape: an act billed 90 on a note, continued at 10 on
/// a devis that prices the first act 0 and is deliberately <b>not bridged</b> to the note. Nothing linked the two,
/// so cancelling or deleting the note left the 90 on <b>no document at all</b> — the devis line stays 0 (there is
/// no bridge for a cascade to follow) and a cancelled note is dropped by « Solde patient », « Créances », la
/// caisse and the dashboard alike. No error on any screen.</para>
///
/// <para>⚠️ <b>It was reachable in two clicks and cheaply.</b> <c>Invoice.CanCancel</c> blocks only a note
/// carrying a live payment, and a <c>Draft</c> is <i>deleted</i> rather than cancelled — so the unpaid and draft
/// cases, which are exactly the ones a continuation is most often built on, had no guard whatsoever. Both doors
/// are tested here, because they are two commands and this repo's dominant defect is a rule wired to one of
/// them.</para>
/// </summary>
public class NoteCarriedActGuardTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid PatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public NoteCarriedActGuardTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(PatientId, ClinicId, "Amine", "Trabelsi", new DateTime(1985, 4, 12), "Male"));

        // Nothing carries a note unless a test says so. Stated rather than left to Moq's default value, which
        // for this signature is a null task result — an NRE the handler's catch-all turns into « Erreur lors de
        // l'annulation », i.e. a refusal that looks exactly like the guard firing.
        _plans.Setup(r => r.GetPlansBilledOnInvoiceAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, string?, TreatmentPlanStatus)>());
        _plans.Setup(r => r.GetByLinkedDentalRecordAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TreatmentPlan>());
    }

    /// <summary>A note billing one fiche — the shape a séance's own note has.</summary>
    private Invoice NoteForFiche(Guid recordId, bool issued, string number = "2026-0019")
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId, dentalRecordId: recordId);
        invoice.SetLines(new[] { ("Soin de carie / obturation", 1, 90m, (Guid?)recordId) });
        if (issued)
        {
            invoice.Issue(number);
        }
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        return invoice;
    }

    /// <summary>
    /// A continuation devis carrying <paramref name="recordId"/>'s act at 0, marked against
    /// <paramref name="markedNoteId"/> — which for a corrected note is a <b>different</b> (cancelled) note from
    /// the one being acted on.
    /// </summary>
    private TreatmentPlan CarryingPlan(Guid recordId, Guid markedNoteId, TreatmentPlanStatus status, string number)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Soin de carie / obturation");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Soin de carie / obturation", 0m, null, new List<int> { 37 }),
            new TreatmentPlanItemInput(null, "Séance suivante", 10m, null, new List<int> { 37 }),
        });
        var first = plan.Items.First();
        plan.SetItemSteps(first.Id, new[] { new TreatmentPlanItemStepInput(null, "1re séance", null) });
        plan.MarkItemStepDone(first.Id, first.Steps.First().Id, new DateTime(2026, 9, 10), recordId);
        plan.MarkItemBilledOnInvoice(first.Id, markedNoteId, 90m);
        if (status != TreatmentPlanStatus.Draft)
        {
            plan.Accept(number);
        }
        _plans.Setup(r => r.GetByLinkedDentalRecordAsync(ClinicId, recordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { plan });
        return plan;
    }

    private Invoice Note(bool issued)
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Soin de carie / obturation", 1, 90m) });
        if (issued)
        {
            invoice.Issue("2026-0019");
        }
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        return invoice;
    }

    private void CarriedBy(Guid invoiceId, TreatmentPlanStatus status, string? number) =>
        _plans.Setup(r => r.GetPlansBilledOnInvoiceAsync(ClinicId, invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (Guid.NewGuid(), number, status) });

    private CancelInvoiceCommandHandler Cancel() => new(
        _invoices.Object, _patients.Object, _plans.Object, _clinicResolver.Object, _uow.Object,
        NullLogger<CancelInvoiceCommandHandler>.Instance);

    private DeleteInvoiceCommandHandler Delete() => new(
        _invoices.Object, _plans.Object, _clinicResolver.Object, _uow.Object,
        NullLogger<DeleteInvoiceCommandHandler>.Instance);

    /// <summary>An issued, unpaid note carried by a live devis: cancelling it would orphan the 90.</summary>
    [Fact]
    public async Task Cancelling_A_Carried_Note_Is_Refused_And_Names_The_Devis()
    {
        var invoice = Note(issued: true);
        CarriedBy(invoice.Id, TreatmentPlanStatus.InProgress, "2026-0012");

        var result = await Cancel().Handle(
            new CancelInvoiceCommand { Id = invoice.Id, Reason = "erreur" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteCarriedActGuard.CarriedByPlanCode, result.Code);
        Assert.Contains("2026-0012", result.Error);
        // ⚠️ The refusal must land BEFORE the mutation, or it has refused nothing.
        Assert.NotEqual(InvoiceStatus.Cancelled, invoice.Status);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The <b>cheaper</b> door, and the one the cancel path can never see: a Draft note is deleted, not
    /// cancelled, and a continuation built on a Draft is an ordinary case.
    /// </summary>
    [Fact]
    public async Task Deleting_A_Carried_Draft_Note_Is_Refused()
    {
        var invoice = Note(issued: false);
        CarriedBy(invoice.Id, TreatmentPlanStatus.Accepted, "2026-0012");

        var result = await Delete().Handle(new DeleteInvoiceCommand { Id = invoice.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteCarriedActGuard.CarriedByPlanCode, result.Code);
        _invoices.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// ⚠️ <b>A cancelled devis claims nothing, so it does not hold the note hostage</b> — that is the recovery
    /// path the refusal names (« annulez d'abord le traitement »), and gating on anything narrower would make it
    /// a dead end.
    /// </summary>
    [Fact]
    public async Task A_Cancelled_Devis_Does_Not_Hold_The_Note()
    {
        var invoice = Note(issued: true);
        CarriedBy(invoice.Id, TreatmentPlanStatus.Cancelled, "2026-0012");

        var result = await Cancel().Handle(
            new CancelInvoiceCommand { Id = invoice.Id, Reason = "erreur" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
    }

    /// <summary>
    /// ⚠️ <b>A <c>Draft</c> devis DOES hold it</b>, although it carries no debt — « Suivre ce traitement »
    /// creates an un-numbered plan that is clinically live and financially inert, and emptying it of its first
    /// act's fee is the same loss under a different status. Gating on
    /// <c>PlanBillingRules.CarriesDebt</c> (false for a Draft) would have been the natural mistake.
    /// </summary>
    [Fact]
    public async Task A_Draft_Devis_Still_Holds_The_Note_Even_Though_It_Carries_No_Debt()
    {
        var invoice = Note(issued: true);
        CarriedBy(invoice.Id, TreatmentPlanStatus.Draft, null);

        var result = await Cancel().Handle(
            new CancelInvoiceCommand { Id = invoice.Id, Reason = "erreur" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        // An un-numbered plan has no number to name, and the sentence must not print a hole where one goes.
        Assert.Contains("un traitement suivi", result.Error);
        Assert.DoesNotContain("n° ,", result.Error);
    }

    /// <summary>
    /// ⚠️ <b>THE REPLACEMENT of a corrected note is guarded too, and asking by id alone would have missed it.</b>
    ///
    /// <para>A note is never edited — it is <b>replaced</b>. Correcting a note
    /// (<c>IssueInvoiceCommand.SupersedePredecessorAsync</c>) and correcting its séance
    /// (<c>UpdateDentalRecordCommand.RetireForCorrectionAsync</c>) both cancel the old one and raise a fresh one,
    /// and the devis' marker goes on naming the cancelled shell. Guarding on that stored id alone leaves the note
    /// that actually holds the money completely unprotected — so deleting it would orphan the fee exactly as
    /// before the guard existed, with the marker still there making everything look covered.</para>
    ///
    /// <para>There are <b>three</b> cancellation sites in this codebase and only one of them is
    /// <c>CancelInvoiceCommand</c>; this is what makes the other two safe without a repoint written into each.</para>
    /// </summary>
    [Fact]
    public async Task The_Replacement_Of_A_Corrected_Carried_Note_Is_Guarded_Too()
    {
        var recordId = Guid.NewGuid();
        var supersededNoteId = Guid.NewGuid();          // the cancelled original the marker still names
        var replacement = NoteForFiche(recordId, issued: true, number: "2026-0020");
        CarryingPlan(recordId, supersededNoteId, TreatmentPlanStatus.InProgress, "2026-0012");

        // Nothing names the replacement by id — only the fiche ties them together.
        var result = await Cancel().Handle(
            new CancelInvoiceCommand { Id = replacement.Id, Reason = "erreur" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteCarriedActGuard.CarriedByPlanCode, result.Code);
        Assert.Contains("2026-0012", result.Error);
        Assert.NotEqual(InvoiceStatus.Cancelled, replacement.Status);
    }

    /// <summary>The same, through the delete door — a replacement raised as a draft.</summary>
    [Fact]
    public async Task Deleting_The_Replacement_Of_A_Corrected_Carried_Note_Is_Refused()
    {
        var recordId = Guid.NewGuid();
        var replacement = NoteForFiche(recordId, issued: false);
        CarryingPlan(recordId, Guid.NewGuid(), TreatmentPlanStatus.Accepted, "2026-0012");

        var result = await Delete().Handle(
            new DeleteInvoiceCommand { Id = replacement.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _invoices.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// ⚠️ The fiche match must not sweep in an ordinary devis. A plan whose act is linked to the same séance but
    /// carries <b>no marker</b> is a normal plan-carried act — the devis prices it and the note bills nothing of
    /// it — so the note is free to be cancelled.
    /// </summary>
    [Fact]
    public async Task A_Plan_On_The_Same_Fiche_With_No_Marker_Does_Not_Hold_The_Note()
    {
        var recordId = Guid.NewGuid();
        var note = NoteForFiche(recordId, issued: true);

        var ordinary = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Couronne");
        ordinary.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Couronne", 600m, null, new List<int> { 11 }),
        });
        ordinary.Accept("2026-0030");
        _plans.Setup(r => r.GetByLinkedDentalRecordAsync(ClinicId, recordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ordinary });

        var result = await Cancel().Handle(
            new CancelInvoiceCommand { Id = note.Id, Reason = "erreur" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Cancelled, note.Status);
    }

    /// <summary>An ordinary note nothing carries is cancelled and deleted exactly as it always was.</summary>
    [Fact]
    public async Task An_Uncarried_Note_Is_Untouched_By_The_Guard()
    {
        var issued = Note(issued: true);
        var cancelled = await Cancel().Handle(
            new CancelInvoiceCommand { Id = issued.Id, Reason = "erreur" }, CancellationToken.None);
        Assert.True(cancelled.IsSuccess);

        var draft = Note(issued: false);
        var deleted = await Delete().Handle(new DeleteInvoiceCommand { Id = draft.Id }, CancellationToken.None);
        Assert.True(deleted.IsSuccess);
        _invoices.Verify(r => r.DeleteAsync(draft.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
