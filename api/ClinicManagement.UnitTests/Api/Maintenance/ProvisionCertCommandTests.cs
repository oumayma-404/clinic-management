using ClinicManagement.API.Maintenance;
using ClinicManagement.Infrastructure.Deployment;
using Xunit;

namespace ClinicManagement.UnitTests.Api.Maintenance;

/// <summary>
/// The <c>provision-cert</c> console command (Server Installer Reliability, AC-1). This is the thin CLI
/// wrapper the server installer runs before starting the API service; its substantive work — idempotent
/// CA + server-cert generation/reuse into <c>.local/</c> — lives in <see cref="Infrastructure.Security.CertificateProvisioner"/>
/// and is covered by <c>CertificateProvisionerTests</c> (loadable PFX + CA, CA→leaf signing, SAN coverage,
/// idempotent reuse). These tests pin the wrapper's own behavior: the exact verb the installer invokes, and
/// the Local-mode guard (the utility must refuse — without touching the DB or generating a cert — when the
/// resolved config is not Local, mirroring the console-only <c>reset-admin-password</c> pattern).
///
/// One test mutates the process-global <c>Auth__Mode</c> environment variable (restored in a
/// <c>finally</c>). The <c>[Collection]</c> marker serializes this class against any future env-var-sensitive
/// test that joins the same collection, so xUnit's default cross-class parallelism can't interleave them.
/// </summary>
[Collection("EnvironmentVariables")]
public sealed class ProvisionCertCommandTests
{
    // [AC-1] The command verb is the exact string the installer passes to ClinicManagement.API.exe
    // (packaging/server/clinic-server.iss). A drift here silently breaks the install-time provisioning
    // step, so the producer (installer) ↔ consumer (this command) contract is pinned in code.
    [Fact]
    public void CommandName_is_the_verb_the_installer_invokes()
    {
        Assert.Equal("provision-cert", ProvisionCertCommand.CommandName);
    }

    // [AC-1] Only valid in Local (offline) mode — a Cloud deployment uses a configured certificate. The
    // command must return a non-zero exit code with a clear message and make NO database connection / write
    // NO certificate when the resolved auth mode is not Local. The Auth__Mode env var overrides the copied
    // Cloud appsettings.json so the assertion is deterministic regardless of ASPNETCORE_ENVIRONMENT.
    [Fact]
    public void Run_refuses_and_returns_nonzero_when_not_in_local_mode()
    {
        const string authModeVar = "Auth__Mode";
        var previousMode = Environment.GetEnvironmentVariable(authModeVar);
        var originalError = Console.Error;
        var capturedError = new StringWriter();

        try
        {
            Environment.SetEnvironmentVariable(authModeVar, "Cloud");
            Console.SetError(capturedError);

            var exitCode = ProvisionCertCommand.Run(new[] { ProvisionCertCommand.CommandName });

            Assert.Equal(1, exitCode);
            // Part A replaced « Local mode » with the resolved profile, so the refusal now names the profile it
            // resolved — which is the more useful message and the one to hold. `nameof` so a rename cannot leave
            // this asserting a string that no longer exists.
            Assert.Contains(nameof(DeploymentKind.CloudBrowser), capturedError.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            Environment.SetEnvironmentVariable(authModeVar, previousMode);
        }
    }
}
