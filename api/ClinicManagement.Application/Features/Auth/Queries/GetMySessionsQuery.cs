using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Auth.Queries;

/// <summary>
/// One live session of this account, as « Mes appareils » shows it.
/// </summary>
/// <param name="Id">The <c>SessionFamily</c>, and what <c>EndOtherSessionCommand</c> takes.</param>
/// <param name="DeviceLabel">
/// What the device called itself at sign-in, or <c>null</c>. Null is ordinary — most sign-ins send nothing — and
/// the screen says « appareil sans nom » rather than inventing one.
/// </param>
/// <param name="LastActiveAtUtc">
/// The last credential rotation, which is the closest thing to « last used » that exists here.
///
/// <para>⚠️ It is <b>not</b> the last human action. An open tab renews roughly every half hour on its own, so
/// this advances while nobody touches the machine — which is exactly why the screen labels it « dernière
/// activité » and not « dernière utilisation ». Reporting it as the latter would tell a user their unattended
/// reception PC was being used.</para>
/// </param>
/// <param name="IsTrusted">Whether « Rester connecté sur cet appareil » was ticked when it was opened.</param>
/// <param name="IsCurrent">
/// Whether this is the session making the request. <b>False for every row when the caller's token predates the
/// family claim</b> — an older access token names no chain — so the screen must treat « none marked » as « I
/// cannot tell », never as « none of these is me ».
/// </param>
public record SessionDeviceDto(
    Guid Id,
    string? DeviceLabel,
    DateTime CreatedAtUtc,
    DateTime LastActiveAtUtc,
    DateTime ExpiresAtUtc,
    bool IsTrusted,
    bool IsCurrent);

/// <summary>
/// « Mes appareils » — every session of the calling account that is still live.
///
/// <para><b>Why this had to ship with the long session and not after it.</b> A 30-day credential on a device you
/// cannot enumerate and cannot revoke is not a convenience, it is a hole: losing the laptop means a month of
/// access with the only lever being a password change, which signs every other device out too. The list and its
/// « Déconnecter » are what make trusting a device a decision that can be taken back.</para>
///
/// <para>⚠️ <b>A <c>Query</c>, and it must stay one.</b> <c>RealtimeBroadcastBehavior</c> derives its key from the
/// namespace, so a command here would broadcast into the clinic group every time somebody opened the security
/// page — telling every connected browser that something changed when nothing had.</para>
/// </summary>
public class GetMySessionsQuery : IRequest<Result<IReadOnlyList<SessionDeviceDto>>>;

public class GetMySessionsQueryHandler
    : IRequestHandler<GetMySessionsQuery, Result<IReadOnlyList<SessionDeviceDto>>>
{
    private readonly IClinicContext _clinicContext;
    private readonly ISessionFamilyRepository _sessionFamilies;

    public GetMySessionsQueryHandler(
        IClinicContext clinicContext,
        ISessionFamilyRepository sessionFamilies)
    {
        _clinicContext = clinicContext;
        _sessionFamilies = sessionFamilies;
    }

    public async Task<Result<IReadOnlyList<SessionDeviceDto>>> Handle(
        GetMySessionsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Result<IReadOnlyList<SessionDeviceDto>>.Failure(ErrorMessages.Generic);
            }

            // ⚠️ Scoped to the caller's OWN account id, never to a parameter. There is no legitimate reason for
            // one member of staff to enumerate a colleague's devices, and an admin-can-see-everything variant
            // would be a way to read when a colleague is at their desk.
            //
            // ⚠️ **Expired families are excluded, and the read is what excludes them.** A family whose credential
            // lapsed is neither ended nor usable; listing it would tell somebody checking after a theft that
            // devices are still signed in when none of them is.
            var families = await _sessionFamilies.GetLiveForUserAsync(userId, DateTime.UtcNow, cancellationToken);

            var currentFamilyId = _clinicContext.GetSessionFamilyId();

            var devices = families
                .Select(f => new SessionDeviceDto(
                    Id: f.Id,
                    DeviceLabel: f.DeviceLabel,
                    CreatedAtUtc: f.CreatedAt,
                    LastActiveAtUtc: f.LastRotatedAt,
                    ExpiresAtUtc: f.ExpiresAtUtc,
                    IsTrusted: f.IsTrusted,
                    // `currentFamilyId is not null &&` is load-bearing: without it a token carrying no family
                    // would compare null to null and mark EVERY row as « cet appareil ».
                    IsCurrent: currentFamilyId is not null && f.Id == currentFamilyId))
                .ToList();

            return Result<IReadOnlyList<SessionDeviceDto>>.Success(devices);
        }
        catch (Exception)
        {
            return Result<IReadOnlyList<SessionDeviceDto>>.Failure(ErrorMessages.Generic);
        }
    }
}
