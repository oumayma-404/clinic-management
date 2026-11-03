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
/// One message from a cabinet's person to the vendor (guided-first-days E) — the week-one card or « Aide » →
/// « Donner mon avis ou une idée ». Every field is optional, but not all of them at once.
/// </summary>
public class SubmitFeedbackCommand : IRequest<Result<FeedbackReceiptDto>>
{
    /// <summary><c>idee</c> · <c>probleme</c> · <c>question</c> · <c>avis</c>.</summary>
    public string? Kind { get; set; }

    /// <summary><c>semaine-1</c> (closes this person's week-one question) or <c>aide</c>.</summary>
    public string? Source { get; set; }

    public int? HelpRating { get; set; }
    public int? EaseRating { get; set; }
    public List<string>? LikedAreas { get; set; }
    public List<string>? HardAreas { get; set; }
    public string? Message { get; set; }

    /// <summary>The screen the person was on, for context (« /appointments »). Free text, capped.</summary>
    public string? FromScreen { get; set; }

    public bool MayContact { get; set; }
}

public sealed record FeedbackReceiptDto(Guid Id);

/// <summary>The refusals, each with its code — a client branches on the code, never on the sentence.</summary>
public static class FeedbackRefusals
{
    public const string EmptyCode = "feedback_empty";
    public const string Empty = "Écrivez un mot ou choisissez une note avant d'envoyer.";

    public const string InvalidCode = "feedback_invalid";
    public const string Invalid = "Cet avis n'a pas pu être lu. Rechargez la page et réessayez.";
}

public class SubmitFeedbackCommandHandler : IRequestHandler<SubmitFeedbackCommand, Result<FeedbackReceiptDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IFeedbackRepository _feedbackRepository;
    private readonly IPlatformAccountRepository _platformAccounts;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubmitFeedbackCommandHandler> _logger;

    public SubmitFeedbackCommandHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IClinicRepository clinicRepository,
        IFeedbackRepository feedbackRepository,
        IPlatformAccountRepository platformAccounts,
        ITransactionalEmailSender emailSender,
        IUnitOfWork unitOfWork,
        ILogger<SubmitFeedbackCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _clinicRepository = clinicRepository;
        _feedbackRepository = feedbackRepository;
        _platformAccounts = platformAccounts;
        _emailSender = emailSender;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<FeedbackReceiptDto>> Handle(SubmitFeedbackCommand request, CancellationToken cancellationToken)
    {
        ProductFeedback feedback;
        User user;
        try
        {
            var userId = _clinicContext.GetUserId();
            var found = string.IsNullOrWhiteSpace(userId)
                ? null
                : await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (found is null)
            {
                return Result<FeedbackReceiptDto>.Failure(ErrorMessages.Generic);
            }

            user = found;

            var kind = request.Kind is null ? FeedbackRules.KindReview : FeedbackRules.ParseKind(request.Kind);
            var source = FeedbackRules.ParseSource(request.Source);
            var liked = FeedbackRules.ParseAreas(request.LikedAreas);
            var hard = FeedbackRules.ParseAreas(request.HardAreas);
            if (kind is null || source is null || liked is null || hard is null
                || request.HelpRating is < 1 or > 5 || request.EaseRating is < 1 or > 5)
            {
                return Result<FeedbackReceiptDto>.Failure(FeedbackRefusals.Invalid, FeedbackRefusals.InvalidCode);
            }

            if (request.HelpRating is null && request.EaseRating is null && liked.Count == 0 && hard.Count == 0
                && string.IsNullOrWhiteSpace(request.Message))
            {
                return Result<FeedbackReceiptDto>.Failure(FeedbackRefusals.Empty, FeedbackRefusals.EmptyCode);
            }

            feedback = new ProductFeedback(
                user.ClinicId, user.Id, user.Role, kind, source, request.HelpRating, request.EaseRating,
                liked, hard, request.Message, request.FromScreen, request.MayContact);
            await _feedbackRepository.AddAsync(feedback, cancellationToken);

            // An answer to the week-one card closes that question for good.
            if (source == FeedbackRules.SourceWeekOne)
            {
                var prompt = await _feedbackRepository.GetPromptAsync(user.Id, cancellationToken);
                if (prompt is null)
                {
                    prompt = new UserFeedbackPrompt(user.Id);
                    await _feedbackRepository.AddPromptAsync(prompt, cancellationToken);
                }

                prompt.Close(UserFeedbackPrompt.ClosedByAnswer);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Saving a feedback message failed.");
            return Result<FeedbackReceiptDto>.Failure(ErrorMessages.Generic);
        }

        // After the commit, best-effort: the message is kept in « Retours » whatever the mail server does.
        try
        {
            var clinic = await _clinicRepository.GetByIdAsync(user.ClinicId, cancellationToken);
            await ConsoleTeamFeedbackNotice.SendAsync(
                _platformAccounts,
                _emailSender,
                new FeedbackNoticeFacts(
                    clinic?.Name ?? "Cabinet",
                    user.FullName ?? user.Email ?? "—",
                    user.Role,
                    user.Email,
                    feedback),
                _logger,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The feedback notice could not be prepared.");
        }

        return Result<FeedbackReceiptDto>.Success(new FeedbackReceiptDto(feedback.Id));
    }
}
