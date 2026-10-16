using System.Security.Claims;
using ClinicManagement.Domain.Entities;
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
///
/// <para>⚠️ <b>HTTP middleware does not run per hub invocation, so no tenant scope is ever set here</b>
/// (multi-tenant-cloud US-2). <c>TenantScopeMiddleware</c> runs on the negotiate request; a hub method runs in
/// its own DI scope, which lands in <c>TenantScopeKind.Unset</c> — and the global query filters now refuse that,
/// so a hub method touching a clinic-filtered entity would read <b>nothing, silently</b>. This hub is safe only
/// because it reads exactly one thing: <c>User</c>, which carries no filter. Any hub method that needs a
/// filtered entity must set the scope itself, from the clinic it resolves below. <c>ClinicHubTenantScopeTests</c>
/// fails if a filtered repository is injected here.</para>
/// </summary>
[Authorize]
public class ClinicHub : Hub
{
    /// <summary>
    /// Server → client event name. Carries a single string argument — the lowercase resource key that
    /// changed (e.g. "appointments", "patients") — so a client refetches only the views it owns.
    /// </summary>
    public const string EntityChanged = "entityChanged";

    /// <summary>Server → client, no argument: something in the staff chat changed for this tab (staff-chat).</summary>
    public const string StaffChatChanged = "staffChatChanged";

    private const string PresenceKey = "staff-chat-presence";

    private readonly IUserRepository _userRepository;
    private readonly StaffPresence _presence;

    public ClinicHub(IUserRepository userRepository, StaffPresence presence)
    {
        _userRepository = userRepository;
        _presence = presence;
    }

    public override async Task OnConnectedAsync()
    {
        var user = await ResolveUserAsync();
        if (user is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ClinicGroups.Name(user.ClinicId));
            await Groups.AddToGroupAsync(Context.ConnectionId, ClinicGroups.User(user.ClinicId, user.Id));

            Context.Items[PresenceKey] = (user.ClinicId, user.Id);
            if (_presence.Connected(user.ClinicId, user.Id))
            {
                await Clients.Group(ClinicGroups.Name(user.ClinicId)).SendAsync(StaffChatChanged);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(PresenceKey, out var value)
            && value is (Guid clinicId, string userId)
            && _presence.Disconnected(clinicId, userId))
        {
            await Clients.Group(ClinicGroups.Name(clinicId)).SendAsync(StaffChatChanged);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Resolves the connected user from the authenticated principal (mirrors <c>ClinicContext.GetUserId</c>) through
    /// the repository the REST handlers use — <c>IHttpContextAccessor</c> is not reliable in a hub connection.
    /// </summary>
    private async Task<User?> ResolveUserAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        return await _userRepository.GetByAuth0SubAsync(userId, Context.ConnectionAborted);
    }
}
