using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.API.Startup;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Relay;
using ClinicManagement.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Pairs this PC with its cloud clinic as its PC de secours (<c>clinic-pc-copy</c> D8). Run by the installer's
/// « PC de secours » role, with the one-time code an administrator issued behind their authenticator. Usage:
///   ClinicManagement.API.exe pair-relay --code-file &lt;fichier&gt; --cloud &lt;https://…&gt; [--label &lt;nom&gt;] [--replace]
/// </summary>
/// <remarks>
/// ⚠️ It does not choose the deployment kind: the installer writes <c>Deployment:Profile=ClinicRelay</c> into
/// <c>appsettings.Install.json</c>, the layer it owns, and this verb refuses anywhere else — a verb that rewrote the
/// profile would turn a LAN server into a copy of somebody else's clinic with one mistyped command.
/// </remarks>
public static class PairRelayConsoleCommand
{
    public const string CommandName = "pair-relay";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var codeFile = ConsoleArgs.ReadOption(args, "--code-file");
        var cloud = ConsoleArgs.ReadOption(args, "--cloud");
        if (string.IsNullOrWhiteSpace(codeFile) || !Uri.TryCreate(cloud, UriKind.Absolute, out var cloudUri)
            || cloudUri.Scheme != Uri.UriSchemeHttps && !cloudUri.IsLoopback)
        {
            Console.Error.WriteLine($"Usage: {CommandName} --code-file <fichier> --cloud <https://…> [--label <nom>] [--replace]");
            return 1;
        }

        var configuration = InstallConfiguration.BuildForConsoleVerb();
        var profile = DeploymentProfile.Resolve(configuration);
        if (!profile.MirrorsCloudClinic)
        {
            Console.Error.WriteLine(
                $"Ce poste n'est pas installé comme PC de secours (profil : {profile.Kind}). Relancez l'installation "
                + "et choisissez « PC de secours ».");
            return 1;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        var store = new RelayCredentialStore(provider.GetRequiredService<IDataProtectionProvider>());
        if (store.Exists && !args.Contains("--replace", StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "Ce PC est déjà jumelé à un cabinet. Retirez-le depuis « Paramètres → PC de secours » puis relancez "
                + "avec --replace.");
            return 1;
        }

        string code;
        try
        {
            code = (await File.ReadAllTextAsync(codeFile, cancellationToken)).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Le code d'installation n'a pas pu être lu : {ex.Message}");
            return 1;
        }
        finally
        {
            // Single-use either way; the file must not outlive the attempt.
            TryDelete(codeFile);
        }

        // A copy holding work saved during a cut that never reached the cloud is that work's only copy (AC-7.3): a new
        // pairing would seed over it.
        var lease = provider.GetRequiredService<RelayLease>();
        if (lease.HoldsUnreturnedWork)
        {
            Console.Error.WriteLine(RelayRefusals.CutWorkKept);
            return 1;
        }

        var (publicKey, privateKey) = RelaySecretEnvelope.NewKeyPair();
        var label = ConsoleArgs.ReadOption(args, "--label") ?? Environment.MachineName;
        var request = new
        {
            code,
            label,
            publicKey,
            certificateFingerprint = RelayHostFacts.CertificateFingerprint(),
            lanAddresses = string.Join(",", RelayHostFacts.LanAddresses()),
            build = provider.GetRequiredService<IRelayBuildInfo>().Current,
        };

        using var http = new HttpClient { BaseAddress = new Uri(cloudUri.ToString().TrimEnd('/') + "/api/"), Timeout = TimeSpan.FromSeconds(30) };
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("relay/pair", request, Json, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine($"Le cloud ne répond pas ({cloudUri.Host}). Vérifiez la connexion internet et réessayez.");
            return 1;
        }

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine(await RefusalAsync(response, cancellationToken));
            return 1;
        }

        var paired = await response.Content.ReadFromJsonAsync<RelayPairingDto>(Json, cancellationToken);
        if (paired is null || string.IsNullOrEmpty(paired.Secret))
        {
            Console.Error.WriteLine("Le cloud a répondu sans identifiant de PC de secours. Réessayez.");
            return 1;
        }

        store.Save(new RelayCredentials(paired.RelayId, paired.ClinicId, paired.ClinicName,
            cloudUri.ToString().TrimEnd('/'), paired.Secret, Convert.ToBase64String(privateKey), DateTime.UtcNow));

        // A new pairing is a new copy: the cursor, the « retiré » and the erase a re-paired PC remembered belong to the
        // pairing it replaces, and a stale cursor would stop the new copy for good (D12).
        new RelayFollowerStateStore().Save(new RelayFollowerState());
        lease.ResetForNewPairing();

        Console.WriteLine($"Ce PC est maintenant le PC de secours {RelayPromoter.OfTheCabinet(paired.ClinicName)}. La première copie commence.");
        return 0;
    }

    private static async Task<string> RefusalAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (body.RootElement.TryGetProperty("error", out var error) && error.GetString() is { Length: > 0 } sentence)
            {
                return sentence;
            }
        }
        catch (JsonException)
        {
        }

        return $"Le jumelage a été refusé par le cloud (HTTP {(int)response.StatusCode}).";
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
