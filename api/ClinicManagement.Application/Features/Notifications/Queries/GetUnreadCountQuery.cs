using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Notifications.Queries;

/// <summary>
/// The current user's total unread count for the header badge. Counts all unread notifications (due,
/// not actor-excluded, at/after the viewer's join baseline, no read marker) — independent of the 50-row
/// display window. The <c>99+</c> display cap is applied client-side.
/// </summary>
public class GetUnreadCountQuery : IRequest<Result<int>>
{
}

public class GetUnreadCountQueryHandler : IRequestHandler<GetUnreadCountQuery, Result<int>>
{
    private readonly IStaffNotificationRepository _notifications;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetUnreadCountQueryHandler(
        IStaffNotificationRepository notifications,
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _notifications = notifications;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<int>> Handle(GetUnreadCountQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<int>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<int>.Failure("Utilisateur introuvable.");
            }

            var now = DateTime.UtcNow;
            var count = await _notifications.CountUnreadAsync(user.ClinicId, userId, user.CreatedAt, now, cancellationToken);

            return Result<int>.Success(count);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<int>.Failure($"Error retrieving unread count: {ex.Message}");
        }
    }
}
