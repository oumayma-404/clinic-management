using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Resolves the <b>effective</b> reminder settings for a clinic: per-clinic overrides where the clinic has
/// configured them, otherwise the per-install <c>Reminders</c> config. A null <paramref name="clinicId"/>
/// (legacy/global rows) resolves purely to the per-install config, preserving today's behavior.
/// Implemented in Infrastructure (it needs config + the secret protector + the settings repository).
/// </summary>
public interface IReminderSettingsProvider
{
    /// <summary>
    /// The channels enabled for the clinic (per-clinic toggles where set, else the install's
    /// <c>Reminders:Channels</c>). Used by the enqueuer to decide which reminder rows to create — no secrets
    /// are decrypted on this path.
    /// </summary>
    Task<IReadOnlyList<NotificationType>> ResolveEnabledChannelsAsync(Guid? clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The full effective settings (identity + decrypted secrets + per-install endpoints) used by the
    /// dispatcher/senders at send time.
    /// </summary>
    Task<ResolvedReminderSettings> ResolveAsync(Guid? clinicId, CancellationToken cancellationToken = default);
}
