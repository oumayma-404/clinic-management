using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// A due, unread post-visit review the popup should surface. Carries the appointment id so the "Add
/// medical record" action can deep-link to record creation for that visit. <see cref="VisibleAt"/> is the
/// effective feed time (the appointment end) at which the review became due.
/// </summary>
public class PendingReviewDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? AppointmentId { get; set; }
    public DateTime VisibleAt { get; set; }
}

public static class PendingReviewMappingExtensions
{
    public static PendingReviewDto ToPendingReviewDto(this StaffNotification notification) => new()
    {
        Id = notification.Id,
        Title = notification.Title,
        Message = notification.Message,
        AppointmentId = notification.AppointmentId,
        VisibleAt = notification.EffectiveFeedTime
    };
}
