using System.Security.Claims;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Reads the acting console account off the request's principal — <see cref="IClinicContext"/>'s counterpart for
/// the second identity population.
///
/// <para><b>Gated on the token-kind claim, not on the shape of the subject.</b> Only the console's own issuer
/// emits <see cref="IPlatformSessionContext.TokenKindClaim"/>, so a clinic token — Auth0 <c>sub</c> or
/// <c>local|{guid}</c> — resolves to null here and falls through to the clinic path in
/// <c>AuditActorProvider</c>. Inferring it from « does the subject parse as a GUID » would be a rule about
/// today's id formats rather than about who issued the token.</para>
///
/// <para>It reads the claims rather than the account row on purpose: this is consulted on every audited save, and
/// <c>PlatformAccountStateMiddleware</c> has already refused the request if the account is deactivated or the
/// version is stale — so a second database read here would buy nothing and cost one query per save.</para>
/// </summary>
public class PlatformSessionContext : IPlatformSessionContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlatformSessionContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? GetAccountId()
    {
        var principal = ConsolePrincipal();
        if (principal is null)
        {
            return null;
        }

        var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? principal.FindFirst("sub")?.Value;

        return Guid.TryParse(subject, out var id) ? id : null;
    }

    public string? GetEmail() => ConsolePrincipal()?.FindFirst("email")?.Value;

    private ClaimsPrincipal? ConsolePrincipal()
    {
        var principal = _httpContextAccessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var kind = principal.FindFirst(IPlatformSessionContext.TokenKindClaim)?.Value;
        return string.Equals(kind, IPlatformSessionContext.PlatformTokenKind, StringComparison.Ordinal)
            ? principal
            : null;
    }
}
