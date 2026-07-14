using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Notifications.Commands;

/// <summary>
/// Marks a single notification read for the current user. Idempotent. A notification from another clinic
/// reads as "not found" (tenant-isolation convention).
/// </summary>
public class MarkNotificationReadCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand, Result>
{
    private readonly IStaffNotificationRepository _notifications;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public MarkNotificationReadCommandHandler(
        IStaffNotificationRepository notifications,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork)
    {
        _notifications = notifications;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Failure("User ID not found in token");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result.Failure("User not found");
            }

            var notification = await _notifications.GetByIdAsync(request.Id, cancellationToken);
            // Cross-clinic (or missing) reads as not-found — never confirm another clinic's id exists.
            if (notification == null || notification.ClinicId != user.ClinicId)
            {
                return Result.Failure("Notification not found");
            }

            // Idempotent: only insert a marker if one doesn't already exist.
            if (!await _notifications.ReadMarkerExistsAsync(request.Id, userId, cancellationToken))
            {
                await _notifications.AddReadMarkerAsync(new NotificationRead(request.Id, userId), cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Error marking notification read: {ex.Message}");
        }
    }
}
