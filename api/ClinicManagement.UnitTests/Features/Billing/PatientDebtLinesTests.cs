using ClinicManagement.Application.Features.Billing;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Billing;

/// <summary>
/// The decomposition of « Solde patient » — the rows behind the one figure the patient file shows.
///
/// <para>These cases are about the <b>projection</b>, not about the de-duplication: by the time
/// <see cref="PatientDebtLines.Project"/> runs, the caller has already applied
/// <c>PlanBillingRules.CarriesDebt</c> and <c>BilledPlanIds</c>. That split is what keeps a Draft devis and a
/// bridged one out of here with no second copy of either rule — and it is why the <i>agreement</i> between the
/// rows and the total is asserted in <c>MoneyReadConsistencyTests</c>, over the fixtures that exercise those
/// rules, rather than restated here.</para>
///
/// <para>⚠️ Every state below is built the way <b>production</b> reaches it, which mattered more than expected:
/// <c>SetInstallments</c> is Draft-only, <c>Accept</c> raises a lump-sum row only when the échéancier is empty,
/// and <c>ReviseInstallments</c> refuses both an empty schedule and one that does not sum to the plan total. So
/// « the échéancier cannot take what the devis owes » is unreachable through those three doors and is reached the
/// way <c>reconcile-money</c>'s <c>plan-schedule-balances</c> says it is — by <c>AddItems</c> raising
/// <c>TotalPlanned</c> without touching the schedule. A fixture that forced the state another way would be
/// testing a shape the product cannot produce.</para>
/// </summary>
public class PatientDebtLinesTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>Mid-September, so a 1 September échéance is a day past and an October one is not.</summary>
    private static readonly DateTime ClinicToday = new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Sept1 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Oct1 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Invoice IssuedNote(decimal total, string number = "2026-0042", params string[] acts)
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        var names = acts.Length == 0 ? new[] { "Détartrage" } : acts;
        invoice.SetLines(names.Select(a => (a, 1, total / names.Length)).ToArray());
        invoice.Issue(number);
        return invoice;
    }

    /// <summary>
    /// An accepted devis. ⚠️ The échéancier is set <b>before</b> <c>Accept</c> because <c>SetInstallments</c> is
    /// Draft-only, and <c>Accept</c> raises its lump-sum row only when none exists — so passing a schedule here
    /// is the one way to get an accepted plan with hand-typed échéances, exactly as the create command does it.
    /// Passing none leaves the auto-raised lump-sum row, which is the common case.
    /// </summary>
    private static TreatmentPlan AcceptedDevis(
        decimal total,
        string number = "2026-0014",
        params (DateTime dueDate, decimal amount)[] schedule)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Réhabilitation");
        plan.SetItems(new[] { ("Couronne", total, (IReadOnlyList<int>)new[] { 11 }) });
        if (schedule.Length > 0)
        {
            plan.SetInstallments(schedule);
        }
        plan.Accept(number);
        return plan;
    }

    private static List<Invoice> NoNotes() => new();
    private static List<TreatmentPlan> NoDevis() => new();

    // A note and an independent devis are two rows, and their sum is the balance the header shows.
    [Fact]
    public void A_Note_And_An_Independent_Devis_Are_Two_Rows_Summing_To_The_Balance()
    {
        var note = IssuedNote(150m);
        var devis = AcceptedDevis(90m);

        var lines = PatientDebtLines.Project(new[] { note }, new[] { devis }, ClinicToday);

        Assert.Equal(2, lines.Count);
        Assert.Equal(240m, lines.Sum(l => l.Outstanding));
        Assert.Contains(lines, l => l.Kind == PatientDebtLines.InvoiceKind && l.Number == "2026-0042");
        Assert.Contains(lines, l => l.Kind == PatientDebtLines.TreatmentPlanKind && l.Number == "2026-0014");
    }

    // A settled note is not a row. « Reste 0,000 DT » is a line that trains the eye to skip lines.
    [Fact]
    public void A_Settled_Note_Is_Not_Listed()
    {
        var note = IssuedNote(150m);
        note.RecordPayment(note.TotalTtc, PaymentMethod.Cash, ClinicToday.AddDays(-1));

        Assert.Empty(PatientDebtLines.Project(new[] { note }, NoDevis(), ClinicToday));
    }

    // A partly-paid note shows what is LEFT, with what was collected beside it.
    [Fact]
    public void A_Partly_Paid_Note_Shows_The_Remainder()
    {
        var note = IssuedNote(150m);
        var ttc = note.TotalTtc;
        note.RecordPayment(60m, PaymentMethod.Cash, ClinicToday.AddDays(-1));

        var line = Assert.Single(PatientDebtLines.Project(new[] { note }, NoDevis(), ClinicToday));

        Assert.Equal(ttc - 60m, line.Outstanding);
        Assert.Equal(60m, line.Collected);
        Assert.Equal(ttc, line.Total);
    }

    // ⚠️ A note is NEVER « en retard ». It is payable on issue, so a lateness derived from its own date would
    // fire on every unpaid note the day after it was raised — the shape that made 25 of 27 échéances read
    // « En retard » before `InstallmentLateness` existed. It carries its age instead.
    [Fact]
    public void A_Note_Is_Never_Reported_Overdue_However_Old()
    {
        var note = IssuedNote(150m);

        var line = Assert.Single(PatientDebtLines.Project(new[] { note }, NoDevis(), ClinicToday));

        Assert.False(line.IsOverdue);
        Assert.NotNull(line.Since);
    }

    // A devis row carries the PLAN's outstanding — the figure « Solde patient » sums — and points at the
    // oldest unpaid échéance, which is what « Encaisser » targets.
    [Fact]
    public void A_Devis_Row_Targets_Its_Oldest_Unpaid_Echeance()
    {
        var devis = AcceptedDevis(1000m, "2026-0014", (Oct1, 600m), (Sept1, 400m));

        var line = Assert.Single(PatientDebtLines.Project(NoNotes(), new[] { devis }, ClinicToday));

        Assert.Equal(1000m, line.Outstanding);
        Assert.Equal(1000m, line.PayableRoom);
        Assert.Equal(Sept1, line.Since);
        Assert.Equal(devis.Installments.OrderBy(i => i.DueDate).First().Id, line.PayableInstallmentId);
    }

    // A hand-typed échéance whose day has passed is late. `InstallmentLateness` is the one authority; this only
    // checks that the row asks it.
    [Fact]
    public void A_Past_Due_Hand_Typed_Echeance_Is_Reported_Late()
    {
        var devis = AcceptedDevis(1000m, "2026-0014", (Sept1, 1000m));

        var line = Assert.Single(PatientDebtLines.Project(NoNotes(), new[] { devis }, ClinicToday));

        Assert.True(line.IsOverdue);
    }

    // ⚠️ The auto-raised lump-sum échéance `Accept` writes is a ledger container, not a date anybody promised —
    // so a devis with unrealised work is not late on it, however long ago it was signed.
    [Fact]
    public void The_Auto_Raised_Echeance_Of_Unfinished_Work_Is_Not_Late()
    {
        var devis = AcceptedDevis(1000m);

        var line = Assert.Single(PatientDebtLines.Project(NoNotes(), new[] { devis }, ClinicToday));

        Assert.False(line.IsOverdue);
    }

    // ⚠️ An échéancier that no longer sums to its plan total can accept LESS than the row owes —
    // `Installment.RecordPayment` is bounded by one échéance's own room. Both figures are carried so the row
    // can offer the payment the échéancier will actually take instead of a refusal for the number it printed.
    [Fact]
    public void A_Short_Echeancier_Reports_Less_Room_Than_The_Devis_Owes()
    {
        var devis = AcceptedDevis(1000m, "2026-0014", (Oct1, 1000m));
        // The reachable route: an amendment raises TotalPlanned and leaves the schedule alone.
        devis.AddItems(new[] { ("Greffe osseuse", 200m, (IReadOnlyList<int>)new[] { 12 }) });

        var line = Assert.Single(PatientDebtLines.Project(NoNotes(), new[] { devis }, ClinicToday));

        Assert.Equal(1200m, line.Outstanding);
        Assert.Equal(1000m, line.PayableRoom);
        Assert.NotNull(line.PayableInstallmentId);
    }

    // ⚠️ The same drift taken one step further: every échéance is settled, so the devis owes the added act and
    // has NOWHERE to put the money. The row must say so rather than render a dead « Encaisser ».
    [Fact]
    public void A_Devis_Whose_Echeances_Are_All_Paid_Owes_The_Added_Act_With_No_Payment_Target()
    {
        var devis = AcceptedDevis(1000m, "2026-0014", (Oct1, 1000m));
        devis.RecordInstallmentPayment(
            devis.Installments.Single().Id, 1000m, PaymentMethod.Cash, ClinicToday.AddDays(-2));
        devis.AddItems(new[] { ("Greffe osseuse", 200m, (IReadOnlyList<int>)new[] { 12 }) });

        var line = Assert.Single(PatientDebtLines.Project(NoNotes(), new[] { devis }, ClinicToday));

        Assert.Equal(200m, line.Outstanding);
        Assert.Equal(0m, line.PayableRoom);
        Assert.Null(line.PayableInstallmentId);
        Assert.Null(line.Since);
        Assert.False(line.IsOverdue);
    }

    // A fully-settled devis is not a row, for the note's reason.
    [Fact]
    public void A_Settled_Devis_Is_Not_Listed()
    {
        var devis = AcceptedDevis(1000m, "2026-0014", (Oct1, 1000m));
        devis.RecordInstallmentPayment(
            devis.Installments.Single().Id, 1000m, PaymentMethod.Cash, ClinicToday.AddDays(-2));

        Assert.Empty(PatientDebtLines.Project(NoNotes(), new[] { devis }, ClinicToday));
    }

    // The acts are named, de-duplicated, and the tail is counted rather than dropped in silence.
    [Fact]
    public void Covers_Names_The_Acts_Deduplicated_And_Counts_The_Tail()
    {
        var note = IssuedNote(400m, "2026-0050", "Composite", "Composite", "Détartrage", "Couronne", "Extraction");

        var line = Assert.Single(PatientDebtLines.Project(new[] { note }, NoDevis(), ClinicToday));

        Assert.Equal("Composite, Détartrage, Couronne +1 autre", line.Covers);
    }

    // Oldest debt first, and an undated row LAST — a null `Since` is « we cannot say », not « the beginning of
    // time », so sorting it to the top would put the least-known row above the most overdue one.
    [Fact]
    public void Rows_Are_Oldest_First_With_The_Undated_Last()
    {
        var recent = IssuedNote(50m, "2026-0060");

        var undated = AcceptedDevis(70m, "2026-0015", (Oct1, 70m));
        undated.RecordInstallmentPayment(
            undated.Installments.Single().Id, 70m, PaymentMethod.Cash, ClinicToday.AddDays(-2));
        undated.AddItems(new[] { ("Greffe osseuse", 30m, (IReadOnlyList<int>)new[] { 12 }) });

        var old = AcceptedDevis(90m, "2026-0016", (new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 90m));

        var lines = PatientDebtLines.Project(new[] { recent }, new[] { undated, old }, ClinicToday);

        Assert.Equal(new[] { "2026-0016", "2026-0060", "2026-0015" }, lines.Select(l => l.Number).ToArray());
    }

    // A patient who owes nothing gets no rows at all — the band is absent, not a « 0,000 DT » heading.
    [Fact]
    public void Nothing_Owed_Is_No_Rows()
    {
        Assert.Empty(PatientDebtLines.Project(NoNotes(), NoDevis(), ClinicToday));
    }
}
