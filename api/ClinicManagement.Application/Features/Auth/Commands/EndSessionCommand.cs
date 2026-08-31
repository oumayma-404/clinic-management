using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// Signing out ends the session <b>on the server</b> (not just in the browser).
///
/// <para><b>What this fixes.</b> There was no revoke endpoint on the API at all: <c>/bff/auth/local-logout</c>
/// cleared the cookies and stopped there, and <c>SessionFamily.End</c> was called from exactly one place — replay
/// detection inside <c>RefreshTokenCommand</c>. So a refresh credential captured before sign-out stayed valid for
/// its full <b>12 hours</b> and kept rotating itself indefinitely. « Se déconnecter » on a shared reception PC
/// removed the cookie from that browser and revoked nothing.</para>
///
/// <para><b>The credential is the authentication</b>, which is why this needs no session and is safe as an
/// anonymous endpoint: the caller proves possession of the very thing being revoked. Requiring a valid access
/// token instead would be worse — a browser signing out after its 30-minute access token has expired is the
/// normal case, and it is exactly the case that most needs the refresh credential killed.</para>
///
/// <para>⚠️ <b>Every outcome is the same success.</b> An unknown, expired or already-ended credential answers
/// exactly as a revoked one does. Signing out is not a place to learn whether a credential was real, and there is
/// nothing a caller could usefully do differently — the browser is discarding it either way. This also makes the
/// endpoint idempotent, which matters because sign-out fires while the session is already being torn down.</para>
///
/// <para>⚠️ <b>It ends ONE family, never the account.</b> A family is one device's chain, so signing out at the
/// reception desk must not sign the dentist's tablet out mid-consultation. <c>TokenVersion</c> remains the
/// account-wide lever, and it is deliberately not touched here — bumping it on an ordinary sign-out would be the
/// « signing somebody out of every device for good hygiene » that <c>RegenerateRecoveryCodesCommand</c> already
/// declined to do.</para>
/// </summary>
public class EndSessionCommand : IRequest<Result>
{
    /// <summary>The refresh credential being retired — the value held in the BFF's HttpOnly cookie.</summary>
    public string RefreshToken { get; set; } = string.Empty;
}

public class EndSessionCommandHandler : IRequestHandler<EndSessionCommand, Result>
{
    private readonly ISessionFamilyRepository _sessionFamilies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<EndSessionCommandHandler> _logger;

    public EndSessionCommandHandler(
        ISessionFamilyRepository sessionFamilies,
        IUnitOfWork unitOfWork,
        ILogger<EndSessionCommandHandler> logger)
    {
        _sessionFamilies = sessionFamilies;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>The reason stamped on the family, so the journal distinguishes this from a replay kill.</summary>
    public const string Reason = "Déconnexion demandée par l'utilisateur";

    public async Task<Result> Handle(EndSessionCommand request, CancellationToken cancellationToken)
    {
        // A missing credential is not an error: a browser whose cookie has already gone still calls this on its
        // way out, and answering 400 would put a French failure toast on a successful sign-out.
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result.Success();
        }

        try
        {
            var family = await _sessionFamilies.GetByCredentialAsync(
                SessionCredential.Hash(request.RefreshToken), cancellationToken);

            // ⚠️ `IsLive` is checked rather than assumed: `End` throws on a family already ended, and a
            // double-submitted sign-out is ordinary rather than exceptional.
            if (family is { IsLive: true })
            {
                family.End(Reason);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Logged, never surfaced. The browser is discarding the credential regardless, and a failure here
            // must not leave a user staring at an error on a screen that has already signed them out. The
            // exposure this leaves is the one that existed before the endpoint was written, not a new one.
            _logger.LogError(ex, "Failed to end a session family on sign-out.");
        }

        return Result.Success();
    }
}
