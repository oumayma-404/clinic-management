using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Queries;

/// <summary>
/// Admin-only (AC-3): the recent reminder outbox rows for the caller's clinic with their delivery state
/// (sent / pending / failed + reason), so a failed reminder is noticed instead of vanishing. Read-only over
/// existing <c>Notification</c> rows; the recipient phone is masked.
/// </summary>
public class GetClinicReminderStatusQuery : IRequest<Result<IReadOnlyList<ReminderStatusDto>>>
{
    public int Take { get; init; } = DefaultTake;

    public const int DefaultTake = 20;
    public const int MaxTake = 100;
}

public class GetClinicReminderStatusQueryHandler
    : IRequestHandler<GetClinicReminderStatusQuery, Result<IReadOnlyList<ReminderStatusDto>>>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetClinicReminderStatusQueryHandler(
        INotificationRepository notificationRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _notificationRepository = notificationRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<IReadOnlyList<ReminderStatusDto>>> Handle(
        GetClinicReminderStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<IReadOnlyList<ReminderStatusDto>>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (user == null)
            {
                return Result<IReadOnlyList<ReminderStatusDto>>.Failure("Utilisateur introuvable.");
            }

            if (!user.IsAdmin())
            {
                return Result<IReadOnlyList<ReminderStatusDto>>.Failure("Seuls les administrateurs peuvent consulter l'état d'envoi des rappels.");
            }

            var take = Math.Clamp(request.Take, 1, GetClinicReminderStatusQuery.MaxTake);
            var rows = await _notificationRepository.GetRecentByClinicIdAsync(user.ClinicId, take, cancellationToken);

            var dtos = rows.Select(ToDto).ToList();
            return Result<IReadOnlyList<ReminderStatusDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ReminderStatusDto>>.Failure($"Error retrieving reminder status: {ex.Message}");
        }
    }

    private static ReminderStatusDto ToDto(Notification n) => new()
    {
        Id = n.Id,
        Channel = n.Type.ToString(),
        RecipientMasked = MaskRecipient(n.Patient?.PhoneNumber?.Value),
        Status = n.Status switch
        {
            NotificationStatus.Sent => ReminderDeliveryStatus.Sent,
            NotificationStatus.Failed => ReminderDeliveryStatus.Failed,
            _ => ReminderDeliveryStatus.Pending,
        },
        FailureReason = string.IsNullOrWhiteSpace(n.ErrorMessage) ? null : n.ErrorMessage,
        ScheduledAt = n.ScheduledFor,
        SentAt = n.SentAt,
    };

    // Display-only PII mask (distinct from ReminderPhone.Mask, which lives in Infrastructure): show only the
    // last 2 digits, e.g. "•••• 56". Returns a French placeholder when the patient has no number.
    private static string MaskRecipient(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return "(aucun numéro)";
        }

        var trimmed = phone.Trim();
        return trimmed.Length <= 2 ? "••" : "••••" + trimmed[^2..];
    }
}
