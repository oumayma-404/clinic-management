using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Server-side console utility for offline admin lockout recovery (FR-B6, Story 8).
///
/// Runs on the clinic SERVER PC — the machine that hosts the API and PostgreSQL — as a one-shot
/// command instead of starting the web server:
///
///   dotnet run --project ClinicManagement.API -- reset-admin-password [admin-email]
///
/// It connects directly to the local database, resets the (sole, or named) local administrator's
/// password to a fresh temporary value, and forces a change at next login. No internet, email, or
/// cloud service is involved. Only valid in Local (offline) mode.
/// </summary>
public static class AdminPasswordResetCommand
{
    public const string CommandName = "reset-admin-password";

    /// <summary>Returns a process exit code: 0 on success, 1 on any failure.</summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        // args[0] is the command name; args[1] (optional) is the target admin email.
        var email = args.Length > 1 ? args[1] : null;

        try
        {
            // Resolve appsettings from the install directory (R-6), not the CWD, so the packaged
            // `ClinicManagement.API.exe reset-admin-password` works from any working directory
            // (this is the sole offline admin-recovery path — FR-B6). The signing key likewise resolves
            // against the install directory via LocalAuthConfig.
            var configuration = InstallConfiguration.BuildForConsoleVerb();

            // Two things are needed: accounts this product owns, and a database to reset one in.
            // ⚠️ The second used to ask HasLocalDbTooling, which is false in HostedMultiTenant (M3) — so from the
            // moment provision-clinic could create a hosted clinic, that clinic's admin could be locked out with
            // no recovery path at all. This verb runs no PostgreSQL binary; it needs the connection string.
            var profile = DeploymentProfile.Resolve(configuration);
            if (!profile.UsesLocalAccounts)
            {
                Console.Error.WriteLine(
                    $"This deployment does not own its accounts (deployment profile: {profile.Kind}). "
                    + "An Auth0 deployment resets passwords through Auth0.");
                return 1;
            }

            if (!MaintenanceDatabase.HasConnectionString(configuration, "This recovery utility"))
            {
                return 1;
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(configuration);

            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            // I6: this verb is the one write path with no HTTP request behind it, and it changes a *credential* —
            // exactly the event an owner should be able to find in « Journal d'activité » afterwards. Naming it
            // makes the audit row read « Tâche automatique (reset-admin-password) » instead of « unknown ».
            // Resolves to `ProcessAuditActorProvider` here: this container has no `AddApplication` and no claims.
            scope.ServiceProvider.GetRequiredService<IAuditActorProvider>().RunAs(CommandName);

            // US-2: recovery searches for an admin across every clinic — there is no clinic in scope to search
            // within, which is the whole point of an offline lockout recovery.
            scope.ServiceProvider.GetRequiredService<ITenantScope>()
                .UseSystemWide($"{CommandName} recovers an admin account in any clinic");

            var recovery = new AdminPasswordRecoveryService(
                scope.ServiceProvider.GetRequiredService<IUserRepository>(),
                scope.ServiceProvider.GetRequiredService<ILocalAuthService>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

            var result = await recovery.ResetAdminPasswordAsync(email, cancellationToken);

            if (result.IsFailure)
            {
                Console.Error.WriteLine($"Password reset failed: {result.Error}");
                return 1;
            }

            var value = result.Value!;
            Console.WriteLine();
            Console.WriteLine("Administrator password reset successfully.");
            Console.WriteLine($"  Account:            {value.AdminEmail}");
            Console.WriteLine($"  Temporary password: {value.TemporaryPassword}");
            Console.WriteLine();
            Console.WriteLine("Give this password to the administrator. They will be required to");
            Console.WriteLine("choose a new one the next time they log in.");
            Console.WriteLine();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Password reset failed: {ex.Message}");
            return 1;
        }
    }
}
