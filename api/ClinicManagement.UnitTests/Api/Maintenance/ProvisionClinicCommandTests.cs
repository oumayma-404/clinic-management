using ClinicManagement.API.Maintenance;
using ClinicManagement.Infrastructure.Deployment;
using Xunit;

namespace ClinicManagement.UnitTests.Api.Maintenance;

/// <summary>
/// The <c>provision-clinic</c> console verb (multi-tenant-cloud US-3, Part C) — the thin CLI wrapper. Its
/// substantive work lives in <c>LocalClinicProvisioning</c> and is covered by
/// <c>LocalClinicProvisioningTests</c>; this pins the wrapper's own behaviour, mirroring
/// <c>VerifySchemaCommandTests</c>.
///
/// <para>Every case here stops <b>before</b> a database connection is opened — the verb parses its arguments
/// first and resolves the deployment profile second, so both refusals are reachable with no infrastructure. That
/// ordering is deliberate rather than incidental: an operator who mistypes a flag should be told which flags
/// exist, not that the database is unreachable.</para>
///
/// <para>One test mutates the process-global <c>Auth__Mode</c> environment variable (restored in a
/// <c>finally</c>). The <c>[Collection]</c> marker serialises this class against the other env-var-sensitive
/// maintenance tests.</para>
/// </summary>
[Collection("EnvironmentVariables")]
public sealed class ProvisionClinicCommandTests
{
    private static string[] ValidArgs(params string[] extra) =>
        new[]
        {
            ProvisionClinicCommand.CommandName,
            "--name", "Cabinet Ben Salah",
            "--admin-email", "owner@cabinet.tn",
            "--admin-name", "Dr Ahmed Ben Salah"
        }.Concat(extra).ToArray();

    private static async Task<(int ExitCode, string Error)> RunCapturingError(string[] args)
    {
        var originalError = Console.Error;
        var originalOut = Console.Out;
        var capturedError = new StringWriter();
        try
        {
            Console.SetError(capturedError);
            Console.SetOut(new StringWriter());
            var exitCode = await ProvisionClinicCommand.RunAsync(args);
            return (exitCode, capturedError.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            Console.SetOut(originalOut);
        }
    }

    // [US-3] The verb an operator types, and that the hosted runbook documents. Drift here breaks
    // `docker exec clinic-api-prod dotnet ClinicManagement.API.dll provision-clinic …`.
    [Fact]
    public void CommandName_is_the_verb_the_runbook_invokes()
    {
        Assert.Equal("provision-clinic", ProvisionClinicCommand.CommandName);
    }

    /// <summary>
    /// [US-3] A full name is required and is <b>not</b> guessed from the email. It is printed on documents and is
    /// the practitioner's own name; deriving « owner » from <c>owner@cabinet.tn</c> would put a fabricated
    /// identity on a clinical record, and the account cannot be created without one anyway
    /// (<c>User.CreateLocalUser</c> throws).
    /// </summary>
    [Theory]
    [InlineData("--name")]
    [InlineData("--admin-email")]
    [InlineData("--admin-name")]
    public async Task A_missing_required_flag_prints_the_usage_and_refuses(string omitted)
    {
        var args = ValidArgs().ToList();
        var index = args.IndexOf(omitted);
        args.RemoveRange(index, 2);

        var (exitCode, error) = await RunCapturingError(args.ToArray());

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage:", error);
        // The usage line must name every required flag — an operator reading it should not need the source.
        Assert.Contains("--name", error);
        Assert.Contains("--admin-email", error);
        Assert.Contains("--admin-name", error);
    }

    /// <summary>
    /// [US-3] A flag given with no value must not swallow the next flag. Without this, <c>--name --admin-email
    /// owner@cabinet.tn</c> would create a clinic literally called « --admin-email » and then complain that the
    /// admin's email was missing — a refusal pointing at the wrong flag.
    /// </summary>
    [Fact]
    public async Task A_flag_with_no_value_does_not_consume_the_next_flag()
    {
        var (exitCode, error) = await RunCapturingError(new[]
        {
            ProvisionClinicCommand.CommandName,
            "--name",
            "--admin-email", "owner@cabinet.tn",
            "--admin-name", "Dr Ahmed Ben Salah"
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage:", error);
    }

    /// <summary>
    /// [US-3] Refused where the product does not own its accounts, and refused with no database connection: an
    /// Auth0 deployment creates users in Auth0, and a password-backed admin minted here would be an account
    /// nobody could log into.
    /// </summary>
    [Fact]
    public async Task It_refuses_on_a_deployment_whose_accounts_it_does_not_own()
    {
        const string authModeVar = "Auth__Mode";
        var previousMode = Environment.GetEnvironmentVariable(authModeVar);

        try
        {
            Environment.SetEnvironmentVariable(authModeVar, "Cloud");

            var (exitCode, error) = await RunCapturingError(ValidArgs());

            Assert.Equal(1, exitCode);
            // ⚠️ See ProvisionCertCommandTests for the full note: `Auth:Mode = Cloud` with no
            // `Deployment:Profile` no longer resolves at all, so the refusal comes from `Resolve` rather than
            // from this verb's own `UsesLocalAccounts` guard — which both surviving profiles now satisfy.
            Assert.Contains(DeploymentProfile.ProfileKey, error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(authModeVar, previousMode);
        }
    }

    /// <summary>
    /// [US-3] ⚠️ The capability this verb must NOT be gated on. <c>HasLocalDbTooling</c> is about
    /// <c>pg_dump</c>/<c>pg_restore</c> being on the box and is <b>false</b> in <c>HostedMultiTenant</c> — which
    /// is the one profile this verb exists for. Gating on it, as the backup and report verbs correctly do, would
    /// make <c>provision-clinic</c> refuse everywhere it is needed.
    /// </summary>
    [Fact]
    public void The_hosted_profile_owns_its_accounts_but_has_no_local_db_tooling()
    {
        var hosted = DeploymentProfile.For(DeploymentKind.HostedMultiTenant);

        Assert.True(hosted.UsesLocalAccounts);
        Assert.False(hosted.HasLocalDbTooling);
    }
}
