using System.Security.Claims;
using ClinicManagement.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ClinicManagement.API.Hubs;

/// <summary>
/// Real-time hub for clinic-scoped change notifications. On connect the caller's clinic id is
/// resolved server-side (the same DB lookup the REST handlers use) and the connection is added to
/// that clinic's group, so a broadcast reaches only clients of the same clinic (multi-tenant
/// isolation, AC-2). Requires an authenticated session in both auth modes (AC-3) — the connection's
/// bearer JWT is validated by the same mode-branched scheme as the REST API.
/// </summary>
[Authorize]
public class ClinicHub : Hub
{
    /// <summary>Server → client event name (no payload — signals the client to refetch).</summary>
    public const string AppointmentsChanged = "appointmentsChanged";

    private readonly IUserRepository _userRepository;

    public ClinicHub(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public override async Task OnConnectedAsync()
    {
        var clinicId = await ResolveClinicIdAsync();
        if (clinicId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ClinicGroups.Name(clinicId.Value));
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Resolves the connected user's clinic id from the authenticated principal. Reads the user id
    /// from the connection's claims (mirrors <c>ClinicContext.GetUserId</c>) then looks up the clinic
    /// via the same repository the REST handlers use — <c>IHttpContextAccessor</c> is not reliable in
    /// a hub connection, so the principal on <see cref="HubCallerContext.User"/> is the correct source.
    /// </summary>
    private async Task<Guid?> ResolveClinicIdAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var user = await _userRepository.GetByAuth0SubAsync(userId, Context.ConnectionAborted);
        return user?.ClinicId;
    }
}
