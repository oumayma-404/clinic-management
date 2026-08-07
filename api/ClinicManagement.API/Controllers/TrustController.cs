using System.Net;
using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// The LAN device-trust page (P8, AC-44): everything a phone or tablet needs in order to trust this install's
/// self-signed HTTPS certificate, served over plain HTTP so it is reachable <b>before</b> that trust exists.
///
/// <para><b>Local-only, and anonymous by necessity.</b> It 404s in Cloud (which has a real certificate and no
/// offline story) via a runtime check rather than conditional registration, matching
/// <see cref="ConnectivityController"/> — the mode is a property of the request, and a controller that exists
/// but refuses is far easier to reason about than one that conditionally does not exist. Anonymous is not a
/// concession: the device cannot log in until it trusts the server, so requiring a token here would be a
/// deadlock. Nothing served is a secret — a CA's <b>public</b> certificate, install instructions, and a QR of
/// an address that is already broadcast on the LAN.</para>
///
/// <para>⚠️ <b>The cleartext exposure is bounded by <see cref="Startup.TrustPortGate"/>, not by this class.</b>
/// Kestrel serves every route on every bound port, so binding the trust port is what makes these actions
/// LAN-reachable — and would equally have made <c>POST /api/auth/login</c> LAN-reachable in cleartext. The gate
/// middleware refuses everything except this prefix on that port. Read the two together.</para>
///
/// <para>⚠️ <b>Routes live under <c>/api/</c> deliberately.</b> In Local mode a YARP catch-all forwards every
/// non-<c>/api</c> route to the co-located Next server, so a friendlier <c>/trust</c> would be proxied to a web
/// app that has no such page. There is also no <c>UseStaticFiles</c> anywhere in this host, which is why the
/// assets are returned as <see cref="FileContentResult"/> rather than dropped in a <c>wwwroot</c>.</para>
///
/// <para>⚠️ <b>The CA is read from disk, not from <see cref="CertificateProvisioner"/>.</b> That type is
/// deliberately not DI-registered — it is constructed before the container exists, because Kestrel needs the
/// certificate to bind. Injecting it here would either duplicate that construction or force it into the
/// container for one consumer; reading the file it already wrote is both simpler and honest about what is
/// being served.</para>
/// </summary>
[ApiController]
[Route("api/trust")]
// All four actions are [AllowAnonymous] by necessity — a device cannot obtain a token until it trusts this
// server's certificate, and it cannot trust the certificate until it has fetched these. The class policy is
// the backstop for a future action that is *not* part of that bootstrap.
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class TrustController : ApiControllerBase
{
    private const string CaCertFileName = "ca.crt";

    /// <summary>What a browser and Android both accept for a DER-encoded CA certificate.</summary>
    private const string CaContentType = "application/x-x509-ca-cert";

    private readonly IConfiguration _configuration;
    private readonly IQrCodeGenerator _qrCodeGenerator;

    private readonly DeploymentProfile _deployment;

    public TrustController(IConfiguration configuration, IQrCodeGenerator qrCodeGenerator, DeploymentProfile deployment)
    {
        _configuration = configuration;
        _deployment = deployment;
        _qrCodeGenerator = qrCodeGenerator;
    }

    // Injected rather than re-resolved per request — see AuthController for why.
    private DeploymentProfile Deployment => _deployment;

    /// <summary>The instructions page itself — the only HTML this API serves.</summary>
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Page()
    {
        if (!Deployment.ExposesTrustEndpoints)
        {
            return NotFound();
        }

        var caPath = LocalInstallPaths.LocalFile(CaCertFileName);
        var caExists = System.IO.File.Exists(caPath);

        var html = BuildPage(caExists);
        return Content(html, "text/html; charset=utf-8", Encoding.UTF8);
    }

    /// <summary>The CA's public certificate — what Android imports directly.</summary>
    [AllowAnonymous]
    [HttpGet("ca.crt")]
    public IActionResult CaCertificate()
    {
        if (!Deployment.ExposesTrustEndpoints)
        {
            return NotFound();
        }

        if (!TryReadCa(out var caDer))
        {
            return Failure(CaMissingMessage, StatusCodes.Status404NotFound);
        }

        return File(caDer, CaContentType, CaCertFileName);
    }

    /// <summary>The same CA wrapped as an iOS/iPadOS configuration profile.</summary>
    [AllowAnonymous]
    [HttpGet("profile.mobileconfig")]
    public IActionResult AppleProfile()
    {
        if (!Deployment.ExposesTrustEndpoints)
        {
            return NotFound();
        }

        if (!TryReadCa(out var caDer))
        {
            return Failure(CaMissingMessage, StatusCodes.Status404NotFound);
        }

        var profile = AppleTrustProfile.Build(caDer, _configuration["Clinic:DisplayName"]);
        return File(profile, AppleTrustProfile.ContentType, "clinique-confiance.mobileconfig");
    }

    /// <summary>
    /// A QR of this page's own LAN address, so the operator can show it to a phone instead of dictating an IP.
    /// Rendered <b>server-side</b> because the page is loaded by a device that does not trust this server yet:
    /// pulling a QR library from a CDN would need internet on an offline install, and bundling one would need
    /// the static-file pipeline this host does not have.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("qr.png")]
    public IActionResult QrCode()
    {
        if (!Deployment.ExposesTrustEndpoints)
        {
            return NotFound();
        }

        var png = _qrCodeGenerator.GeneratePng(TrustPageUrl(), pixelsPerModule: 8);
        return File(png, "image/png");
    }

    private const string CaMissingMessage =
        "Aucune autorité de certification n'a été générée sur ce serveur. "
        + "Le serveur utilise un certificat fourni par l'exploitant (Https:CertPath) : "
        + "installez sur l'appareil l'autorité qui a émis ce certificat.";

    private bool TryReadCa(out byte[] caDer)
    {
        var caPath = LocalInstallPaths.LocalFile(CaCertFileName);
        if (!System.IO.File.Exists(caPath))
        {
            caDer = Array.Empty<byte>();
            return false;
        }

        caDer = System.IO.File.ReadAllBytes(caPath);
        return caDer.Length > 0;
    }

    /// <summary>
    /// The address to print and to encode in the QR. Prefers a real LAN IPv4 whenever the request arrived on
    /// loopback — the operator opening this page on the server PC types <c>localhost</c>, and a QR encoding
    /// <c>localhost</c> sends the phone to itself. Falls back to the host the request actually used, which is
    /// by definition an address that reached us.
    /// </summary>
    private string ResolveAdvertisedHost()
    {
        var requestHost = Request.Host.Host;

        var isLoopbackHost =
            string.Equals(requestHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(requestHost, out var parsed) && IPAddress.IsLoopback(parsed));

        if (!isLoopbackHost)
        {
            return requestHost;
        }

        // Same list the certificate's SANs were built from, so an address printed here is one the certificate
        // actually claims. See LanAddresses.
        var lan = LanAddresses.IPv4().FirstOrDefault();
        return lan?.ToString() ?? requestHost;
    }

    // ⚠️ The two download links in the page below are ROOT-relative, not document-relative, and that is the whole
    // point: `href="ca.crt"` on a page served at `/api/trust` resolves against `/api/`, so the browser asks for
    // `/api/ca.crt` — a 404, and refused outright by TrustPortGate on this port. Both buttons therefore failed for
    // anyone reaching the page without a trailing slash, which is the address printed in the docs and typed by
    // hand. Found on an iPhone: the page rendered and the one screen whose entire job is installing the CA could
    // not install it.
    private int TrustPort() =>
        _configuration.GetValue<int?>("Hosting:TrustPort") ?? Startup.TrustPortGate.DefaultPort;

    private int HttpsPort() => _configuration.GetValue<int?>("Hosting:HttpsPort") ?? 5001;

    private string TrustPageUrl() => $"http://{ResolveAdvertisedHost()}:{TrustPort()}{Startup.TrustPortGate.TrustPathPrefix}";

    private string AppUrl() => $"https://{ResolveAdvertisedHost()}:{HttpsPort()}";

    private string BuildPage(bool caExists)
    {
        var appUrl = WebUtility.HtmlEncode(AppUrl());
        var trustUrl = WebUtility.HtmlEncode(TrustPageUrl());

        var actions = caExists
            ? $"""
                       <img class="qr" src="qr.png" width="200" height="200"
                            alt="QR code menant à cette page : {trustUrl}">
                       <p class="muted">Scannez ce code depuis l'appareil à configurer pour ouvrir cette page dessus.</p>

                       <h2>1 · Installer le certificat</h2>
                       <div class="grid">
                         <a class="btn" href="/api/trust/profile.mobileconfig">iPhone / iPad</a>
                         <a class="btn" href="/api/trust/ca.crt">Android</a>
                       </div>

                       <h3>Sur iPhone / iPad</h3>
                       <ol>
                         <li>Touchez <b>iPhone / iPad</b> ci-dessus, puis <b>Autoriser</b>.</li>
                         <li>Ouvrez <b>Réglages</b> : un encart <b>Profil téléchargé</b> apparaît en haut. Touchez-le, puis <b>Installer</b>.</li>
                         <li class="warn"><b>Étape indispensable :</b> allez dans <b>Réglages → Général → Informations → Certificats de confiance</b>
                             et activez l'interrupteur au nom de la clinique.<br>
                             Sans cette étape le certificat est installé mais <i>inactif</i>, et l'appareil affichera toujours un avertissement.</li>
                       </ol>

                       <h3>Sur Android</h3>
                       <ol>
                         <li>Touchez <b>Android</b> ci-dessus pour télécharger <code>ca.crt</code>.</li>
                         <li>Ouvrez <b>Paramètres → Sécurité → Chiffrement et identifiants → Installer un certificat →
                             Certificat CA</b>, acceptez l'avertissement, puis choisissez le fichier téléchargé.</li>
                         <li>Selon la marque, le chemin peut être <b>Paramètres → Sécurité → Autres paramètres de sécurité</b>.
                             Cherchez « installer un certificat ».</li>
                       </ol>

                       <h2>2 · Ouvrir l'application</h2>
                       <p>Une fois le certificat activé, ouvrez cette adresse :</p>
                       <p><a class="btn wide" href="{appUrl}">{appUrl}</a></p>
                       <p class="muted">Cette page-ci n'est pas l'application : elle sert uniquement à installer le certificat.</p>
               """
            : $"""
                       <div class="notice">
                         <h2>Rien à installer depuis cette page</h2>
                         <p>{WebUtility.HtmlEncode(CaMissingMessage)}</p>
                         <p>Adresse de l'application : <a href="{appUrl}">{appUrl}</a></p>
                       </div>
               """;


        // Single self-contained document: no external stylesheet, no script, no web font. The device loading
        // this has no trusted route to the server and may have no internet at all, so anything it cannot fetch
        // would render as a broken page at exactly the moment the user needs instructions.
        return $"""
                <!DOCTYPE html>
                <html lang="fr">
                <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
                <title>Accès sécurisé — configuration de l'appareil</title>
                <style>
                {PageStyles}
                </style>
                </head>
                <body>
                <h1>Accès sécurisé à la clinique</h1>
                <p class="muted">Cette page installe le certificat qui permet à cet appareil de se connecter au
                serveur de la clinique sans avertissement de sécurité.</p>
                {actions}
                <hr>
                <h2>Si l'avertissement persiste</h2>
                <ul>
                  <li><b>iPhone :</b> l'interrupteur de l'étape 3 n'est pas activé. C'est de loin la cause la plus fréquente.</li>
                  <li><b>Le navigateur refuse de continuer</b> sans proposer « Continuer quand même » : videz les données du
                      site pour cette adresse, ou essayez un autre navigateur, puis réinstallez le certificat.</li>
                  <li><b>Le serveur a été réinstallé :</b> son autorité a changé. Supprimez l'ancien profil ou certificat sur
                      l'appareil, puis reprenez cette page depuis le début.</li>
                  <li><b>L'adresse du serveur a changé</b> (nouveau bail DHCP) : le certificat ne couvre que les adresses
                      qu'avait le serveur au moment de sa création. Demandez une adresse IP fixe pour le serveur.</li>
                </ul>
                </body>
                </html>
                """;
    }

    /// <summary>
    /// The page's stylesheet, kept out of the interpolated template on purpose: CSS is almost entirely braces,
    /// and inside an interpolated raw string every one of them would have to be escaped — a transformation
    /// that silently changes the CSS if a single pair is missed. Held separately, the rules are the literal
    /// text that ships.
    /// </summary>
    private const string PageStyles = """
                  :root { color-scheme: light dark; }
                  * { box-sizing: border-box; }
                  body {
                    margin: 0 auto; padding: 1.5rem 1rem 3rem;
                    font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
                    line-height: 1.6; color: #16202b; background: #fdfdfe;
                    max-width: 42rem;
                  }
                  h1 { font-size: 1.4rem; margin: 0 0 .25rem; }
                  h2 { font-size: 1.1rem; margin: 2rem 0 .5rem; }
                  h3 { font-size: .95rem; margin: 1.5rem 0 .4rem; }
                  ol, ul { padding-inline-start: 1.25rem; }
                  li { margin-bottom: .5rem; }
                  code { background: #eef1f4; padding: .1em .35em; border-radius: .25rem; }
                  .muted { color: #5b6773; font-size: .875rem; }
                  .grid { display: flex; flex-wrap: wrap; gap: .75rem; }
                  .btn {
                    display: inline-block; flex: 1 1 10rem; text-align: center;
                    min-height: 44px; padding: .7rem 1rem;
                    background: #0f766e; color: #fff; text-decoration: none;
                    border-radius: .5rem; font-weight: 600;
                  }
                  .btn.wide { flex-basis: 100%; word-break: break-all; }
                  .qr {
                    display: block; margin: 1rem auto .25rem; image-rendering: pixelated;
                    background: #fff; padding: .5rem; border-radius: .5rem;
                  }
                  .warn {
                    background: #fff7ed; border-inline-start: 3px solid #d97706;
                    padding: .6rem .8rem; border-radius: .25rem; list-style-position: inside;
                  }
                  .notice { background: #eef1f4; padding: 1rem; border-radius: .5rem; }
                  hr { border: 0; border-top: 1px solid #dfe4e9; margin: 2rem 0; }
                  @media (prefers-color-scheme: dark) {
                    body { color: #e6ebf0; background: #10161d; }
                    code, .notice { background: #1d2630; }
                    .muted { color: #9aa7b4; }
                    .warn { background: #2a2114; }
                    hr { border-top-color: #2a343f; }
                  }
                """;
}
