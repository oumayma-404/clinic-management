using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The OS-push device registry. Clinic-owned, so every read here takes the global query filter — with one
/// deliberate, documented exception (<see cref="GetByTokenAcrossClinicsAsync"/>).
/// </summary>
public interface IDeviceRegistrationRepository
{
    Task<DeviceRegistration?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The registration holding this token, <b>whatever clinic it belongs to</b>.
    ///
    /// <para><b>Why it ignores the clinic filter, and why that is not a tenant leak (AC-41 vs AC-53).</b> The
    /// token is unique across the table — that uniqueness is what makes rebinding one write instead of a
    /// conflict — so a clinic-scoped lookup would miss a row belonging to another clinic and the insert that
    /// followed would hit the unique index as a 500. The caller must already <i>possess</i> the token, which the
    /// OS issued to their own app install on the device in their hand; nothing about the previous owner is
    /// returned to them, and the rebind moves the row to the caller's own clinic. The alternative — refusing —
    /// would brick push for a practitioner who works at two practices, or for the next person to sign in on a
    /// tablet that changed hands.</para>
    ///
    /// <para>It is named for what it does rather than hiding it behind <c>GetByTokenAsync</c>, so a future caller
    /// reaching for a cross-clinic read has to type the reason.</para>
    /// </summary>
    Task<DeviceRegistration?> GetByTokenAcrossClinicsAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every active device of these users, in one query. <b>Batched, never per-user</b>: the fan-out asks for a
    /// whole audience at once, and an appointment in a six-person practice would otherwise be six round trips
    /// inside a post-commit side effect.
    /// </summary>
    Task<IReadOnlyList<DeviceRegistration>> GetActiveForUsersAsync(
        Guid clinicId, IEnumerable<string> userIds, CancellationToken cancellationToken = default);

    /// <summary>The caller's own devices, newest-seen first — what « mes appareils » would read.</summary>
    Task<IReadOnlyList<DeviceRegistration>> GetActiveForUserAsync(
        Guid clinicId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Active registrations of this clinic on one platform — the count a settings surface states.</summary>
    Task<int> CountActiveAsync(
        Guid clinicId, DevicePlatform platform, CancellationToken cancellationToken = default);

    Task AddAsync(DeviceRegistration registration, CancellationToken cancellationToken = default);

    Task UpdateAsync(DeviceRegistration registration, CancellationToken cancellationToken = default);
}
