using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.News.Queries;

/// <summary>The sidebar's badge on « Nouveautés ». A count, so the rail never loads the news themselves.</summary>
public class GetNewsSummaryQuery : IRequest<Result<NewsSummaryDto>>;

public class GetNewsSummaryQueryHandler : IRequestHandler<GetNewsSummaryQuery, Result<NewsSummaryDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly IPlatformNewsRepository _newsRepository;
    private readonly ILogger<GetNewsSummaryQueryHandler> _logger;

    public GetNewsSummaryQueryHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IPlatformNewsRepository newsRepository,
        ILogger<GetNewsSummaryQueryHandler> logger)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _newsRepository = newsRepository;
        _logger = logger;
    }

    public async Task<Result<NewsSummaryDto>> Handle(GetNewsSummaryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            var user = string.IsNullOrWhiteSpace(userId)
                ? null
                : await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user is null)
            {
                return Result<NewsSummaryDto>.Failure(ErrorMessages.Generic);
            }

            var marker = await _newsRepository.GetReadMarkerAsync(user.Id, cancellationToken);
            var unread = await _newsRepository.CountVisibleInAppAfterAsync(
                NewsReading.ReadUpTo(marker, user), cancellationToken);

            return Result<NewsSummaryDto>.Success(new NewsSummaryDto(unread));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Reading the news badge failed.");
            return Result<NewsSummaryDto>.Failure(ErrorMessages.Generic);
        }
    }
}
