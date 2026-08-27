using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Notifications.Queries;

/// <summary>
/// The current user's due, unread post-visit review notifications — drives the "how was the visit" popup.
/// Visibility mirrors the unread predicate (due, at/after join baseline, no read marker) and honors the
/// per-notification target user, so a doctor-targeted review reaches only that doctor.
/// </summary>
public class GetPendingReviewsQuery : IRequest<Result<IEnumerable<PendingReviewDto>>>
{
}

public class GetPendingReviewsQueryHandler : IRequestHandler<GetPendingReviewsQuery, Result<IEnumerable<PendingReviewDto>>>
{
    private readonly IStaffNotificationRepository _notifications;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly ILogger<GetPendingReviewsQueryHandler> _logger;

    public GetPendingReviewsQueryHandler(
        IStaffNotificationRepository notifications,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        ILogger<GetPendingReviewsQueryHandler> logger)
    {
        _notifications = notifications;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _logger = logger;
    }

    public async Task<Result<IEnumerable<PendingReviewDto>>> Handle(GetPendingReviewsQuery request, CancellationToken cancellationToken)
    {
        string? userId = null;
        try
        {
            userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<IEnumerable<PendingReviewDto>>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<IEnumerable<PendingReviewDto>>.Failure("Utilisateur introuvable.");
            }

            var now = DateTime.UtcNow;
            var reviews = await _notifications.GetPendingReviewsForUserAsync(user.ClinicId, userId, user.CreatedAt, now, cancellationToken);

            var dtos = reviews.Select(r => r.ToPendingReviewDto()).ToList();
            return Result<IEnumerable<PendingReviewDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error retrieving pending reviews for user {UserId}", userId);
            return Result<IEnumerable<PendingReviewDto>>.Failure("Erreur lors du chargement des visites à saisir.");
        }
    }
}
