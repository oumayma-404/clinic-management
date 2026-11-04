using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Demo;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// « Entrer dans la démo » (<c>demo-cabinet</c> US-1): a new, untrusted session for the current demo dentist, ending
/// at a fixed time. Each visitor gets a family of their own, so one leaving never signs another out.
/// </summary>
/// <remarks>
/// ⚠️ It writes the session row and nothing else — never the shared <see cref="User"/> row (no <c>LastLoginAt</c>),
/// whose concurrency token would make simultaneous visitors collide. <c>TradeRelayAssertionCommand</c> is the
/// precedent for a mint with no password.
/// </remarks>
public sealed record StartDemoSessionCommand : IRequest<Result<LoginResultDto>>;

public sealed class StartDemoSessionCommandHandler : IRequestHandler<StartDemoSessionCommand, Result<LoginResultDto>>
{
    public const string DeviceLabel = "Visite de démonstration";

    private readonly IDemoPolicy _demo;
    private readonly DemoCabinetLocator _locator;
    private readonly ISessionFamilyRepository _sessionFamilies;
    private readonly ILocalAuthService _localAuth;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditActorProvider _auditActor;
    private readonly ILogger<StartDemoSessionCommandHandler> _logger;

    public StartDemoSessionCommandHandler(
        IDemoPolicy demo,
        DemoCabinetLocator locator,
        ISessionFamilyRepository sessionFamilies,
        ILocalAuthService localAuth,
        IUnitOfWork unitOfWork,
        IAuditActorProvider auditActor,
        ILogger<StartDemoSessionCommandHandler> logger)
    {
        _demo = demo;
        _locator = locator;
        _sessionFamilies = sessionFamilies;
        _localAuth = localAuth;
        _unitOfWork = unitOfWork;
        _auditActor = auditActor;
        _logger = logger;
    }

    private static Result<LoginResultDto> Rebuilding() =>
        Result<LoginResultDto>.Failure(DemoRefusals.RebuildingMessage, DemoRefusals.RebuildingCode);

    public async Task<Result<LoginResultDto>> Handle(StartDemoSessionCommand request, CancellationToken cancellationToken)
    {
        // The controller 404s first; this is the second lock on an anonymous door.
        if (!_demo.IssuesDemoSessions)
        {
            return Rebuilding();
        }

        try
        {
            var cabinet = await _locator.CurrentAsync(cancellationToken);
            if (cabinet is null)
            {
                return Rebuilding();
            }

            var admin = cabinet.Admin;

            _auditActor.AuthenticatedAs(admin.Id, admin.Email);

            var family = new SessionFamily(
                admin.Id, SessionCredential.Hash(Guid.NewGuid().ToString()), DateTime.UtcNow, DeviceLabel);
            await _sessionFamilies.AddAsync(family, cancellationToken);

            var sessionEnd = family.CreatedAt + _demo.SessionLifetime;
            var refresh = _localAuth.GenerateRefreshToken(admin, family.Id, trusted: false, sessionEnd);
            family.Rotate(SessionCredential.Hash(refresh.AccessToken), refresh.ExpiresAtUtc);
            var access = _localAuth.GenerateToken(admin, family.Id, sessionEnd);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<LoginResultDto>.Success(new LoginResultDto
            {
                AccessToken = access.AccessToken,
                ExpiresAt = access.ExpiresAtUtc,
                RefreshToken = refresh.AccessToken,
                RefreshExpiresAt = refresh.ExpiresAtUtc,
                MustChangePassword = false,
                User = new UserDto
                {
                    Id = admin.Id,
                    ClinicId = admin.ClinicId,
                    Role = admin.Role,
                    Email = admin.Email,
                    FullName = admin.FullName,
                    CreatedAt = admin.CreatedAt,
                },
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Demo: a visitor's session could not be opened.");
            return Rebuilding();
        }
    }
}
