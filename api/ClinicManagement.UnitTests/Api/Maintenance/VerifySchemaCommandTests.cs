using ClinicManagement.API.Maintenance;
using Xunit;

namespace ClinicManagement.UnitTests.Api.Maintenance;

/// <summary>
/// The <c>verify-schema</c> console command (plan Testing Strategy). This is the thin CLI wrapper; its
/// substantive work lives in <see cref="Application.Common.Maintenance.SchemaVerificationService"/> and is
/// covered by <c>SchemaVerificationServiceTests</c>. These tests pin the wrapper's own behaviour — the verb
/// operators and the runbook invoke, the distinct exit code that separates "found drift" from "could not run",
/// and the Local-mode refusal — mirroring <c>ReconcileMoneyCommandTests</c>.
///
/// One test mutates the process-global <c>Auth__Mode</c> environment variable (restored in a <c>finally</c>).
/// The <c>[Collection]</c> marker serialises this class against the other env-var-sensitive maintenance tests.
/// </summary>
[Collection("EnvironmentVariables")]
public sealed class VerifySchemaCommandTests
{
    // The verb an operator types, and that the plan's Testing Strategy documents. Drift here breaks the
    // documented before/after migration procedure, so the contract is pinned in code.
    [Fact]
    public void CommandName_is_the_verb_the_runbook_invokes()
    {
        Assert.Equal("verify-schema", VerifySchemaCommand.CommandName);
    }

    // "Could not run" and "ran and found a problem" must not look the same to an operator or a script. Drift
    // gets its own code, matching reconcile-money so the two verbs can be scripted the same way.
    [Fact]
    public void Drift_has_a_distinct_exit_code_from_a_failure_to_run()
    {
        Assert.Equal(2, VerifySchemaCommand.DriftFoundExitCode);
        Assert.NotEqual(1, VerifySchemaCommand.DriftFoundExitCode);
        Assert.NotEqual(0, VerifySchemaCommand.DriftFoundExitCode);
    }

    // The two read-only report verbs must agree on what each exit code means, or a runbook that treats 2 as
    // "drift" for one and "could not run" for the other silently mis-reports.
    [Fact]
    public void The_drift_exit_code_matches_the_other_report_verb()
    {
        Assert.Equal(ReconcileMoneyCommand.DriftFoundExitCode, VerifySchemaCommand.DriftFoundExitCode);
    }

    // Local-only, mirroring the other console verbs: refuse with a clear message and make NO database
    // connection when the resolved auth mode is not Local. The Auth__Mode env var overrides the copied
    // appsettings.json so the assertion is deterministic.
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

            var exitCode = await VerifySchemaCommand.RunAsync(new[] { VerifySchemaCommand.CommandName });

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
