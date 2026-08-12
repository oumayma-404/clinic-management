using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Persistence for a clinic's per-clinic reminder settings (1:1 with the clinic; keyed by clinic id).
/// Mutations only stage changes — the caller commits via <c>IUnitOfWork</c>.
/// </summary>
public interface IClinicReminderSettingsRepository
{
    Task<ClinicReminderSettings?> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Which cabinet owns this WhatsApp Business Account — the resolution FR-7a's template-status webhook needs and
    /// which did not exist before Part 4 (this interface had <see cref="GetByClinicIdAsync"/> alone).
    ///
    /// <para>⚠️ <b>The one deliberately unfiltered read of this repository</b>, on
    /// <c>IDeviceRegistrationRepository.GetByTokenAcrossClinicsAsync</c>'s precedent: a WABA id is globally unique,
    /// the webhook is anonymous and carries no clinic, and the answer is needed <i>in order to</i> know whose row it
    /// is — so a scoped read structurally cannot find it. It is not a leak: the caller already holds a WABA id
    /// Meta signed for, and nothing about any other cabinet is returned.</para>
    /// </summary>
    Task<ClinicReminderSettings?> GetByWhatsAppBusinessAccountIdAsync(
        string businessAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every cabinet whose reminder template is still waiting on Meta — the reconciling poll's candidate set
    /// (FR-7a). Cross-cabinet by nature, so it is meaningful only under <c>UseSystemWide</c>.
    ///
    /// <para>A cabinet is a candidate when it is connected, holds a WABA id, and its stored template status is
    /// <b>not</b> one Meta will not move again by itself (<c>WhatsAppTemplateStatuses.IsTerminal</c>) — including
    /// the cabinet with <b>no</b> stored status at all, which is either a submission that failed or one that has
    /// never been made, and is exactly the stranding the poll exists to end.</para>
    /// </summary>
    Task<IReadOnlyList<ClinicReminderSettings>> GetAwaitingTemplateReviewAsync(
        CancellationToken cancellationToken = default);

    Task AddAsync(ClinicReminderSettings settings, CancellationToken cancellationToken = default);
    Task UpdateAsync(ClinicReminderSettings settings, CancellationToken cancellationToken = default);
}
