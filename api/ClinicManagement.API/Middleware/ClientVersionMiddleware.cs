using ClinicManagement.API.Controllers;
using ClinicManagement.API.Models;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// Refuses an API call from a native shell older than the operator's floor with <b>426 Upgrade Required</b>
/// (AC-30), so a build whose bridge no longer matches the server says so instead of failing screen by screen.
///
/// <para><b>Placed before <c>UseAuthentication</c>, which departs from the blueprint on purpose (plan R-11).</b>
/// The refusal needs no principal, and running it first means a stale shell's <i>login</i> also 426s rather than
/// 401ing — 401 is what the client reads as « signed out », and AC-33 requires the opposite: the app must show
/// « mettez à jour », not a login screen it can never get past.</para>
///
/// <para><b>Scoped to <c>/api</c>.</b> AC-30 says « every API route », and the scope is load-bearing rather than
/// tidy: in a self-hosted install Kestrel also serves the web app through the YARP catch-all, so refusing every
/// path would 426 the page itself — leaving the shell showing raw JSON instead of the French update state that
/// same page renders. The BFF routes and the realtime hub sit outside <c>/api</c> and are unaffected, which is
/// also what AC-32 asks for.</para>
///
/// <para>Nothing here asks what kind of deployment this is (AC-70): a client too old for the server is too old
/// on a clinic's own PC and in a datacentre alike.</para>
/// </summary>
public class ClientVersionMiddleware
{
    /// <summary>What the shells send. A browser sends nothing, which is what keeps the floor off it (AC-32).</summary>
    public const string HeaderName = "X-Client-Version";

    /// <summary>The machine-readable tag on the 426 body — mirrored by <c>ApiErrorCode.ClientTooOld</c>.</summary>
    public const string TooOldCode = "client_too_old";

    private const string ApiPrefix = "/api";

    private const string RefusalMessage =
        "Cette version de l'application n'est plus prise en charge. Mettez-la à jour pour continuer.";

    private readonly RequestDelegate _next;

    public ClientVersionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        var reported = context.Request.Headers[HeaderName].ToString();

        // Read per request rather than once at construction so an operator can raise the floor under a running
        // server — which is AC-33's mid-session case, not a hypothetical.
        if (Applies(context.Request.Path)
            && ClientRequirements.Read(configuration).IsBelowFloor(reported))
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsJsonAsync(new { error = RefusalMessage, code = TooOldCode });
            return;
        }

        await _next(context);
    }

    private static bool Applies(PathString path) =>
        path.StartsWithSegments(ApiPrefix)
        && !path.StartsWithSegments(MetaController.ClientRequirementsPath);
}
