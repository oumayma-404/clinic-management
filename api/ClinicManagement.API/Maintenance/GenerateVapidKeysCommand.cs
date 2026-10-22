using ClinicManagement.Infrastructure.Services;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Prints a fresh VAPID key pair for Web Push (staff-chat Part 8), as the two environment lines the API reads.
///
/// <code>
///   ClinicManagement.API.exe generate-vapid-keys
/// </code>
///
/// <para>Run it once, on the server that will use it, so the private key never travels. ⚠️ <b>Changing the keys later
/// ends every browser's subscription</b>: each was created against the old public key, and its push service refuses a
/// message signed by another — the browsers subscribe again the next time their user opens the app.</para>
/// </summary>
public static class GenerateVapidKeysCommand
{
    public const string CommandName = "generate-vapid-keys";

    public static int Run()
    {
        var (publicKey, privateKey) = WebPushCrypto.NewVapidKeys();
        Console.WriteLine($"Push__WebPush__PublicKey={publicKey}");
        Console.WriteLine($"Push__WebPush__PrivateKey={privateKey}");
        return 0;
    }
}
