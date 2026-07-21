namespace ClinicManagement.Domain.Enums;

/// <summary>
/// A clinic's WhatsApp Business connection state (Meta Embedded Signup, Cloud onboarding). Stored as an
/// int on <see cref="Entities.ClinicReminderSettings"/>; the default is <see cref="NotConnected"/>.
/// </summary>
public enum WhatsAppConnectionStatus
{
    NotConnected = 0,
    Connected = 1,
    Error = 2
}
