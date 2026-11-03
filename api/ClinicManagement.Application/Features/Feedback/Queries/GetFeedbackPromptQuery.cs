using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Feedback.Queries;

/// <summary>Whether to put the week-one question to the signed-in person now (guided-first-days E).</summary>
public class GetFeedbackPromptQuery : IRequest<Result<FeedbackPromptDto>>;

public sealed record FeedbackPromptDto(bool Show);

/// <summary>
/// The question is put after real use, never on day one: an account at least <see cref="FeedbackRules.MinAccountDays"/>
/// days old, in a cabinet that worked on at least <see cref="FeedbackRules.MinActiveDays"/> distinct days lately, at
/// most twice, never after an answer or « Ne plus demander ». The cheap checks run first; the activity read last.
///
/// <para>⚠️ The activity days are the vendor console's counters (<c>ClinicActivityDay</c>, unfiltered by design); the
/// read names this cabinet explicitly, from the caller's own account row.</para>
/// </summary>
public class GetFeedbackPromptQueryHandler : IRequestHandler<GetFeedbackPromptQuery, Result<FeedbackPromptDto>>
{
    private static readonly FeedbackPromptDto No = new(false);

    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly IFeedbackRepository _feedbackRepository;
    private readonly IClinicActivityRepository _activityRepository;
    private readonly ILogger<GetFeedbackPromptQueryHandler> _logger;

    public GetFeedbackPromptQueryHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IFeedbackRepository feedbackRepository,
        IClinicActivityRepository activityRepository,
        ILogger<GetFeedbackPromptQueryHandler> logger)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _feedbackRepository = feedbackRepository;
        _activityRepository = activityRepository;
        _logger = logger;
    }

    public async Task<Result<FeedbackPromptDto>> Handle(GetFeedbackPromptQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            var user = string.IsNullOrWhiteSpace(userId)
                ? null
                : await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user is null)
            {
                return Result<FeedbackPromptDto>.Failure(ErrorMessages.Generic);
            }

            var now = DateTime.UtcNow;
            if (now - user.CreatedAt < TimeSpan.FromDays(FeedbackRules.MinAccountDays))
            {
                return Result<FeedbackPromptDto>.Success(No);
            }

            var prompt = await _feedbackRepository.GetPromptAsync(user.Id, cancellationToken);
            if (prompt is not null && !prompt.MayShow(now))
            {
                return Result<FeedbackPromptDto>.Success(No);
            }

            var today = DateOnly.FromDateTime(ClinicClock.ClinicToday(now));
            var days = await _activityRepository.GetDaysAsync(
                user.ClinicId, today.AddDays(-FeedbackRules.ActiveDaysLookback), today, cancellationToken);
            var activeDays = days.Count(d => d.Writes > 0);

            return Result<FeedbackPromptDto>.Success(new FeedbackPromptDto(activeDays >= FeedbackRules.MinActiveDays));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Reading the feedback prompt failed.");
            return Result<FeedbackPromptDto>.Failure(ErrorMessages.Generic);
        }
    }
}
