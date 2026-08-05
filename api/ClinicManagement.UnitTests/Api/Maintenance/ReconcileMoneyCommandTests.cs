using ClinicManagement.API.Maintenance;
using ClinicManagement.Infrastructure.Deployment;
using Xunit;

namespace ClinicManagement.UnitTests.Api.Maintenance;

/// <summary>
/// The <c>reconcile-money</c> console command (Data &amp; Money Integrity, AC-74). This is the thin CLI wrapper;
/// its substantive work lives in <see cref="Application.Common.Maintenance.MoneyReconciliationService"/> and is
/// covered by <c>MoneyReconciliationServiceTests</c>. These tests pin the wrapper's own behavior: the verb
/// operators and the runbook invoke, the distinct exit code that separates "found drift" from "could not run",
/// the argument guard, and the Local-mode refusal — mirroring <c>ProvisionCertCommandTests</c>.
///
/// Two tests mutate the process-global <c>Auth__Mode</c> environment variable (restored in a <c>finally</c>).
/// The <c>[Collection]</c> marker serializes this class against the other env-var-sensitive maintenance tests.
/// </summary>
[Collection("EnvironmentVariables")]
public sealed class ReconcileMoneyCommandTests
{
    // [AC-74] The verb an operator types, and that packaging/README.md documents. A drift here breaks the
    // documented before/after migration procedure, so the contract is pinned in code.
    [Fact]
    public void CommandName_is_the_verb_the_runbook_invokes()
    {
        Assert.Equal("reconcile-money", ReconcileMoneyCommand.CommandName);
    }

    // [AC-74] "Could not run" and "ran and found a problem" must not look the same to an operator or a script —
    // both existing verbs use 1 only for the former, so drift gets its own code.
    [Fact]
    public void Drift_has_a_distinct_exit_code_from_a_failure_to_run()
    {
        Assert.Equal(2, ReconcileMoneyCommand.DriftFoundExitCode);
        Assert.NotEqual(1, ReconcileMoneyCommand.DriftFoundExitCode);
        Assert.NotEqual(0, ReconcileMoneyCommand.DriftFoundExitCode);
    }

    // [AC-74] A bad months-of-history argument is rejected before any database work is attempted, so a typo
    // cannot be mistaken for a clean report.
    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task Run_rejects_an_invalid_months_argument_without_touching_the_database(string months)
    {
        var originalError = Console.Error;
        var capturedError = new StringWriter();

        try
        {
            Console.SetError(capturedError);

            var exitCode = await ReconcileMoneyCommand.RunAsync(
                new[] { ReconcileMoneyCommand.CommandName, months });

            Assert.Equal(1, exitCode);
            Assert.Contains("months", capturedError.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    // [AC-74 / US-6 M3] The gate is « is there a database to connect to? », NOT the deployment profile.
    //
    // ⚠️ This case used to assert the refusal named `CloudBrowser`, and amendment M3 deliberately retired that:
    // the verb needs a connection string, not pg_dump, and gating it on the profile made its sibling
    // `verify-schema` — the product's ONLY gate on a schema change — unreachable in a hosted deployment. What it
    // must still do is refuse, exit non-zero and open no connection when no connection string is configured, in
    // EVERY profile. Env vars override the copied appsettings.json so the assertion is deterministic.
    [Theory]
    [InlineData("Cloud")]
    [InlineData("Local")]
    public async Task Run_refuses_and_returns_nonzero_without_a_connection_string(string authMode)
    {
        const string authModeVar = "Auth__Mode";
        const string connectionVar = "ConnectionStrings__DefaultConnection";
        var previousMode = Environment.GetEnvironmentVariable(authModeVar);
        var previousConnection = Environment.GetEnvironmentVariable(connectionVar);
        var originalError = Console.Error;
        var capturedError = new StringWriter();

        try
        {
            Environment.SetEnvironmentVariable(authModeVar, authMode);
            Environment.SetEnvironmentVariable(connectionVar, string.Empty);
            Console.SetError(capturedError);

            var exitCode = await ReconcileMoneyCommand.RunAsync(new[] { ReconcileMoneyCommand.CommandName });

            Assert.Equal(1, exitCode);
            // Names the key an operator has to set, and both spellings of it — the refusal is the whole
            // instruction they get.
            Assert.Contains("ConnectionStrings:DefaultConnection", capturedError.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.SetEnvironmentVariable(authModeVar, previousMode);
            Environment.SetEnvironmentVariable(connectionVar, previousConnection);
        }
    }
}
