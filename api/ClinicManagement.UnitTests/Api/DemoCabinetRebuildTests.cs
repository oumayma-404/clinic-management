using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// demo-cabinet FR-7 / AC-7.6 / EC-7: a rebuilt cabinet opens only after the checks pass, and any failure before the
/// opening removes it and leaves yesterday's cabinet the one visitors enter.
/// </summary>
public class DemoCabinetRebuildTests
{
    private sealed class Steps : IDemoRebuildSteps
    {
        public static readonly Guid Built = Guid.NewGuid();

        public Guid ClinicId => Built;

        public List<string> Calls { get; } = new();

        public string? FailAt { get; init; }

        public IReadOnlyList<string> CheckFailures { get; init; } = Array.Empty<string>();

        public Task<IReadOnlySet<string>> SchemaBaselineAsync(CancellationToken cancellationToken) =>
            Do("baseline", (IReadOnlySet<string>)new HashSet<string>());

        public Task ProvisionAsync(CancellationToken cancellationToken) => Do("provision", 0);

        public Task BuildAsync(CancellationToken cancellationToken) => Do("build", 0);

        public Task<IReadOnlyList<string>> CheckAsync(IReadOnlySet<string> schemaBaseline, CancellationToken cancellationToken) =>
            Do("check", CheckFailures);

        public Task OpenAsync(CancellationToken cancellationToken) => Do("open", 0);

        public Task RetireOthersAsync(Guid currentClinicId, CancellationToken cancellationToken) => Do($"retire-all-but:{currentClinicId}", 0);

        public Task PurgeAsync(Guid clinicId, CancellationToken cancellationToken) => Do($"purge:{clinicId}", 0);

        private Task<T> Do<T>(string call, T result)
        {
            Calls.Add(call);
            return FailAt != null && call.StartsWith(FailAt, StringComparison.Ordinal)
                ? Task.FromException<T>(new InvalidOperationException($"{call} failed"))
                : Task.FromResult(result);
        }
    }

    private static Task Run(Steps steps) => DemoCabinetRebuild.RunAsync(steps, NullLogger.Instance, CancellationToken.None);

    [Fact] // [FR-7] built aside with the dentist off, checked, opened, then the old ones go
    public async Task A_Rebuild_Opens_Only_After_The_Checks_And_Retires_Last()
    {
        var steps = new Steps();

        await Run(steps);

        Assert.Equal(new[] { "baseline", "provision", "build", "check", "open", $"retire-all-but:{Steps.Built}" }, steps.Calls);
    }

    [Fact] // [AC-7.6] a cabinet that fails its money or schema check is never opened, and is removed
    public async Task A_Failed_Check_Keeps_Yesterdays_Cabinet()
    {
        var steps = new Steps { CheckFailures = new[] { "money ledgers-agree: 12,000 DT" } };

        var ex = await Assert.ThrowsAsync<DemoCabinetRebuild.CheckFailedException>(() => Run(steps));

        Assert.Equal(new[] { "baseline", "provision", "build", "check", $"purge:{Steps.Built}" }, steps.Calls);
        Assert.Contains("ledgers-agree", ex.Message);
    }

    [Fact] // [EC-7] a scenario that breaks mid-build leaves no half-built cabinet behind
    public async Task A_Failed_Build_Removes_The_Half_Built_Cabinet()
    {
        var steps = new Steps { FailAt = "build" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(steps));

        Assert.Equal(new[] { "baseline", "provision", "build", $"purge:{Steps.Built}" }, steps.Calls);
    }

    [Fact] // a provisioning that fails part-way may have written rows; whatever it wrote is removed
    public async Task A_Failed_Provision_Is_Cleaned_Up_Too()
    {
        var steps = new Steps { FailAt = "provision" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(steps));

        Assert.Equal(new[] { "baseline", "provision", $"purge:{Steps.Built}" }, steps.Calls);
    }

    [Fact] // nothing was written before the baseline, so there is nothing to remove
    public async Task A_Failed_Baseline_Purges_Nothing()
    {
        var steps = new Steps { FailAt = "baseline" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(steps));

        Assert.Equal(new[] { "baseline" }, steps.Calls);
    }

    [Fact] // the failure stays the one reported, even when cleaning up after it also fails
    public async Task A_Failed_Cleanup_Does_Not_Hide_The_Original_Failure()
    {
        var steps = new Steps { FailAt = "purge", CheckFailures = new[] { "schema x" } };

        await Assert.ThrowsAsync<DemoCabinetRebuild.CheckFailedException>(() => Run(steps));
    }

    [Fact] // the new cabinet is open and correct; an old one left behind is the next rebuild's job
    public async Task A_Failed_Retirement_Does_Not_Fail_The_Rebuild()
    {
        var steps = new Steps { FailAt = "retire" };

        await Run(steps);

        Assert.Contains("open", steps.Calls);
        Assert.DoesNotContain($"purge:{Steps.Built}", steps.Calls);
    }

    [Fact] // the demo database holds only demo cabinets, so any money drift is a reason not to open
    public void Money_Drift_Fails_The_Check()
    {
        var failures = DemoCabinetRebuild.Failures(Money(MoneyReconciliationSeverity.Drift), new HashSet<string>(), Schema());

        Assert.Single(failures);
        Assert.StartsWith("money ledgers-agree", failures[0]);
    }

    [Fact] // a deployment's standing schema drift (e.g. the key ring) is not the rebuild's fault; only new drift is
    public void Only_Schema_Drift_The_Build_Added_Fails_The_Check()
    {
        var before = DemoCabinetRebuild.SchemaDrift(Schema(("key-ring-protection", SchemaVerificationSeverity.Drift)));
        var after = Schema(
            ("key-ring-protection", SchemaVerificationSeverity.Drift),
            ("audit-chain", SchemaVerificationSeverity.Drift),
            ("indexes", SchemaVerificationSeverity.Info));

        var failures = DemoCabinetRebuild.Failures(Money(MoneyReconciliationSeverity.Info), before, after);

        Assert.Equal(new[] { "schema db audit-chain: detail" }, failures);
    }

    private static MoneyReconciliationReport Money(MoneyReconciliationSeverity severity) =>
        new(new[] { new MoneyReconciliationFinding("Cabinet Dentaire Démo", "ledgers-agree", "12,000 DT", severity) },
            Array.Empty<MonthlyBaselineLine>());

    private static SchemaVerificationReport Schema(params (string Check, SchemaVerificationSeverity Severity)[] findings) =>
        new(findings.Select(f => new SchemaVerificationFinding("db", f.Check, "detail", f.Severity)).ToList());
}
