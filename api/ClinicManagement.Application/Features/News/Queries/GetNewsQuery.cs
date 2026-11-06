using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.News.Queries;

/// <summary>The « Nouveautés » page. Reading it marks nothing: the page says so with <c>MarkNewsReadCommand</c>.</summary>
public class GetNewsQuery : IRequest<Result<NewsFeedDto>>;

public class GetNewsQueryHandler : IRequestHandler<GetNewsQuery, Result<NewsFeedDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly IPlatformNewsRepository _newsRepository;
    private readonly ILogger<GetNewsQueryHandler> _logger;

    public GetNewsQueryHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IPlatformNewsRepository newsRepository,
        ILogger<GetNewsQueryHandler> logger)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _newsRepository = newsRepository;
        _logger = logger;
    }

    public async Task<Result<NewsFeedDto>> Handle(GetNewsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            var user = string.IsNullOrWhiteSpace(userId)
                ? null
                : await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user is null)
            {
                return Result<NewsFeedDto>.Failure(ErrorMessages.Generic);
            }

            var marker = await _newsRepository.GetReadMarkerAsync(user.Id, cancellationToken);
            var readUpTo = NewsReading.ReadUpTo(marker, user);
            var rows = await _newsRepository.ListVisibleInAppAsync(cancellationToken);

            var entries = rows.Select(row => NewsReading.ToEntry(row, readUpTo)).ToList();
            return Result<NewsFeedDto>.Success(new NewsFeedDto(entries.Count(e => e.IsUnread), entries));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Reading the news page failed.");
            return Result<NewsFeedDto>.Failure(ErrorMessages.Generic);
        }
    }
}
