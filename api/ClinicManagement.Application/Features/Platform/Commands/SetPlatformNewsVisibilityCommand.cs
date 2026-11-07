using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Commands;

/// <summary>
/// « Masquer dans l'app » / « Afficher dans l'app »: whether a sent news stays on the cabinets' « Nouveautés » page.
/// The e-mails already sent are untouched, and pressing twice is a no-op rather than a refusal.
/// </summary>
/// <remarks>
/// Refused while the e-mails are still going out: the dispatcher holds the same row and its closing save would meet
/// the version this write moved.
/// </remarks>
public class SetPlatformNewsVisibilityCommand : IRequest<Result<PlatformNewsVisibilityDto>>
{
    public Guid BroadcastId { get; set; }
    public bool Visible { get; set; }
}

public class SetPlatformNewsVisibilityCommandHandler
    : IRequestHandler<SetPlatformNewsVisibilityCommand, Result<PlatformNewsVisibilityDto>>
{
    public const string NotFoundCode = "news_not_found";
    public const string NotFound = "Cette nouveauté n'existe plus.";

    public const string InProgressCode = "news_in_progress";
    public const string InProgress = "L'envoi de cette nouveauté est en cours. Réessayez une fois l'envoi terminé.";

    private readonly IPlatformNewsRepository _news;
    private readonly IPlatformSessionContext _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetPlatformNewsVisibilityCommandHandler> _logger;

    public SetPlatformNewsVisibilityCommandHandler(
        IPlatformNewsRepository news,
        IPlatformSessionContext session,
        IUnitOfWork unitOfWork,
        ILogger<SetPlatformNewsVisibilityCommandHandler> logger)
    {
        _news = news;
        _session = session;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<PlatformNewsVisibilityDto>> Handle(
        SetPlatformNewsVisibilityCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var broadcast = await _news.GetWithoutRecipientsAsync(request.BroadcastId, cancellationToken);
            if (broadcast is null)
            {
                return Result<PlatformNewsVisibilityDto>.Failure(NotFound, NotFoundCode);
            }

            if (broadcast.CompletedAtUtc is null)
            {
                return Result<PlatformNewsVisibilityDto>.Failure(InProgress, InProgressCode);
            }

            var changed = request.Visible ? broadcast.ShowInApp() : broadcast.HideFromApp(DateTime.UtcNow);
            if (changed)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Console account {AccountId} {Action} news {BroadcastId} in the app.",
                    _session.GetAccountId(), request.Visible ? "showed" : "hid", broadcast.Id);
            }

            return Result<PlatformNewsVisibilityDto>.Success(
                new PlatformNewsVisibilityDto(broadcast.Id, broadcast.IsVisibleInApp));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Changing whether news {BroadcastId} shows in the app failed.", request.BroadcastId);
            return Result<PlatformNewsVisibilityDto>.Failure(ErrorMessages.Generic);
        }
    }
}
