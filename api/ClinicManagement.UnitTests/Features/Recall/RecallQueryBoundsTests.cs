using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Recall.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Recall;

/// <summary>
/// The bounded « patients à relancer » read (AC-P4.41–4.43). The query used to load <b>every</b> patient and
/// <b>every</b> appointment in the clinic and derive everything in memory (§ 9.6); the filters now live in SQL.
///
/// <para>The load is a performance change, so the thing that actually needs pinning is that it is <b>only</b> a
/// performance change (AC-P4.42). The exact rule is <c>anchor.AddMonths(interval) &lt;= now</c>, and it cannot
/// be inverted into a SQL comparison without changing who qualifies, because <c>AddMonths</c> clamps to the end
/// of a shorter month. So the repository is given a deliberately wide bound and the exact test stays here — and
/// <see cref="Widened_Bound_Never_Excludes_A_Patient_Who_Is_Actually_Due"/> proves the bound is a superset for
/// every date and every interval the clinic can configure, which is the claim the whole design rests on.</para>
/// </summary>
public class RecallQueryBoundsTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    private GetPatientsToRecallQueryHandler Handler() =>
        new(_patients.Object, _clinics.Object, _plans.Object, _invoices.Object, _clinicResolver.Object);

    private void Authenticated(int intervalMonths)
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        var clinic = new Clinic(ClinicId, "Cabinet Test");
        clinic.SetRecallIntervalMonths(intervalMonths);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);

        // No devis and no money in these fixtures: this class is about the OverdueVisit reason's date arithmetic,
        // so every other reason is wired empty to keep it the only thing that can put a patient on the list.
        _plans.Setup(r => r.GetRecallPlanFactsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RecallPlanFact>());
        _plans.Setup(r => r.GetInstallmentOutstandingByPatientAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, decimal, DateTime?)>());
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, ClinicManagement.Domain.Enums.InvoiceStatus)>());
    }

    private void CandidatesAre(params RecallCandidate[] candidates) =>
        _patients.Setup(r => r.GetRecallCandidatesAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

    private static RecallCandidate Candidate(DateTime anchorUtc, DateTime? lastVisitUtc = null, string last = "Dupont") =>
        new(Guid.NewGuid(), "Jean", last, "+21620123456", anchorUtc, lastVisitUtc, null, null);

    // ---- AC-P4.42: the bound is a superset, for every date and every configurable interval --------------

    // The one property the whole design depends on: if a patient IS due under the exact rule, the widened SQL
    // bound must let their row through. AddMonths clamps at most three days (31 → 28), so adding three days back
    // covers it — but "at most three" is an argument, and this is the check. Every day across four years
    // (including two Februaries and a leap year) × every interval a clinic can set.
    [Fact]
    public void Widened_Bound_Never_Excludes_A_Patient_Who_Is_Actually_Due()
    {
        var failures = new List<string>();

        for (var now = new DateTime(2024, 1, 1, 9, 30, 0, DateTimeKind.Utc);
             now < new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc);
             now = now.AddDays(1))
        {
            // The bound the handler hands to SQL, and the exact test it applies afterwards.
            for (var interval = 1; interval <= 60; interval++)
            {
                var bound = now.AddMonths(-interval).AddDays(3);

                // Probe anchors around the bound: anything ≤ bound reaches the handler, so a violation can only
                // be an anchor ABOVE the bound that is nonetheless due.
                for (var offsetDays = 1; offsetDays <= 5; offsetDays++)
                {
                    var anchor = bound.AddDays(offsetDays);
                    var isActuallyDue = anchor.AddMonths(interval) <= now;

                    if (isActuallyDue)
                    {
                        failures.Add(
                            $"now={now:O} interval={interval} anchor={anchor:O} is due but falls outside bound {bound:O}");
                    }
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "The SQL anchor bound would drop patients who are genuinely due, so the relance list would silently "
            + "lose rows — a behaviour change, which AC-P4.42 forbids:\n  "
            + string.Join("\n  ", failures.Take(10)));
    }

    // The other half of "identical results": the margin must not ADD anyone. A candidate the widened bound let
    // through, but whose due date has not arrived, is dropped by the handler.
    [Fact]
    public async Task A_Candidate_Inside_The_Margin_But_Not_Yet_Due_Is_Excluded()
    {
        Authenticated(intervalMonths: 6);
        var now = DateTime.UtcNow;

        // Anchor two days short of the interval: inside the three-day margin, not due.
        CandidatesAre(Candidate(now.AddMonths(-6).AddDays(2)));

        var result = await Handler().Handle(new GetPatientsToRecallQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    // ---- The bound handed to SQL ------------------------------------------------------------------------

    // AC-P4.41 — the read is bounded by date, and by the clinic's OWN interval rather than a fixed window.
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(24)]
    public async Task Passes_The_Interval_Derived_Bound_To_The_Repository(int intervalMonths)
    {
        Authenticated(intervalMonths);
        CandidatesAre();

        DateTime? captured = null;
        _patients.Setup(r => r.GetRecallCandidatesAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, DateTime, IReadOnlyCollection<Guid>?, CancellationToken>(
                (_, bound, _, _, _) => captured = bound)
            .ReturnsAsync(Array.Empty<RecallCandidate>());

        await Handler().Handle(new GetPatientsToRecallQuery(), CancellationToken.None);

        Assert.NotNull(captured);
        var expected = DateTime.UtcNow.AddMonths(-intervalMonths).AddDays(3);
        Assert.Equal(expected, captured!.Value, TimeSpan.FromSeconds(10));
    }

    // AC-P4.43 — archived patients stay excluded. The exclusion moved into SQL, so what this asserts is that the
    // handler goes through the bounded projection and no longer touches `GetByClinicIdAsync`, whose
    // `includeArchived` flag is the only way an archived patient could reappear here.
    [Fact]
    public async Task Never_Reads_Patients_Through_The_Archived_Inclusive_Path()
    {
        Authenticated(intervalMonths: 6);
        CandidatesAre();

        await Handler().Handle(new GetPatientsToRecallQuery(), CancellationToken.None);

        _patients.Verify(
            r => r.GetRecallCandidatesAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _patients.Verify(
            r => r.GetByClinicIdAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _patients.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Mapping ---------------------------------------------------------------------------------------

    // The due date is derived from the anchor, `daysOverdue` from the due date, and a never-seen patient (no
    // completed visit) still gets a due date — from their creation date, which is what the anchor already is.
    [Fact]
    public async Task Maps_Due_Date_Overdue_Days_And_A_Never_Seen_Patient()
    {
        Authenticated(intervalMonths: 6);
        var now = DateTime.UtcNow;
        var lastVisit = now.AddMonths(-8);

        CandidatesAre(
            Candidate(lastVisit, lastVisit, last: "Vu"),
            Candidate(now.AddMonths(-13), lastVisitUtc: null, last: "JamaisVu"));

        var result = await Handler().Handle(new GetPatientsToRecallQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rows = result.Value!.Items.ToList();
        Assert.Equal(2, rows.Count);

        var seen = rows.Single(r => r.PatientName.EndsWith("Vu", StringComparison.Ordinal) && r.LastVisitDate != null);
        Assert.Equal(lastVisit.AddMonths(6), seen.DueDate);
        Assert.Equal(Math.Max(0, (now.Date - lastVisit.AddMonths(6).Date).Days), seen.DaysOverdue);

        var neverSeen = rows.Single(r => r.LastVisitDate == null);
        Assert.True(neverSeen.DueDate <= now); // still due — the anchor fell back to CreatedAt
        Assert.Equal("Jean JamaisVu", neverSeen.PatientName);
    }

    // Most overdue first, then by name — the order the relance list is read in.
    [Fact]
    public async Task Orders_Most_Overdue_First()
    {
        Authenticated(intervalMonths: 6);
        var now = DateTime.UtcNow;

        CandidatesAre(
            Candidate(now.AddMonths(-7), last: "Recent"),
            Candidate(now.AddMonths(-30), last: "TresEnRetard"));

        var result = await Handler().Handle(new GetPatientsToRecallQuery(), CancellationToken.None);

        var rows = result.Value!.Items.ToList();
        Assert.Equal("Jean TresEnRetard", rows[0].PatientName);
        Assert.Equal("Jean Recent", rows[1].PatientName);
    }
}
