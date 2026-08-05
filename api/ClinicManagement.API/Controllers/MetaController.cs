using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// What a client must be to use this server (AC-28). Anonymous by necessity: a shell below the floor has to be
/// able to ask <i>before</i> signing in, and the whole point of the answer is to reach a client the rest of the
/// API is refusing.
///
/// <para>That is also why <c>ClientVersionMiddleware</c> exempts this one route (AC-29) — without the exemption
/// the single route that says where to update would be the single route a stale client cannot read.</para>
/// </summary>
[ApiController]
[Route(RoutePrefix)]
// The single action below is deliberately [AllowAnonymous]; the class policy exists so a future action added
// here is covered rather than silently anonymous. Same shape as ConnectivityController.
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class MetaController : ApiControllerBase
{
    private const string RoutePrefix = "api/meta";
    private const string ClientRequirementsRoute = "client-requirements";

    /// <summary>
    /// The absolute path <c>ClientVersionMiddleware</c> exempts from the floor. Stated once, here, because the
    /// exemption and the route have to be the same string — a rename that moved only one of them would leave the
    /// update instructions unreachable by exactly the clients they exist for.
    /// </summary>
    public const string ClientRequirementsPath = "/" + RoutePrefix + "/" + ClientRequirementsRoute;

    private readonly IConfiguration _configuration;

    public MetaController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [AllowAnonymous]
    [HttpGet(ClientRequirementsRoute)]
    public IActionResult ClientRequirements() => Ok(Models.ClientRequirements.Read(_configuration));
}
