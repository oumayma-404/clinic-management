using ClinicManagement.Infrastructure.Relay;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// The vendor's half of a promotion (<c>clinic-pc-copy</c> D11, AC-9.3): run on the vendor's own machine, never on a
/// server — the key that signs a promotion must not die with the cloud it replaces. Usage:
///   ClinicManagement.API.exe sign-relay-promotion --new-key &lt;fichier&gt;
///   ClinicManagement.API.exe sign-relay-promotion --key &lt;fichier&gt; --relay &lt;id&gt; --clinic &lt;id&gt; [--days &lt;1-30&gt;] --out &lt;fichier&gt;
/// </summary>
/// <remarks>
/// Reads no configuration and opens no database. It refuses a key that is not the one compiled into the PCs
/// (<see cref="RelayPromotionCode.VendorPublicKey"/>): a code they cannot verify would only be discovered at the
/// cabinet, on the day it is needed.
/// </remarks>
public static class SignRelayPromotionConsoleCommand
{
    public const string CommandName = "sign-relay-promotion";

    public static int Run(string[] args)
    {
        // Read by a person at a console: the console's own code page garbles every accent.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var newKey = ConsoleArgs.ReadOption(args, "--new-key");
        if (!string.IsNullOrWhiteSpace(newKey))
        {
            if (File.Exists(newKey))
            {
                Console.Error.WriteLine($"{newKey} existe déjà : une clé n'est jamais écrasée.");
                return 1;
            }

            var fresh = RelayPromotionCode.NewVendorKey();
            File.WriteAllText(newKey, fresh.PrivateKeyPem);
            Console.WriteLine($"Clé privée écrite dans {newKey} — à garder hors ligne, avec une copie de secours.");
            Console.WriteLine("Clé publique, à mettre dans RelayPromotionCode.VendorPublicKey :");
            Console.WriteLine(fresh.PublicKey);
            return 0;
        }

        var keyFile = ConsoleArgs.ReadOption(args, "--key");
        var outFile = ConsoleArgs.ReadOption(args, "--out");
        var days = int.TryParse(ConsoleArgs.ReadOption(args, "--days"), out var d) ? d : (int)RelayPromotionCode.DefaultValidity.TotalDays;
        if (string.IsNullOrWhiteSpace(keyFile) || string.IsNullOrWhiteSpace(outFile)
            || !Guid.TryParse(ConsoleArgs.ReadOption(args, "--relay"), out var relayId)
            || !Guid.TryParse(ConsoleArgs.ReadOption(args, "--clinic"), out var clinicId)
            || days < 1 || days > RelayPromotionCode.MaxValidity.TotalDays)
        {
            Console.Error.WriteLine($"Usage: {CommandName} --key <fichier> --relay <id> --clinic <id> [--days <1-30>] --out <fichier>");
            Console.Error.WriteLine($"   ou: {CommandName} --new-key <fichier>");
            return 1;
        }

        string privateKeyPem;
        try
        {
            privateKeyPem = File.ReadAllText(keyFile);
            if (!string.Equals(RelayPromotionCode.PublicKeyOf(privateKeyPem), RelayPromotionCode.VendorPublicKey, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Cette clé n'est pas celle que les PC de secours reconnaissent : aucun code n'a été émis.");
                return 1;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or System.Security.Cryptography.CryptographicException)
        {
            Console.Error.WriteLine($"La clé n'a pas pu être lue : {ex.Message}");
            return 1;
        }

        var now = DateTime.UtcNow;
        var code = RelayPromotionCode.Sign(privateKeyPem, relayId, clinicId, now, TimeSpan.FromDays(days));
        File.WriteAllText(outFile, code);
        Console.WriteLine($"Code de promotion écrit dans {outFile} — PC de secours {relayId} du cabinet {clinicId}, "
                          + $"valable jusqu'au {now.AddDays(days):dd/MM/yyyy HH:mm} UTC.");
        return 0;
    }
}
