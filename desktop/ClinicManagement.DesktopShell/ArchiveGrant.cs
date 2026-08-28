using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Trades this machine's device grant for an ordinary short-lived access token.
///
/// <para>⚠️ <b>The token lives thirty minutes</b> (<c>Auth:Local:AccessTokenLifetimeMinutes</c>), which is ample
/// for one archive download and nowhere near enough for a first file mirror of a cabinet with years of
/// radiographs. A caller that runs longer than that must be able to ask again mid-run rather than failing — which
/// is why this is a free function on the grant rather than state inside either service.</para>
///
/// <para>⚠️ Every refusal — unknown, revoked, an issuing account since deactivated or demoted — comes back as
/// <c>null</c>, with no attempt to distinguish them. The server deliberately answers them all alike, and inventing
/// a difference here would make the shell claim to know something it was not told.</para>
/// </summary>
internal static class ArchiveGrant
{
    /// <summary>Where the grant travels. Must match <c>BackupController.ArchiveGrantHeader</c>.</summary>
    public const string Header = "X-Archive-Grant";

    public static async Task<string?> ExchangeAsync(
        HttpClient http, ServerConfig server, string secret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{server.BaseUrl}/api/backup/archive-grants/token");
        request.Headers.Add(Header, secret);
        request.Content = new StringContent("", Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return body.RootElement.TryGetProperty("accessToken", out var token) ? token.GetString() : null;
    }
}
