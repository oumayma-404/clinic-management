using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One installed app on one device, and who is signed into it — the registry OS push is delivered against.
///
/// <para><b>Unique on <see cref="Token"/></b>, which is what makes <b>rebinding</b> one deterministic write
/// rather than a conflict to resolve (AC-41). A phone or tablet at the reception desk is shared: the assistant(e)
/// signs out, the dentist signs in, and the OS hands the app the <i>same</i> token. Two rows for one token would
/// mean the previous user keeps receiving the notifications of a session they have left — on a device someone
/// else is holding.</para>
///
/// <para><see cref="UserId"/> is a <b>string</b> because <c>User</c> is: its id is the Auth0 <c>sub</c> or
/// <c>local|{guid}</c>, not a GUID.</para>
/// </summary>
public class DeviceRegistration : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }

    /// <summary>The signed-in user this device currently delivers to.</summary>
    public string UserId { get; private set; } = string.Empty;

    public DevicePlatform Platform { get; private set; }

    /// <summary>The FCM registration token or APNs device token. Opaque, and the natural key.</summary>
    public string Token { get; private set; } = string.Empty;

    /// <summary>
    /// The shell build that registered, for operator diagnosis only — never a delivery decision. The version
    /// floor is <c>ClientVersionMiddleware</c>'s business and it refuses the request outright, so a row here
    /// can only have come from a build the server already accepts.
    /// </summary>
    public string? ShellVersion { get; private set; }

    public DateTime LastSeenAt { get; private set; }

    /// <summary>
    /// False once the app was uninstalled (the platform reported the token unregistered — AC-49), the user
    /// signed out (AC-40), or the token was rebound to somebody else (AC-41).
    ///
    /// <para>Deactivated rather than deleted so « why did this device stop receiving? » stays answerable, and
    /// so a re-registration is an update of one row instead of a second row for the same physical device.</para>
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private DeviceRegistration() { } // For EF Core

    public static DeviceRegistration Create(
        Guid clinicId, string userId, DevicePlatform platform, string token, string? shellVersion, DateTime nowUtc)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("La clinique est obligatoire.", nameof(clinicId));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("L'utilisateur est obligatoire.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Le jeton de l'appareil est obligatoire.", nameof(token));
        }

        return new DeviceRegistration
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            UserId = userId.Trim(),
            Platform = platform,
            Token = token.Trim(),
            ShellVersion = Blank(shellVersion),
            LastSeenAt = nowUtc,
            IsActive = true,
            CreatedAt = nowUtc
        };
    }

    /// <summary>
    /// The caller re-presented a token this row already holds: refresh it in place (AC-40's re-registration and
    /// AC-41's « registering a token bound to the caller is a refresh » are the same write).
    ///
    /// <para>Reactivates, because a device whose app was uninstalled and reinstalled presents the same token and
    /// is genuinely reachable again — leaving it inactive would silently mean no notifications for ever.</para>
    /// </summary>
    public void Refresh(DevicePlatform platform, string? shellVersion, DateTime nowUtc)
    {
        Platform = platform;
        ShellVersion = Blank(shellVersion) ?? ShellVersion;
        LastSeenAt = nowUtc;
        IsActive = true;
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Moves this token to another user on another clinic's terms — the shared-device case (AC-41, EC-3).
    ///
    /// <para>One row, so the previous binding is <b>gone</b> rather than merely outranked: the whole defect this
    /// prevents is the earlier user still receiving pushes on a device they have handed over.</para>
    /// </summary>
    public void RebindTo(Guid clinicId, string userId, DevicePlatform platform, string? shellVersion, DateTime nowUtc)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("La clinique est obligatoire.", nameof(clinicId));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("L'utilisateur est obligatoire.", nameof(userId));
        }

        ClinicId = clinicId;
        UserId = userId.Trim();
        Refresh(platform, shellVersion, nowUtc);
    }

    /// <summary>
    /// Stops delivery to this device — sign-out (AC-40) or a token the platform reported unregistered (AC-49).
    /// Returns false when it was already inactive, so a caller can avoid a pointless write.
    /// </summary>
    public bool Deactivate(DateTime nowUtc)
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        UpdatedAt = nowUtc;
        return true;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
