namespace ClinicManagement.Domain.Common;

/// <summary>
/// What a browser's Web Push subscription must look like before the server will ever POST to it (staff-chat Part 8).
///
/// <para>⚠️ <b>The endpoint is a URL the client chose, and the dispatcher sends to it.</b> Accepting any https address
/// would let a signed-in account point the server at its own network — the database, the object store, the loopback
/// the job dashboard trusts. So only the browser vendors' push services are accepted: Google (Chrome, Edge on Android,
/// Samsung), Mozilla, Apple (Safari and iPhone home-screen apps) and Microsoft (Edge on Windows).</para>
/// </summary>
public static class WebPushSubscription
{
    private static readonly string[] Hosts =
    {
        "fcm.googleapis.com",
        "updates.push.services.mozilla.com",
        "web.push.apple.com",
    };

    private static readonly string[] HostSuffixes =
    {
        ".push.apple.com",
        ".push.services.mozilla.com",
        ".notify.windows.com",
    };

    /// <summary>Why this subscription is refused, in French; null when it may be registered.</summary>
    public static string? Refusal(string endpoint, string? p256dh, string? auth)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Port != 443
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return "Adresse d'abonnement aux notifications invalide.";
        }

        var host = uri.Host.ToLowerInvariant();
        if (!Hosts.Contains(host) && !HostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal)))
        {
            return "Ce service de notifications n'est pas pris en charge.";
        }

        var key = FromBase64Url(p256dh);
        var secret = FromBase64Url(auth);
        // An uncompressed P-256 point and a 16-byte secret — anything else cannot be encrypted to.
        if (key is not { Length: 65 } || key[0] != 0x04 || secret is not { Length: 16 })
        {
            return "Les clés de l'abonnement aux notifications sont invalides.";
        }

        return null;
    }

    /// <summary>Base64url without padding, as browsers hand the keys over; null when it does not decode.</summary>
    public static byte[]? FromBase64Url(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var base64 = value.Trim().Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string ToBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
