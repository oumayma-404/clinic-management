using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;
using MediatR;

namespace ClinicManagement.Application.Features.Platform.Auth;

/// <summary>
/// What the console needs to know before it asks anybody to <b>choose</b> a password
/// (<c>hosted-security-hardening</c> FR-1.9).
///
/// <para><b>Why the console cannot read <c>GET /api/auth/mode</c>, which serves the same floor to the clinic
/// app.</b> The console reaches the API on its own listener (<c>CONSOLE_API_URL</c>, <c>http://api:5443/api</c>),
/// and <c>ConsolePortGate</c> refuses <b>both</b> directions on it: anything not under <c>/api/platform</c> is
/// 404 there, matched with <c>StartsWithSegments</c>. So the floor has to be published on the platform surface
/// or the console cannot learn it at all — and the alternative, restating <c>12</c> in the console's own source,
/// is the second authority this whole step exists to delete.</para>
///
/// <para>⚠️ <b>A MediatR query rather than a constant in the controller, deliberately.</b>
/// <c>PlatformReadShapeTests</c> derives what the console may return by reflecting over every
/// <c>Features.Platform</c> request's response type, so a value returned from an anonymous object in the
/// controller would be invisible to it. Going through a request is what puts <c>PasswordMinLength</c> in front of
/// that guard — and its set is asserted in <b>both</b> directions, so the name is reviewed on the way in and
/// cannot be left behind as a pre-approved hole if this read is ever removed.</para>
///
/// <para>It takes no dependencies and reads no database: the floor is a compile-time constant, and the point is
/// only that exactly one place decides it.</para>
/// </summary>
public record GetPlatformAuthMetaQuery : IRequest<Result<PlatformAuthMetaDto>>;

/// <summary>The console's half of the password policy. Carries no account and no cabinet.</summary>
public record PlatformAuthMetaDto(int PasswordMinLength);

public class GetPlatformAuthMetaQueryHandler
    : IRequestHandler<GetPlatformAuthMetaQuery, Result<PlatformAuthMetaDto>>
{
    public Task<Result<PlatformAuthMetaDto>> Handle(
        GetPlatformAuthMetaQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<PlatformAuthMetaDto>.Success(
            new PlatformAuthMetaDto(PasswordPolicy.MinLength)));
}
