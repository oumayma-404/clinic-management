using ClinicManagement.API.Maintenance;
using ClinicManagement.Infrastructure.Deployment;
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

    // [US-6 M3] The gate is « is there a database to connect to? », NOT the deployment profile — and for this
    // verb that is the point of the amendment rather than a detail of it.
    //
    // ⚠️ This case used to assert the refusal named `CloudBrowser`. `verify-schema` is the product's ONLY gate on
    // a schema change (nothing in this test project touches a database), so refusing in a hosted deployment left
    // the one topology where a bad migration hits every clinic at once with no gate at all. What it must still do
    // is refuse, exit non-zero and open no connection when no connection string is configured, in EVERY profile.
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

            var exitCode = await VerifySchemaCommand.RunAsync(new[] { VerifySchemaCommand.CommandName });

            Assert.Equal(1, exitCode);
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
