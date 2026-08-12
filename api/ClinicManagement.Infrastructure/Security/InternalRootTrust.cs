using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// An <see cref="HttpClient"/> that trusts the deployment's <b>internal root</b> and nothing less
/// (hosted-security-hardening Part 2, FR-2.2). Used for the object-store hop, whose leaf is signed by a CA
/// minted for this deployment and therefore present in no system trust store.
///
/// <para>⚠️ <b>This ADDS a trust anchor; it does not remove a check.</b> The distinction is the whole point,
/// because the shape one reaches for first — a callback returning <c>true</c> — is indistinguishable in a diff
/// and turns verified TLS back into encryption with no identity behind it, which is exactly what FR-2.1/2.2
/// exist to rule out. So:</para>
/// <list type="bullet">
/// <item>no policy error at all ⇒ accepted, unchanged from the default;</item>
/// <item><b>only</b> a chain error ⇒ rebuilt against the internal root, accepted if it chains;</item>
/// <item>a <b>name mismatch</b>, or no certificate at all ⇒ <b>refused</b>, whatever the root says.</item>
/// </list>
///
/// <para>⚠️ Revocation is <c>NoCheck</c>: there is no CRL or OCSP responder for a CA that exists only inside
/// this compose network, and the default <c>Online</c> mode would make every request wait for a lookup that
/// cannot succeed. Ten-year leaves and a rebuild of the volume are this deployment's revocation story.</para>
/// </summary>
public static class InternalRootTrust
{
    /// <summary>
    /// A client verifying against <paramref name="trustedRoot"/> in addition to the system anchors. The
    /// caller owns the returned instance; the Minio builder is handed it with <c>disposeHttpClient: true</c>.
    /// </summary>
    public static HttpClient CreateHttpClient(X509Certificate2 trustedRoot)
    {
        ArgumentNullException.ThrowIfNull(trustedRoot);

        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                    IsTrusted(trustedRoot, certificate, errors),
            },
        };

        return new HttpClient(handler, disposeHandler: true);
    }

    /// <summary>
    /// The rule stated in the class summary, extracted so it is assertable without opening a socket.
    /// </summary>
    public static bool IsTrusted(
        X509Certificate2 trustedRoot,
        System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
        SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        // Only an untrusted chain may be forgiven by the internal root. A hostname that does not match the
        // leaf is a different failure and no trust anchor makes it acceptable.
        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || certificate is null)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        using var leaf = new X509Certificate2(certificate);
        return chain.Build(leaf);
    }
}
