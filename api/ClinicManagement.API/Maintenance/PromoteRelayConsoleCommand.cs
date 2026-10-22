using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Relay;
using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Turns this PC de secours into the cabinet's local server once its cloud is lost for good (<c>clinic-pc-copy</c>
/// D11, AC-9.3), with a code the vendor signed offline (<c>sign-relay-promotion</c>). Run on the PC, by hand. Usage:
///   ClinicManagement.API.exe promote-relay                      (says which code to ask the vendor for)
///   ClinicManagement.API.exe promote-relay --code-file &lt;fichier&gt;
/// </summary>
/// <remarks>
/// Exit codes (<see cref="RelayPromotionResult"/>): 0 promoted (or nothing asked) · 1 could not run · 2 code refused ·
/// 3 the cloud still serves. Every refusal leaves the PC exactly as it was. No MediatR command behind it: a promotion
/// has no web path, like <c>platform-account</c>.
/// </remarks>
public static class PromoteRelayConsoleCommand
{
    public const string CommandName = "promote-relay";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        // Read by a person at a console, not by the installer: the console's own code page garbles every accent.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (RelayPromoter.LoadRecord() is { } done)
        {
            Console.WriteLine($"Ce PC a déjà été promu le {done.PromotedAtUtc:dd/MM/yyyy} : c'est le serveur local {RelayPromoter.OfTheCabinet(done.ClinicName)}.");
            return RelayPromotionResult.Promoted;
        }

        var configuration = InstallConfiguration.BuildForConsoleVerb();
        var profile = DeploymentProfile.Resolve(configuration);
        if (!profile.MirrorsCloudClinic)
        {
            Console.Error.WriteLine($"Ce poste n'est pas un PC de secours (profil : {profile.Kind}) : il n'y a rien à promouvoir.");
            return RelayPromotionResult.CannotRun;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        var credentials = new RelayCredentialStore(provider.GetRequiredService<IDataProtectionProvider>()).TryLoad();
        if (credentials is null)
        {
            Console.Error.WriteLine("Ce PC de secours n'est jumelé à aucun cabinet (ou son jumelage est illisible) : il n'y a rien à promouvoir.");
            return RelayPromotionResult.CannotRun;
        }

        var codeFile = ConsoleArgs.ReadOption(args, "--code-file");
        if (string.IsNullOrWhiteSpace(codeFile))
        {
            // The vendor has no console once the cloud is lost: this PC says who it is.
            Console.WriteLine("Pour faire de ce PC le serveur du cabinet, demandez à l'éditeur un code de promotion pour :");
            Console.WriteLine($"  PC de secours : {credentials.RelayId}");
            Console.WriteLine($"  Cabinet       : {credentials.ClinicId}");
            Console.WriteLine($"puis lancez : {CommandName} --code-file <fichier>");
            return RelayPromotionResult.Promoted;
        }

        if (!MaintenanceDatabase.HasConnectionString(configuration, "Promoting the PC de secours"))
        {
            return RelayPromotionResult.CannotRun;
        }

        string code;
        try
        {
            code = await File.ReadAllTextAsync(codeFile, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Le code de promotion n'a pas pu être lu : {ex.Message}");
            return RelayPromotionResult.CannotRun;
        }

        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;
        var actors = scoped.GetRequiredService<IAuditActorProvider>();
        actors.RunAs(CommandName);
        // One cabinet, so UseClinic — the narrowest scope over the one row this verb writes.
        scoped.GetRequiredService<ITenantScope>().UseClinic(credentials.ClinicId);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var promoter = new RelayPromoter(
            RelayPromotionCode.VendorPublicKey,
            ct => RelayPromoter.CloudServesAsync(http, credentials.CloudUrl, ct),
            async (claim, nowUtc, ct) =>
            {
                await RelayJournal.StageOnPcAsync(scoped.GetRequiredService<IAuditEntryRepository>(), actors.Current,
                    credentials.ClinicId, claim.RelayId, Environment.MachineName, AuditAction.Update, RelayJournal.Promoted,
                    nowUtc, ct);
                await scoped.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
            },
            Path.Combine(AppContext.BaseDirectory, InstallConfiguration.InstallLayerFileName),
            new RelayFollowerStateStore());

        RelayPromotionResult result;
        try
        {
            result = await promoter.PromoteAsync(credentials, code, DateTime.UtcNow, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"La promotion n'a pas pu se terminer ({ex.Message}). Relancez la même commande.");
            return RelayPromotionResult.CannotRun;
        }

        if (result.ExitCode == RelayPromotionResult.Promoted)
        {
            TryDelete(codeFile);
        }

        (result.ExitCode == RelayPromotionResult.Promoted ? Console.Out : Console.Error).WriteLine(result.Sentence);
        return result.ExitCode;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
