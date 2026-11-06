using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.News.Commands;

/// <summary>
/// The admin opened « Nouveautés »: everything shown is now read. The marker moves to the newest news sent, not to
/// « now », so a second visit with nothing new writes nothing.
/// </summary>
public class MarkNewsReadCommand : IRequest<Result>;

public class MarkNewsReadCommandHandler : IRequestHandler<MarkNewsReadCommand, Result>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly IPlatformNewsRepository _newsRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MarkNewsReadCommandHandler> _logger;

    public MarkNewsReadCommandHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IPlatformNewsRepository newsRepository,
        IUnitOfWork unitOfWork,
        ILogger<MarkNewsReadCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _newsRepository = newsRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkNewsReadCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            var user = string.IsNullOrWhiteSpace(userId)
                ? null
                : await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user is null)
            {
                return Result.Failure(ErrorMessages.Generic);
            }

            var newest = await _newsRepository.GetNewestVisibleInAppAtAsync(cancellationToken);
            var marker = await _newsRepository.GetReadMarkerAsync(user.Id, cancellationToken);
            if (newest is not { } readUpTo || readUpTo <= NewsReading.ReadUpTo(marker, user))
            {
                return Result.Success();
            }

            if (marker is null)
            {
                await _newsRepository.AddReadMarkerAsync(
                    new PlatformNewsReadMarker(user.ClinicId, user.Id, readUpTo), cancellationToken);
            }
            else
            {
                marker.Advance(readUpTo);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Marking the news as read failed.");
            return Result.Failure(ErrorMessages.Generic);
        }
    }
}
