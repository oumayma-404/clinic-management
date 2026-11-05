using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Users;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Auth.Queries;

/// <summary>
/// What the invitation page shows before a password is chosen (<c>guided-first-days</c> C): « Bonjour Amel », the
/// identifiant, the cabinet. Anonymous — the token is the credential — and a POST so it never lands in a URL or a log.
/// </summary>
public class PreviewStaffInvitationQuery : IRequest<Result<StaffInvitationPreviewDto>>
{
    public string Token { get; set; } = string.Empty;
}

/// <summary>The name exactly as the admin typed it — the e-mail greets the same way (« Dr Ross » is not « Dr »).</summary>
public sealed record StaffInvitationPreviewDto(string FullName, string Email, string ClinicName);

public class PreviewStaffInvitationQueryHandler
    : IRequestHandler<PreviewStaffInvitationQuery, Result<StaffInvitationPreviewDto>>
{
    private readonly IStaffInvitationRepository _invitations;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly ILogger<PreviewStaffInvitationQueryHandler> _logger;

    public PreviewStaffInvitationQueryHandler(
        IStaffInvitationRepository invitations,
        IUserRepository users,
        IClinicRepository clinics,
        ILogger<PreviewStaffInvitationQueryHandler> logger)
    {
        _invitations = invitations;
        _users = users;
        _clinics = clinics;
        _logger = logger;
    }

    public async Task<Result<StaffInvitationPreviewDto>> Handle(
        PreviewStaffInvitationQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var found = await StaffInvitationLink.ResolveAsync(
                _invitations, _users, request.Token, DateTime.UtcNow, cancellationToken);
            if (found is null)
            {
                return Result<StaffInvitationPreviewDto>.Failure(StaffInvitations.UnusableLink, StaffInvitations.InvalidCode);
            }

            var (_, user) = found.Value;
            var clinic = await _clinics.GetByIdAsync(user.ClinicId, cancellationToken);

            return Result<StaffInvitationPreviewDto>.Success(
                new StaffInvitationPreviewDto((user.FullName ?? string.Empty).Trim(), user.Email ?? string.Empty, clinic?.Name ?? string.Empty));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Reading a staff invitation failed.");
            // No code: a server fault must not read as « lien expiré », or she asks for a resend that kills a good link.
            return Result<StaffInvitationPreviewDto>.Failure("L'invitation n'a pas pu être lue. Réessayez dans un instant — votre lien reste valable.");
        }
    }
}

/// <summary>
/// The one reading of an invitation link, shared by the preview and the accept so they cannot disagree on what is
/// usable: a matching hash (fixed-time compare), unused, unexpired, an active local account that has never signed in.
/// </summary>
public static class StaffInvitationLink
{
    public static async Task<(StaffInvitation Invitation, User User)?> ResolveAsync(
        IStaffInvitationRepository invitations,
        IUserRepository users,
        string? rawToken,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var candidate = StaffInvitation.HashToken(rawToken.Trim());
        var row = await invitations.GetByTokenHashAsync(candidate, cancellationToken);
        if (row is null || !StaffInvitation.TokenHashMatches(row.TokenHash, candidate) || !row.IsUsable(nowUtc))
        {
            return null;
        }

        var user = await users.GetByIdAsync(row.UserId, cancellationToken);
        if (user is null || !user.IsLocalAccount() || !user.IsActive || user.LastLoginAt is not null)
        {
            return null;
        }

        return (row, user);
    }
}
