using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Services;

public interface INotificationService
{
    Task<bool> SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
    Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
    Task<bool> SendNotificationAsync(string email, string phoneNumber, NotificationType type, string subject, string message, CancellationToken cancellationToken = default);
}



