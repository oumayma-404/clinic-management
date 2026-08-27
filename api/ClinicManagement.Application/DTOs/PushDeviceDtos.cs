using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.DTOs;

/// <summary>The registration the shell gets back, so it can tell a refresh from a rebind without guessing.</summary>
public class PushDeviceDto
{
    public Guid Id { get; set; }
    public DevicePlatform Platform { get; set; }
    public string? ShellVersion { get; set; }
    public DateTime LastSeenAt { get; set; }

    /// <summary>
    /// True when this call moved the token off another account (AC-41) — the shared-tablet case. Reported rather
    /// than silent because the shell may want to clear anything the previous session cached on that device, and
    /// because it is the one outcome an operator would otherwise have to read the audit ledger to see.
    /// </summary>
    public bool ReboundFromAnotherUser { get; set; }
}

/// <summary>
/// What one platform's OS notifications can do on this installation — per platform, because a half-configured
/// install must not read as a working one (AC-52).
/// </summary>
public class PushPlatformAvailabilityDto
{
    public DevicePlatform Platform { get; set; }

    /// <summary>Platform name as a French sentence uses it — « Android », « iOS ».</summary>
    public string Label { get; set; } = string.Empty;

    public bool Supported { get; set; }

    /// <summary>Why not, in French; null when it is supported. Server-side so three surfaces share one wording.</summary>
    public string? Reason { get; set; }

    /// <summary>This clinic's active registrations on that platform.</summary>
    public int RegisteredDevices { get; set; }
}

/// <summary>
/// The answer <c>GET /api/push-devices/availability</c> gives, and what the settings surface states (AC-51, AC-52).
///
/// <para><see cref="AvailableAtAll"/> is not derivable by a client from an empty <see cref="Platforms"/> list,
/// because the list is never empty — every platform is always present with its own verdict, so « iOS : non
/// configuré » is a statement rather than an absence. An absent row is not a statement at all.</para>
/// </summary>
public class PushAvailabilityDto
{
    public bool AvailableAtAll { get; set; }
    public List<PushPlatformAvailabilityDto> Platforms { get; set; } = new();
}
