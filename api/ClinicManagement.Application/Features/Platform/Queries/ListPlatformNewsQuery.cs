using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Queries;

/// <summary>The recent news sends, newest first, with what reached whom.</summary>
public class ListPlatformNewsQuery : IRequest<Result<IReadOnlyList<PlatformNewsBroadcastDto>>>
{
}

public class ListPlatformNewsQueryHandler
    : IRequestHandler<ListPlatformNewsQuery, Result<IReadOnlyList<PlatformNewsBroadcastDto>>>
{
    public const int HistorySize = 20;

    private readonly IPlatformNewsRepository _news;
    private readonly ILogger<ListPlatformNewsQueryHandler> _logger;

    public ListPlatformNewsQueryHandler(IPlatformNewsRepository news, ILogger<ListPlatformNewsQueryHandler> logger)
    {
        _news = news;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<PlatformNewsBroadcastDto>>> Handle(
        ListPlatformNewsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _news.ListRecentAsync(HistorySize, cancellationToken);

            return Result<IReadOnlyList<PlatformNewsBroadcastDto>>.Success(rows
                .Select(r => new PlatformNewsBroadcastDto(
                    r.Id,
                    r.Subject,
                    r.CreatedAtUtc,
                    r.SentByName,
                    r.Clinics,
                    r.Recipients,
                    r.Sent,
                    r.Failed,
                    r.CompletedAtUtc is null,
                    r.FailedClinics))
                .ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading the news history");
            return Result<IReadOnlyList<PlatformNewsBroadcastDto>>.Failure("Impossible de lire l'historique des envois.", ex);
        }
    }
}
