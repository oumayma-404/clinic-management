using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// « Déconnecter cet appareil » — ends one of the caller's own sessions from « Mes appareils ».
///
/// <para><b>The counterpart of the long session.</b> Trusting a device is only a safe thing to offer because it
/// can be untrusted from somewhere else: a laptop left at a conference, a phone sold, a shared PC somebody
/// ticked the box on by mistake. Without this, the only lever over a 30-day credential is a password change,
/// which bumps <c>TokenVersion</c> and therefore signs the user out of every device including the one in their
/// hand — a blunt instrument that punishes the ordinary case.</para>
///
/// <para>⚠️ <b>It ends a family; it does not touch <c>TokenVersion</c>.</b> Same rule as
/// <c>EndSessionCommand</c>: one device, never the account. Bumping the version here would make « remove the
/// laptop I lost » sign the dentist's tablet out mid-consultation.</para>
///
/// <para>⚠️ <b>Ending the session you are calling from is allowed.</b> It is a coherent request — « sign this
/// browser out from here » — and refusing it would be a rule the user has to discover by hitting it. The screen
/// marks that row « cet appareil » and confirms differently; the API does not need a second opinion.</para>
/// </summary>
public class EndOtherSessionCommand : IRequest<Result>
{
    public Guid SessionId { get; set; }
}

public class EndOtherSessionCommandHandler : IRequestHandler<EndOtherSessionCommand, Result>
{
    /// <summary>Stamped on the family so the journal can tell this from a timeout and from a replay kill.</summary>
    public const string Reason = "Appareil déconnecté depuis « Mes appareils »";

    /// <summary>
    /// One refusal for « no such session » and for « somebody else's session » alike.
    ///
    /// <para>⚠️ Distinguishing them would answer « does this session id exist? » for ids the caller does not own,
    /// which is a membership oracle over a table keyed by GUID. There is nothing a caller could do differently
    /// with the two answers: in both cases the device is not theirs to end.</para>
    /// </summary>
    private const string NotFoundError = "Cette session n'existe pas ou n'est pas la vôtre.";

    private readonly IClinicContext _clinicContext;
    private readonly ISessionFamilyRepository _sessionFamilies;
    private readonly IUnitOfWork _unitOfWork;

    public EndOtherSessionCommandHandler(
        IClinicContext clinicContext,
        ISessionFamilyRepository sessionFamilies,
        IUnitOfWork unitOfWork)
    {
        _clinicContext = clinicContext;
        _sessionFamilies = sessionFamilies;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(EndOtherSessionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Result.Failure(ErrorMessages.Generic);
            }

            var family = await _sessionFamilies.GetByIdAsync(request.SessionId, cancellationToken);

            // ⚠️ The ownership comparison is the whole authorization of this endpoint. `SessionFamily` carries no
            // `ClinicId` — deliberately, see the entity — so no query filter stands behind it and nothing else
            // would stop one user ending another's session by guessing an id.
            if (family is null || family.UserId != userId)
            {
                return Result.Failure(NotFoundError);
            }

            // Already ended is a success, not a refusal: two tabs pressing « Déconnecter » on the same row is
            // ordinary, and the caller's intent is satisfied either way.
            if (family.IsLive)
            {
                family.End(Reason);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is not Common.Exceptions.ConflictException)
        {
            return Result.Failure(ErrorMessages.Generic, ex);
        }
    }
}
