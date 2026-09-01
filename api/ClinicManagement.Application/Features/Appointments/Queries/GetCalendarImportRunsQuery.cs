using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Queries;

/// <summary>
/// « Imports Google » — what the calendar import has done to this cabinet, newest first, and which passes can
/// still be undone.
///
/// <para><b>A query, not a command</b>, for <c>GetVisitsToCloseQuery</c>'s mechanical reason: a read living under
/// <c>.Commands</c> would broadcast <c>appointments</c> on every page load and make every open browser in the
/// practice refetch its agenda.</para>
/// </summary>
public class GetCalendarImportRunsQuery : IRequest<Result<PagedResult<CalendarImportRunDto>>>
{
    public PageRequest? Paging { get; set; }

    /// <summary>
    /// Return only the one run worth offering an undo for — what the « Annuler cet import » banner asks.
    ///
    /// <para>The recurring pass writes a row every few hours and most of them create nothing, so the banner must
    /// not simply take the latest: it would put a destructive-looking button in front of a practice for no reason
    /// and hide the one import that actually filled its worklist behind a dozen that did not.</para>
    /// </summary>
    public bool LatestUndoableOnly { get; set; }
}

public class GetCalendarImportRunsQueryHandler
    : IRequestHandler<GetCalendarImportRunsQuery, Result<PagedResult<CalendarImportRunDto>>>
{
    private readonly ICalendarImportRunRepository _runRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetCalendarImportRunsQueryHandler> _logger;

    public GetCalendarImportRunsQueryHandler(
        ICalendarImportRunRepository runRepository,
        IUserRepository userRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetCalendarImportRunsQueryHandler> logger)
    {
        _runRepository = runRepository;
        _userRepository = userRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<PagedResult<CalendarImportRunDto>>> Handle(
        GetCalendarImportRunsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PagedResult<CalendarImportRunDto>>.Failure(
                    clinicResult.Error ?? ErrorMessages.Generic);
            }

            var clinicId = clinicResult.Value;

            if (request.LatestUndoableOnly)
            {
                var latest = await _runRepository.GetLatestUndoableAsync(clinicId, cancellationToken);

                if (latest is null)
                {
                    return Result<PagedResult<CalendarImportRunDto>>.Success(
                        PagedResult<CalendarImportRunDto>.Unpaged(Array.Empty<CalendarImportRunDto>()));
                }

                var contents = await _runRepository.GetContentsAsync(clinicId, latest.Id, cancellationToken);

                return Result<PagedResult<CalendarImportRunDto>>.Success(
                    PagedResult<CalendarImportRunDto>.Unpaged(new[]
                    {
                        CalendarImportRunPresentation.ToDto(
                            latest,
                            await ResolveActorNameAsync(latest.TriggeredByUserId, cancellationToken),
                            contents.Visits.Count + contents.Patients.Count)
                    }));
            }

            var page = await _runRepository.GetHistoryAsync(clinicId, request.Paging, cancellationToken);

            var rows = new List<CalendarImportRunDto>(page.Items.Count);

            foreach (var run in page.Items)
            {
                // A reverted run owns nothing, so the contents read is skipped for it — the common case on a
                // history page, and the one where the answer is known without asking.
                var remaining = run.IsReverted
                    ? 0
                    : await CountRemainingAsync(clinicId, run.Id, cancellationToken);

                rows.Add(CalendarImportRunPresentation.ToDto(
                    run,
                    await ResolveActorNameAsync(run.TriggeredByUserId, cancellationToken),
                    remaining));
            }

            return Result<PagedResult<CalendarImportRunDto>>.Success(
                new PagedResult<CalendarImportRunDto>(rows, page.Page, page.PageSize, page.TotalCount));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Failed to read the Google Calendar import runs");
            return Result<PagedResult<CalendarImportRunDto>>.Failure(ErrorMessages.Generic);
        }
    }

    private async Task<int> CountRemainingAsync(Guid clinicId, Guid runId, CancellationToken cancellationToken)
    {
        var contents = await _runRepository.GetContentsAsync(clinicId, runId, cancellationToken);
        return contents.Visits.Count + contents.Patients.Count;
    }

    /// <summary>
    /// A person's name for a manual run, or null when the actor is a job or an account that no longer exists.
    /// Resolution failures are silent by design: a run whose author has since been deleted still has to render.
    /// </summary>
    private async Task<string?> ResolveActorNameAsync(string actor, CancellationToken cancellationToken)
    {
        if (actor.StartsWith(Domain.Entities.CalendarImportRun.JobActorPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var user = await _userRepository.GetByIdAsync(actor, cancellationToken);
        return user?.FullName;
    }
}
