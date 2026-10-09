using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>
/// <c>clinic-pc-copy</c> AC-3.7 / D24 — a plain browser (no Windows or Android app) opens the PC de secours from the
/// address on « Paramètres → PC de secours », and « Préparer ce navigateur » sends it to the PC's own trust page, which
/// installs the cabinet's certificate so the PC then opens with no warning. Both are shown as a link and a QR code.
///
/// <para>⚠️ The trust page is the LAN server's existing one (<c>TrustController</c>, cleartext by necessity — a device
/// cannot fetch the certificate fix over the certificate it does not trust yet), on <see cref="TrustPort"/>.
/// ⚠️ A browser prepared on one address keeps a warning if the box gives the PC another: the PC's certificate names
/// the addresses it had when it was made. The apps do not have this limit (D21) — they pin the certificate itself.</para>
/// </summary>
public static class RelayBrowserAccess
{
    /// <summary>The PC's trust page port — <c>TrustPortGate.DefaultPort</c> and the installer's <c>TrustPort</c>, which write it.</summary>
    public const int TrustPort = 5080;

    public sealed record Urls(string Open, string Prepare);

    /// <summary>Where a browser opens the PC and where it is prepared — null for a retired PC or one not heard from yet.</summary>
    public static Urls? For(ClinicRelay? relay)
    {
        if (relay is null || relay.RetiredAtUtc is not null || relay.HttpsPort is not { } port || port is <= 0 or > 65535)
        {
            return null;
        }

        var address = relay.LanAddressList.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        if (address is null)
        {
            return null;
        }

        var host = address.Contains(':') ? $"[{address}]" : address;
        return new Urls($"https://{host}:{port}/", $"http://{host}:{TrustPort}/api/trust");
    }
}
