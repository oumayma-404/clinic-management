using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// A later séance of a treatment whose money lives on a note d'honoraires collects ON THAT NOTE.
///
/// <para>
/// <b>The case, production 30/09/2026 (devis 2026-0011).</b> Séance 1 was recorded as an ordinary fiche: 160 DT
/// act, 100 paid → note 2026-0070. « C'est la suite d'une séance précédente » then attached that note to a new
/// 160 DT devis. At séance 2 the dentist typed the remaining 60 and the fiche refused it — the treatment field was
/// withheld (the note holds the money) and the séance's own « Payé » cannot exceed its 0 total. He had to leave
/// the fiche for the devis' « Encaisser ».
/// </para>
/// </summary>
public class CollectOnBridgedNoteTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid PatientId = Guid.NewGuid();
    private static readonly DateTime Seance1 = new(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Seance2 = new(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);

    private readonly DentalRecord _fiche1 = new(Guid.NewGuid(), PatientId, ClinicId, Seance1, 160m, true);
    private readonly DentalRecord _fiche2 = new(Guid.NewGuid(), PatientId, ClinicId, Seance2, 0m, true);
    private readonly TreatmentPlan _plan;
    private readonly Invoice _note;

    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public CollectOnBridgedNoteTests()
    {
        _plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Prothèse amovible");
        _plan.SetItems(new[] { ("Prothèse amovible", 160m, (IReadOnlyList<int>)new[] { 11 }) });
        _plan.Accept("2026-0011");

        _note = new Invoice(Guid.NewGuid(), ClinicId, PatientId, dentalRecordId: _fiche1.Id);
        _note.SetLines(new[] { ("Prothèse amovible", 1, 160m, (Guid?)_fiche1.Id) });
        _note.Issue("2026-0070");
        _note.RecordPayment(80m, PaymentMethod.Cash, Seance1);
        _note.RecordPayment(20m, PaymentMethod.Cash, Seance1);
        _note.AttachToTreatmentPlan(_plan.Id);

        _plans.Setup(r => r.GetByIdAsync(_plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_plan);
        _records.Setup(r => r.GetByIdAsync(_fiche1.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_fiche1);
        _records.Setup(r => r.GetByIdAsync(_fiche2.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_fiche2);
        _invoices.Setup(r => r.GetByIdAsync(_note.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_note);
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new[]
            {
                (_plan.Id, _note.Id, _note.Number, _note.Status, _note.TotalTtc, _note.Outstanding),
            });
    }

    private Task<Result<TreatmentCollectionResult>> Collect(DentalRecord fiche, decimal amount)
    {
        var clinic = new Mock<ICurrentClinicResolver>();
        clinic.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));
        var handler = new CollectOnTreatmentCommandHandler(
            _plans.Object, new Mock<IProcedureTypeRepository>().Object, _records.Object, _invoices.Object,
            clinic.Object, _unitOfWork.Object, NullLogger<CollectOnTreatmentCommandHandler>.Instance);
        return handler.Handle(new CollectOnTreatmentCommand
        {
            TreatmentPlanId = _plan.Id, DentalRecordId = fiche.Id, Amount = amount, Method = "Cash",
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_Second_Seance_Collects_The_Rest_On_The_Note()
    {
        var result = await Collect(_fiche2, 60m);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(TreatmentCollectionOutcome.Collected, result.Value!.Outcome);
        Assert.Equal("2026-0070", result.Value.NoteNumber);
        Assert.Equal(60m, result.Value.AmountCollected);
        Assert.Equal(0m, result.Value.Outstanding);

        Assert.Equal(InvoiceStatus.Paid, _note.Status);
        Assert.Equal(60m, _note.CollectedOnRecord(_fiche2.Id));
        // Never on the échéancier: a bridged devis' échéance would be money no read counts.
        Assert.Equal(0m, _plan.AmountPaid);
    }

    [Fact]
    public async Task Re_Saving_The_Seance_Takes_Nothing_More()
    {
        await Collect(_fiche2, 60m);
        var again = await Collect(_fiche2, 60m);

        Assert.True(again.IsSuccess);
        Assert.Equal(TreatmentCollectionOutcome.AlreadyCollected, again.Value!.Outcome);
        Assert.Equal(160m, _note.AmountCollected);
    }

    [Fact]
    public async Task A_Collection_Is_Not_Lowered_By_Retyping_It()
    {
        await Collect(_fiche2, 60m);
        var lowered = await Collect(_fiche2, 40m);

        Assert.True(lowered.IsFailure);
        Assert.Equal(TreatmentCollectionRefusals.CollectionLoweredCode, lowered.Code);
        Assert.Contains("note n° 2026-0070", lowered.Error);
    }

    [Fact]
    public async Task More_Than_The_Note_Awaits_Is_Refused_And_Nothing_Is_Recorded()
    {
        var result = await Collect(_fiche2, 70m);

        Assert.True(result.IsFailure);
        Assert.Equal(TreatmentCollectionRefusals.ExceedsOutstandingCode, result.Code);
        Assert.Equal(100m, _note.AmountCollected);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Notes_Own_Fiche_Collects_Through_Its_Own_Paye_Not_Here()
    {
        var result = await Collect(_fiche1, 60m);

        Assert.True(result.IsSuccess);
        Assert.Equal(TreatmentCollectionOutcome.NotCollected, result.Value!.Outcome);
        Assert.Equal(100m, _note.AmountCollected);
    }

    // ── The first séance's fiche must not be blamed for money it did not take ─────────────────────

    private DentalRecordBillingGuard.Snapshot SnapshotFor(DentalRecord fiche) => new(
        _note.Id, _note.Number, _note.Status, 160m, _note.AmountCollected, 0m,
        _note.CollectedExcludingOtherRecords(fiche.Id));

    [Fact]
    public async Task Re_Saving_Seance_One_Unchanged_After_Seance_Two_Collected_Is_Allowed()
    {
        await Collect(_fiche2, 60m);

        var allowed = DentalRecordBillingGuard.Check(SnapshotFor(_fiche1), 160m, 100m, storedAmountPaid: 100m);

        Assert.True(allowed.IsSuccess, allowed.Error);
    }

    [Fact]
    public void Re_Saving_Seance_One_Unchanged_After_Money_Taken_At_The_Desk_Is_Allowed()
    {
        // The route the dentist actually took: « Encaisser » on the devis, i.e. an untagged payment on the note.
        // Comparing the fiche's 100 with the note's 160 refused the save and offered « Corriger la note », which
        // would have voided those 60 DT.
        _note.RecordPayment(60m, PaymentMethod.Cash, Seance2);

        var allowed = DentalRecordBillingGuard.Check(SnapshotFor(_fiche1), 160m, 100m, storedAmountPaid: 100m);

        Assert.True(allowed.IsSuccess, allowed.Error);
    }

    [Fact]
    public void Lowering_Seance_Ones_Own_Figure_Is_Still_Refused()
    {
        var refused = DentalRecordBillingGuard.Check(SnapshotFor(_fiche1), 160m, 80m, storedAmountPaid: 100m);

        Assert.True(refused.IsFailure);
        Assert.Equal(DentalRecordBillingRefusals.PaymentLoweredCode, refused.Code);
    }

    // ── The first séance's own act is priced on its note, never zeroed by the devis link ──────────

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 160)]
    public async Task Seance_Ones_Act_Keeps_Its_Fee_When_Its_Own_Note_Represents_The_Devis(bool ownNote, int expected)
    {
        var acts = new List<DentalRecordActInput>
        {
            new(null, "Prothèse amovible", 160m, 160m, false, new[] { 11 }, null, null, null),
        };

        var imposed = await PlanCarriedActPricing.ImposeAsync(
            _plans.Object, acts, _plan.Id, _plan.Items.Single().Id, ClinicId,
            NullLogger.Instance, CancellationToken.None, billedOnANoteRepresentingThePlan: ownNote);

        Assert.Null(imposed.Refusal);
        Assert.Equal(expected, imposed.Acts.Single().Cost);
    }

    // ── Deleting the second séance gives its money back, and keeps the first séance's note ────────

    [Fact]
    public async Task Deleting_Seance_Two_Voids_Its_Collection_And_Keeps_The_Note()
    {
        await Collect(_fiche2, 60m);

        _plans.Setup(r => r.GetByCollectedDentalRecordAsync(ClinicId, _fiche2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TreatmentPlan>());
        _invoices.Setup(r => r.GetByDentalRecordAsync(ClinicId, _fiche2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Invoice>());
        _invoices.Setup(r => r.GetCollectedOnByDentalRecordAsync(ClinicId, _fiche2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { _note });
        var credits = new Mock<ICreditNoteRepository>();
        credits.Setup(r => r.GetTotalForInvoiceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0m);

        var reversal = await DentalRecordDeletionReversal.InspectAsync(
            _plans.Object, _invoices.Object, credits.Object, ClinicId, _fiche2, CancellationToken.None);
        Assert.False(reversal.IsRefused);
        Assert.Equal(60m, reversal.TotalReversed);

        await DentalRecordDeletionReversal.ApplyAsync(
            reversal, _plans.Object, _invoices.Object, Seance2, "local|x", "Dr Hamdane", CancellationToken.None);

        Assert.Equal(100m, _note.AmountCollected);
        Assert.Equal(0m, _note.CollectedOnRecord(_fiche2.Id));
        Assert.NotEqual(InvoiceStatus.Cancelled, _note.Status);
    }
}
