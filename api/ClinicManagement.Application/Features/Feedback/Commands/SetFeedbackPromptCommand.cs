using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Feedback.Commands;

/// <summary>
/// The week-one card's three non-answers (guided-first-days E): <c>shown</c> (it appeared — counts towards the two
/// showings), <c>later</c> (a week, once) and <c>never</c>. An answer closes it through <c>SubmitFeedbackCommand</c>.
/// </summary>
public class SetFeedbackPromptCommand : IRequest<Result>
{
    public string? Action { get; set; }
}

public class SetFeedbackPromptCommandHandler : IRequestHandler<SetFeedbackPromptCommand, Result>
{
    public const string ActionShown = "shown";
    public const string ActionLater = "later";
    public const string ActionNever = "never";

    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly IFeedbackRepository _feedbackRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetFeedbackPromptCommandHandler> _logger;

    public SetFeedbackPromptCommandHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IFeedbackRepository feedbackRepository,
        IUnitOfWork unitOfWork,
        ILogger<SetFeedbackPromptCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _feedbackRepository = feedbackRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(SetFeedbackPromptCommand request, CancellationToken cancellationToken)
    {
        var action = request.Action?.Trim().ToLowerInvariant();
        if (action is not (ActionShown or ActionLater or ActionNever))
        {
            return Result.Failure(FeedbackRefusals.Invalid, FeedbackRefusals.InvalidCode);
        }

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

            var prompt = await _feedbackRepository.GetPromptAsync(user.Id, cancellationToken);
            if (prompt is null)
            {
                prompt = new UserFeedbackPrompt(user.Id);
                await _feedbackRepository.AddPromptAsync(prompt, cancellationToken);
            }

            switch (action)
            {
                case ActionShown:
                    prompt.MarkShown();
                    break;
                case ActionLater:
                    prompt.Later();
                    break;
                default:
                    prompt.Close(UserFeedbackPrompt.ClosedByNever);
                    break;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Updating the feedback prompt failed.");
            return Result.Failure(ErrorMessages.Generic);
        }
    }
}
