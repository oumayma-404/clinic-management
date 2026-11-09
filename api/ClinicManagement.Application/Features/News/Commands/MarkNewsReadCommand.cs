using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.News.Commands;

/// <summary>
/// The admin opened « Nouveautés »: what the page showed is now read. The marker moves to the newest news the page
/// showed (<see cref="UpTo"/>), never past the newest one sent, so a second visit with nothing new writes nothing.
/// </summary>
public class MarkNewsReadCommand : IRequest<Result>
{
    /// <summary>When the newest news on the page was sent. Absent: the newest news sent.</summary>
    public DateTime? UpTo { get; set; }
}

public class MarkNewsReadCommandHandler : IRequestHandler<MarkNewsReadCommand, Result>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly IPlatformNewsRepository _newsRepository;
    private readonly ILogger<MarkNewsReadCommandHandler> _logger;

    public MarkNewsReadCommandHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IPlatformNewsRepository newsRepository,
        ILogger<MarkNewsReadCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _newsRepository = newsRepository;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkNewsReadCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var user = await NewsReading.ResolveCallerAsync(_clinicContext, _userRepository, cancellationToken);
            if (user is null)
            {
                return Result.Failure(ErrorMessages.Generic);
            }

            if (await _newsRepository.GetNewestVisibleInAppAtAsync(cancellationToken) is not { } newest)
            {
                return Result.Success();
            }

            var readUpTo = request.UpTo is { } shown && shown.ToUniversalTime() < newest ? shown.ToUniversalTime() : newest;
            var marker = await _newsRepository.GetReadMarkerAsync(user.Id, cancellationToken);
            if (readUpTo <= NewsReading.ReadUpTo(marker, user))
            {
                return Result.Success();
            }

            await _newsRepository.AdvanceReadMarkerAsync(user.ClinicId, user.Id, readUpTo, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Marking the news as read failed.");
            return Result.Failure(ErrorMessages.Generic);
        }
    }
}
