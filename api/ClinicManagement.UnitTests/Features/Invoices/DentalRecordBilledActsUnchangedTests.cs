using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// « Les actes d'une fiche facturée ne peuvent plus être modifiés » — and until now that sentence was only true
/// of their <b>total</b>.
///
/// <para>
/// <b>The reported defect.</b> <see cref="DentalRecordBillingGuard.Check"/> compared <c>proposedCost</c> against
/// the note's billed total and nothing else, on the stated grounds that <c>SetActs</c> regenerates every act id
/// so there is no before/after identity to diff. True of the ids, and the wrong question: swap « Détartrage »
/// for « Gingivectomie » at the same 60 DT and the guard passed, the fiche was rewritten, and the numbered
/// document went on billing an act the séance no longer records — with a green toast and this guard's own
/// refusal printed nowhere.
/// </para>
///
/// <para>
/// ⚠️ <b>The comparison is between the two sides of one edit, never against the note's stored lines.</b> That
/// was tried first and it is wrong in production: a note raised before <c>DentalRecordInvoiceLines</c> existed,
/// one edited by hand afterwards, or a legacy fiche billed from its <c>ProcedureType</c> summary all carry text
/// this class would no longer compose — so re-dating such a séance, changing nothing, would be refused for ever.
/// `A_Note_Whose_Stored_Lines_Differ_Does_Not_Refuse_An_Unrelated_Edit` is that regression, pinned.
/// </para>
/// </summary>
public class DentalRecordBilledActsUnchangedTests
{
    private static DentalRecordBillingGuard.Snapshot Note(decimal billed = 120m, decimal collected = 120m) =>
        new(Guid.NewGuid(), "2026-0073", InvoiceStatus.Paid, billed, collected, 0m);

    private static IReadOnlyList<DentalRecordInvoiceLines.Line> Lines(
        params (string Designation, int Quantity, decimal UnitPriceHt)[] lines) =>
        lines.Select(l => new DentalRecordInvoiceLines.Line(l.Designation, l.Quantity, l.UnitPriceHt)).ToList();

    /// <summary>The defect itself: same money, different act.</summary>
    [Fact]
    public void Swapping_The_Act_At_The_Same_Price_Is_Refused()
    {
        var before = Lines(("Détartrage (dents 16)", 1, 120m));
        var after = Lines(("Gingivectomie (dents 16)", 1, 120m));

        var result = DentalRecordBillingGuard.Check(Note(), 120m, 120m, before, after);

        Assert.True(result.IsFailure);
        Assert.Equal(DentalRecordBillingRefusals.ActsChangedCode, result.Code);
    }

    /// <summary>
    /// H5: correcting the tooth of a flat act bills the same work — same act, quantity and price — so the save
    /// passes instead of forcing a new note. A different act at the same price is still refused.
    /// </summary>
    [Fact]
    public void Moving_A_Flat_Act_To_Another_Tooth_Passes_But_Another_Act_Does_Not()
    {
        var before = new[] { new DentalRecordInvoiceLines.Line("Détartrage (dents 36)", 1, 120m, "Détartrage") };
        var moved = new[] { new DentalRecordInvoiceLines.Line("Détartrage (dents 46)", 1, 120m, "Détartrage") };
        var swapped = new[] { new DentalRecordInvoiceLines.Line("Gingivectomie (dents 36)", 1, 120m, "Gingivectomie") };

        Assert.True(DentalRecordBillingGuard.Check(Note(), 120m, 120m, before, moved).IsSuccess);
        Assert.Equal(
            DentalRecordBillingRefusals.ActsChangedCode,
            DentalRecordBillingGuard.Check(Note(), 120m, 120m, before, swapped).Code);
    }

    /// <summary>Adding a free act keeps the total and adds a line — still a change to what was billed.</summary>
    [Fact]
    public void Adding_An_Act_Priced_Zero_Is_Refused()
    {
        var result = DentalRecordBillingGuard.Check(
            Note(), 120m, 120m,
            Lines(("Détartrage (dents 16)", 1, 120m)),
            Lines(("Détartrage (dents 16)", 1, 120m), ("Consultation de contrôle", 1, 0m)));

        Assert.Equal(DentalRecordBillingRefusals.ActsChangedCode, result.Code);
    }

    /// <summary>
    /// The ordinary edit this guard must never refuse: the acts are untouched and only the date, the notes or
    /// the payment moved. It is what the three re-dating cases in <c>DentalRecordCorrectionTests</c> exercise.
    /// </summary>
    [Fact]
    public void An_Edit_That_Leaves_The_Acts_Alone_Passes()
    {
        var lines = Lines(("Détartrage (dents 16)", 1, 120m));

        Assert.True(DentalRecordBillingGuard.Check(Note(), 120m, 120m, lines, Lines(("Détartrage (dents 16)", 1, 120m))).IsSuccess);
    }

    /// <summary>
    /// The regression the first shape of this check caused. The note's own stored lines may legitimately say
    /// something else — this guard has no opinion about them.
    /// </summary>
    [Fact]
    public void A_Note_Whose_Stored_Lines_Differ_Does_Not_Refuse_An_Unrelated_Edit()
    {
        // The note was raised by hand as « Soin de carie / obturation », 2 × 60 — nothing this class composes.
        var stored = Note(billed: 120m, collected: 120m);
        var unchanged = Lines(("Soin de carie / obturation (dents 16, 26)", 1, 120m));

        Assert.True(DentalRecordBillingGuard.Check(stored, 120m, 120m, unchanged, unchanged).IsSuccess);
    }

    /// <summary>
    /// `BillDentalRecordCommand` bills the fiche as stored, so it supplies no « after ». Demanding one would
    /// refuse every ordinary billing.
    /// </summary>
    [Fact]
    public void A_Caller_That_Is_Not_Editing_The_Work_Supplies_Neither_Side()
    {
        Assert.True(DentalRecordBillingGuard.Check(Note(), 120m, 120m).IsSuccess);
    }

    /// <summary>
    /// Order is not a change. `Invoice.Lines` has no ordering configured and `SetActs` rebuilds the list, so a
    /// re-read handing them back the other way round would refuse an edit that changed nothing.
    /// </summary>
    [Fact]
    public void The_Same_Lines_In_Another_Order_Are_The_Same_Lines()
    {
        var before = Lines(("Détartrage (dents 16)", 1, 70m), ("Consultation de contrôle", 1, 50m));
        var after = Lines(("Consultation de contrôle", 1, 50m), ("Détartrage (dents 16)", 1, 70m));

        Assert.True(DentalRecordBillingGuard.Check(Note(), 120m, 120m, before, after).IsSuccess);
    }

    /// <summary>
    /// The money check keeps its precedence: it is the cheaper test, the only one a caller with no « after »
    /// can make, and its refusal is the one `DentalRecordCorrectionTests` has always pinned.
    /// </summary>
    [Fact]
    public void A_Changed_Total_Is_Still_Caught_With_No_Lines_Supplied()
    {
        var result = DentalRecordBillingGuard.Check(Note(billed: 180m, collected: 180m), 150m, 180m);

        Assert.Equal(DentalRecordBillingRefusals.ActsChangedCode, result.Code);
    }
}
