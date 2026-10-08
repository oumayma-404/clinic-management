using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Relay;
using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Uninstalling a PC de secours (<c>clinic-pc-copy</c> AC-8.3): tells the cloud, which counts it as retiring the PC, and —
/// when the box « Effacer aussi la copie du cabinet ? » was left ticked — erases the copy once the cloud has answered.
/// Run by the installer's uninstall step, with the API service stopped. Usage:
///   ClinicManagement.API.exe uninstall-relay [--erase]
/// </summary>
/// <remarks>
/// Exit 0 done · 1 cannot run · 2 done with something the operator must act on — the uninstaller shows the sentence and
/// goes on either way: refusing to uninstall would not make a lost cloud answer.
/// </remarks>
public static class UninstallRelayConsoleCommand
{
    public const string CommandName = "uninstall-relay";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var configuration = InstallConfiguration.BuildForConsoleVerb();
        var profile = DeploymentProfile.Resolve(configuration);
        if (!profile.MirrorsCloudClinic)
        {
            Console.Error.WriteLine($"Ce poste n'est pas installé comme PC de secours (profil : {profile.Kind}).");
            return 1;
        }

        if (!MaintenanceDatabase.HasConnectionString(configuration, "Uninstalling the PC de secours"))
        {
            return 1;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var credentials = new RelayCredentialStore(provider.GetRequiredService<IDataProtectionProvider>()).TryLoad();
        IRelayCloudClient? cloud = null;
        if (credentials is not null)
        {
            scope.ServiceProvider.GetRequiredService<IAuditActorProvider>().RunAs(CommandName);
            // One cabinet, so UseClinic — the narrowest scope over the only rows this verb may erase.
            scope.ServiceProvider.GetRequiredService<ITenantScope>().UseClinic(credentials.ClinicId);
            cloud = new RelayCloudClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, credentials,
                provider.GetRequiredService<IRelayBuildInfo>().Current);
        }

        var uninstaller = new RelayUninstaller(cloud, new RelayFollowerStateStore(),
            ct => scope.ServiceProvider.GetRequiredService<RelayLocalEraser>().EraseAsync(credentials!.ClinicId, DateTime.UtcNow, ct));

        try
        {
            var result = await uninstaller.UninstallAsync(
                args.Contains("--erase", StringComparer.OrdinalIgnoreCase), DateTime.UtcNow, cancellationToken);
            (result.ExitCode == 0 ? Console.Out : Console.Error).WriteLine(result.Sentence);
            return result.ExitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The rows go in one transaction, so either none left or all did and only files remain: say which.
            Console.Error.WriteLine(new RelayFollowerStateStore().Load().ErasedAtUtc is null
                ? $"La copie n'a pas pu être effacée ({ex.Message}). Rien n'a été effacé."
                : $"Les dossiers du cabinet sont effacés, mais certains fichiers n'ont pas pu l'être ({ex.Message}).");
            return 2;
        }
    }
}
