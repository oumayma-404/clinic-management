using ClinicManagement.API.Maintenance;
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

    // [AC-74] Local-only, mirroring the other two console verbs: the command must refuse with a clear message
    // and make NO database connection when the resolved auth mode is not Local. The Auth__Mode env var
    // overrides the copied appsettings.json so the assertion is deterministic.
    [Fact]
    public async Task Run_refuses_and_returns_nonzero_when_not_in_local_mode()
    {
        const string authModeVar = "Auth__Mode";
        var previousMode = Environment.GetEnvironmentVariable(authModeVar);
        var originalError = Console.Error;
        var capturedError = new StringWriter();

        try
        {
            Environment.SetEnvironmentVariable(authModeVar, "Cloud");
            Console.SetError(capturedError);

            var exitCode = await ReconcileMoneyCommand.RunAsync(new[] { ReconcileMoneyCommand.CommandName });

            Assert.Equal(1, exitCode);
            Assert.Contains("Local", capturedError.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            Environment.SetEnvironmentVariable(authModeVar, previousMode);
        }
    }
}
