using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Auth;

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
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile(
                    $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
                    optional: true)
                .AddEnvironmentVariables()
                .Build();

            if (!LocalAuthConfig.IsLocalMode(configuration))
            {
                Console.Error.WriteLine(
                    "This recovery utility only runs in Local (offline) mode (Auth:Mode=Local). " +
                    "Cloud deployments reset passwords through Auth0.");
                return 1;
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(configuration);

            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

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
