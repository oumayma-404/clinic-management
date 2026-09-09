using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// A treatment followed without a devis — « Suivre ce traitement » creates an un-numbered <c>Draft</c> — is
/// clinically live and financially inert, and this pins both halves of that.
///
/// <para>
/// Every test here corresponds to a defect that shipped: the status set was written out by hand in a place the
/// frontend guard could not see, the « leave a Draft alone » guard was applied to one of four writers, and the
/// correction gate excluded a state that had since become correctable. None of them raised an error anywhere.
/// </para>
/// </summary>
public class FollowedTreatmentLifecycleTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Today = new(2026, 9, 6, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>A followed treatment: one act, two séances, no number — what `StartTreatmentCommand` produces.</summary>
    private static TreatmentPlan FollowedTreatment(decimal total = 250m)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Couronne");
        plan.SetItems(new[] { ("Couronne", total, (IReadOnlyList<int>)new List<int> { 26 }) });
        var item = plan.Items.Single();
        plan.SetItemSteps(item.Id, new[]
        {
            new TreatmentPlanItemStepInput(null, "Préparation", 45, null),
            new TreatmentPlanItemStepInput(null, "Scellement", 30, 14),
        });
        return plan;
    }

    // ---------------------------------------------------------------------------------------------------------
    // The one live-treatment test
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A Draft is live. This is the whole point of « Suivre ce traitement », and the rule the SQL behind
    /// « Traitements en cours » was not told about — so a treatment started minutes earlier was absent from the
    /// list it belongs to, with the act's own status correctly `InProgress` all along.
    /// </summary>
    [Theory]
    [InlineData(TreatmentPlanStatus.Draft, true)]
    [InlineData(TreatmentPlanStatus.Accepted, true)]
    [InlineData(TreatmentPlanStatus.InProgress, true)]
    [InlineData(TreatmentPlanStatus.Completed, false)]
    [InlineData(TreatmentPlanStatus.Cancelled, false)]
    // ⚠️ `Stopped` was appended after this Theory was written, and its absence here is exactly how the rule got
    // out of date silently: `IsLive` read « not Cancelled and not Completed », so the new member came back TRUE
    // and this table went on passing. `TreatmentPlanStatusCoverageTests` is the guard that cannot be out of date
    // this way — it enumerates the enum — but the case belongs here too, beside the ones it sits with.
    [InlineData(TreatmentPlanStatus.Stopped, false)]
    public void IsLive_admits_everything_that_is_not_finished(TreatmentPlanStatus status, bool expected) =>
        Assert.Equal(expected, TreatmentPlanLifecycle.IsLive(status));

    /// <summary>
    /// The materialised set the SQL filter uses and the predicate the rest of the code uses are the same answer.
    /// They are two members precisely because a query filter cannot call a method, which is how they could drift.
    /// </summary>
    [Fact]
    public void LiveStatuses_is_exactly_the_set_IsLive_admits()
    {
        var fromPredicate = Enum.GetValues<TreatmentPlanStatus>()
            .Where(TreatmentPlanLifecycle.IsLive)
            .ToHashSet();

        Assert.Equal(fromPredicate, TreatmentPlanLifecycle.LiveStatuses.ToHashSet());
    }

    /// <summary>
    /// The derived guard, and the reason this file exists at all.
    ///
    /// <para>
    /// `check:responsive`'s N23 holds the same rule against the four <c>.tsx</c> writers — and scans <c>.tsx</c>
    /// only, so the fifth writer (the « Traitements en cours » projection, in C#) was structurally beyond it and
    /// stayed wrong for the life of the feature. This is that scan on this side of the wire.
    /// </para>
    /// </summary>
    [Fact]
    public void No_source_file_writes_the_live_treatment_test_by_hand()
    {
        var root = SolutionRoot();
        // The hand-written shape, in either order: `Status == Accepted || … == InProgress`, however spelled.
        var handWritten = new Regex(
            @"==\s*TreatmentPlanStatus\.(Accepted|InProgress)\s*\|\|[^\r\n]*==\s*TreatmentPlanStatus\.(Accepted|InProgress)",
            RegexOptions.Compiled);

        /*
         * ⚠️ The red proof, run here rather than left to a reviewer deleting a call by hand: the pattern must
         * actually match the literal it exists to forbid. A derived guard fails OPEN — a regex that compiles and
         * matches nothing passes for ever while checking nothing, which is precisely how the SQL writer survived
         * N23 for the life of the feature.
         */
        Assert.Matches(
            handWritten,
            "where plan.Status == TreatmentPlanStatus.Accepted || plan.Status == TreatmentPlanStatus.InProgress");

        var scanned = 0;
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/") || relative.Contains("/bin/") || relative.Contains("/Migrations/"))
            {
                continue;
            }
            // The entity states the transitions themselves (`UpdateDetails`, `Complete`, `StopTreatment` each
            // list the states they accept, with Draft named alongside), the rule's own home, and this scan.
            if (relative.EndsWith("Entities/TreatmentPlan.cs")
                || relative.EndsWith("Services/TreatmentPlanLifecycle.cs")
                || relative.EndsWith("Domain/FollowedTreatmentLifecycleTests.cs"))
            {
                continue;
            }

            scanned++;
            var source = File.ReadAllText(file);
            if (handWritten.IsMatch(source))
            {
                offenders.Add(relative);
            }
        }

        // « Found nothing » must not be able to read as « nothing was wrong »: a wrong root, a renamed folder or a
        // too-eager exclusion would all leave this test green while scanning an empty set.
        Assert.True(scanned > 500, $"Only {scanned} C# files were scanned from '{root}' — the guard scanned almost nothing.");

        Assert.True(
            offenders.Count == 0,
            "These files write the live-treatment test by hand instead of calling "
            + "`TreatmentPlanLifecycle.IsLive` / `LiveStatuses`, so an un-numbered followed treatment silently "
            + $"drops out of them:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    // ---------------------------------------------------------------------------------------------------------
    // A Draft never acquires a status that carries debt
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>Recording a séance advances the ACT and leaves the treatment un-quoted.</summary>
    [Fact]
    public void Recording_a_seance_on_a_followed_treatment_leaves_it_a_draft()
    {
        var plan = FollowedTreatment();
        var item = plan.Items.Single();

        plan.MarkItemStepDone(item.Id, item.Steps.First().Id, Today, Guid.NewGuid());

        Assert.Equal(TreatmentPlanStatus.Draft, plan.Status);
        Assert.Null(plan.Number);
        Assert.Equal(TreatmentPlanItemStatus.InProgress, plan.Items.Single().Status);
        Assert.False(PlanBillingRules.CarriesDebt(plan.Status));
    }

    /// <summary>
    /// Detaching a mis-attached step from a followed treatment is possible — <c>EnsureCorrectable</c> excluded
    /// <c>Draft</c> on the grounds that it « has no realised act to undo », which stopped being true the moment a
    /// Draft could record séances — and it does not promote the plan on the way.
    /// </summary>
    [Fact]
    public void A_step_recorded_on_a_followed_treatment_can_be_detached()
    {
        var plan = FollowedTreatment();
        var item = plan.Items.Single();
        var step = item.Steps.First();
        plan.MarkItemStepDone(item.Id, step.Id, Today, Guid.NewGuid());

        var undone = plan.UnmarkItemStep(item.Id, step.Id);

        Assert.True(undone);
        Assert.Equal(TreatmentPlanStatus.Draft, plan.Status);
        Assert.Null(plan.Number);
    }

    /// <summary>
    /// The reachable one, and the worst: « Arrêter le traitement » admits a Draft, so stop-then-reopen turned a
    /// followed treatment into an <c>Accepted</c> devis with a null number — a live créance for a total nobody
    /// ever quoted, with no error and nothing on screen saying so.
    /// <para>
    /// ⚠️ The intermediate status is <c>Stopped</c> since 2026-09-09, not <c>Completed</c>. The assertion was
    /// updated rather than dropped because the transition it pins — a stop must not promote an un-numbered
    /// treatment on the way back — is the same one, and it now also pins the new status against a regression to
    /// the old collision.
    /// </para>
    /// </summary>
    [Fact]
    public void Stopping_then_reopening_a_followed_treatment_leaves_it_a_draft()
    {
        var plan = FollowedTreatment();
        var item = plan.Items.Single();
        plan.MarkItemStepDone(item.Id, item.Steps.First().Id, Today, Guid.NewGuid());

        plan.StopTreatment(Today);
        Assert.Equal(TreatmentPlanStatus.Stopped, plan.Status);
        Assert.NotEqual(TreatmentPlanStatus.Completed, plan.Status);

        plan.Reopen();

        Assert.Equal(TreatmentPlanStatus.Draft, plan.Status);
        Assert.Null(plan.Number);
        Assert.False(PlanBillingRules.CarriesDebt(plan.Status));
    }

    /// <summary>The same three writers on a NUMBERED devis still promote it — the guard is « no number », not « never ».</summary>
    [Fact]
    public void A_numbered_devis_still_reopens_as_a_live_devis()
    {
        var plan = FollowedTreatment();
        plan.Accept("2026-0042");
        var item = plan.Items.Single();
        plan.MarkItemStepDone(item.Id, item.Steps.First().Id, Today, Guid.NewGuid());

        plan.StopTreatment(Today);
        plan.Reopen();

        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);
        Assert.Equal("2026-0042", plan.Number);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Chairside collection
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 150 collected on a 250 treatment leaves 100 — the figure the next séance prefills from, and the one the
    /// plan could not state at all before this existed.
    /// </summary>
    [Fact]
    public void Collecting_at_the_chair_draws_the_treatment_down()
    {
        var plan = FollowedTreatment(total: 250m);
        plan.Accept("2026-0042");
        var recordId = Guid.NewGuid();

        plan.CollectChairside(150m, PaymentMethod.Cash, Today, cheque: null, recordId);

        Assert.Equal(150m, plan.AmountPaid);
        Assert.Equal(100m, plan.Outstanding);
        Assert.Equal(TreatmentPlanStatus.InProgress, plan.Status);
        Assert.Equal(150m, plan.CollectedOnRecord(recordId));
    }

    /// <summary>
    /// What one fiche collected is attributable to that fiche and to no other — this is what lets a re-save post
    /// the difference rather than taking the money a second time.
    /// </summary>
    [Fact]
    public void CollectedOnRecord_answers_per_fiche()
    {
        var plan = FollowedTreatment(total: 250m);
        plan.Accept("2026-0042");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        plan.CollectChairside(150m, PaymentMethod.Cash, Today, cheque: null, first);
        plan.CollectChairside(60m, PaymentMethod.Card, Today.AddDays(14), cheque: null, second);

        Assert.Equal(150m, plan.CollectedOnRecord(first));
        Assert.Equal(60m, plan.CollectedOnRecord(second));
        Assert.Equal(0m, plan.CollectedOnRecord(Guid.NewGuid()));
        Assert.Equal(40m, plan.Outstanding);
    }

    /// <summary>
    /// A payment large enough to close the first échéance runs into the next. Posting the whole amount against
    /// one line would be refused by <c>Installment.RecordPayment</c>'s over-payment guard — on a patient who is
    /// simply paying ahead of a schedule they were quoted.
    /// </summary>
    [Fact]
    public void Collecting_spreads_across_the_echeancier()
    {
        var plan = FollowedTreatment(total: 300m);
        plan.SetInstallments(new[]
        {
            (Today.AddDays(7), 100m),
            (Today.AddDays(37), 100m),
            (Today.AddDays(67), 100m),
        });
        plan.Accept("2026-0042");

        plan.CollectChairside(250m, PaymentMethod.Cash, Today, cheque: null, Guid.NewGuid());

        Assert.Equal(250m, plan.AmountPaid);
        Assert.Equal(50m, plan.Outstanding);
        var byDueDate = plan.Installments.OrderBy(i => i.DueDate).ToList();
        Assert.Equal(100m, byDueDate[0].AmountPaid);
        Assert.Equal(100m, byDueDate[1].AmountPaid);
        Assert.Equal(50m, byDueDate[2].AmountPaid);
    }

    /// <summary>More than the treatment is worth is refused, and nothing is written on the way.</summary>
    [Fact]
    public void Collecting_more_than_is_owed_is_refused()
    {
        var plan = FollowedTreatment(total: 250m);
        plan.Accept("2026-0042");

        Assert.Throws<InvalidOperationException>(
            () => plan.CollectChairside(400m, PaymentMethod.Cash, Today, cheque: null, Guid.NewGuid()));
    }

    /// <summary>
    /// Chairside money reaches la caisse, which is the whole reason collecting issues the devis rather than
    /// letting a Draft hold it: both installment reads filter on <see cref="PlanBillingRules.DebtBearingPlanStatuses"/>,
    /// so a payment on a Draft would sit somewhere the till cannot see.
    /// </summary>
    [Fact]
    public void A_collected_treatment_is_in_a_status_la_caisse_reads()
    {
        var plan = FollowedTreatment(total: 250m);
        plan.Accept("2026-0042");
        plan.CollectChairside(150m, PaymentMethod.Cash, Today, cheque: null, Guid.NewGuid());

        Assert.Contains(plan.Status, PlanBillingRules.DebtBearingPlanStatuses);
    }

    /// <summary>
    /// The solution directory, found from <b>this file's own compile-time path</b> and never from
    /// <c>AppContext.BaseDirectory</c>.
    ///
    /// <para>
    /// ⚠️ The suite is routinely built to a scratch <c>BaseOutputPath</c> outside the repository (the Smart App
    /// Control workaround), so walking up from the run directory climbs the user profile instead — and on Windows
    /// that path contains the <c>Application Data</c> junction, which throws <c>UnauthorizedAccessException</c>
    /// before the scan can even start. `RealtimeResourceResolverTests` and the CSP agreement guard use
    /// <c>[CallerFilePath]</c> for the same reason. It <b>throws</b> rather than skipping when the root cannot be
    /// found: a derived guard that silently scans nothing is worse than no guard at all.
    /// </para>
    /// </summary>
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
