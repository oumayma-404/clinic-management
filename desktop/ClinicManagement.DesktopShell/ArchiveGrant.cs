using System.Net;
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

    /// <summary>
    /// The outcome of an exchange. <b>Three cases, not two</b> — see <see cref="Throttled"/>.
    /// </summary>
    /// <param name="Token">The bearer, or null.</param>
    /// <param name="Throttled">
    /// ⚠️ <b>Separated from a refusal because conflating them told the user to fix the wrong thing.</b> The token
    /// endpoint is on the archive rate limiter — three in ten minutes — so a second « Copier maintenant » inside
    /// that window is answered <c>429</c>. Treating every non-success as a refusal put « Ce poste n'est plus
    /// autorisé. Autorisez-le à nouveau » on screen for a limit that clears itself, which invites an owner to
    /// revoke and re-issue a perfectly good key, and to do it repeatedly because re-issuing does not help.
    /// </param>
    public readonly record struct ExchangeResult(string? Token, bool Throttled)
    {
        public bool Succeeded => Token != null;
    }

    /// <summary>The one French sentence for a genuinely refused grant (phase 1's AC-3).</summary>
    public const string RefusedMessage =
        "Ce poste n'est plus autorisé. Autorisez-le à nouveau depuis « Paramètres » sur le serveur.";

    /// <summary>And the one for a limit that will clear on its own.</summary>
    public const string ThrottledMessage =
        "Le serveur limite les copies rapprochées. Patientez une dizaine de minutes et réessayez — "
        + "ce poste reste autorisé.";

    public static async Task<ExchangeResult> ExchangeAsync(
        HttpClient http, ServerConfig server, string secret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{server.BaseUrl}/api/backup/archive-grants/token");
        request.Headers.Add(Header, secret);
        request.Content = new StringContent("", Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new ExchangeResult(null, Throttled: true);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new ExchangeResult(null, Throttled: false);
        }

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return new ExchangeResult(
            body.RootElement.TryGetProperty("accessToken", out var token) ? token.GetString() : null,
            Throttled: false);
    }
}
