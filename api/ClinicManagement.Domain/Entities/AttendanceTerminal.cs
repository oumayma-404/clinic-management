using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A « borne »: a reception PC or tablet an admin turned into the cabinet's clock-in point. The device holds a
/// long-lived random secret in a cookie; only its SHA-256 is stored, which is what proves a punch came from the
/// cabinet. An admin may also assign it from her own computer to another signed-in device: the borne then waits,
/// with no secret, until that device's next status read claims it and receives its cookie.
/// </summary>
public class AttendanceTerminal : AggregateRoot<Guid>
{
    public const int MaxNameLength = 60;

    public Guid ClinicId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>SHA-256 hex of the device's secret — high-entropy, so a fast hash is the right one. Null while it waits for its device.</summary>
    public string? SecretHash { get; private set; }

    /// <summary>The signed-in device (its session) this borne waits for; cleared once that device claims it.</summary>
    public Guid? PendingSessionFamilyId { get; private set; }

    /// <summary>« Chrome sur Windows · Ines Gharbi » — what the admin chose, kept for the list.</summary>
    public string? DeviceDescription { get; private set; }

    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    /// <summary>When a device first held its cookie (registered on that device, or claimed); null while it waits.</summary>
    public DateTime? ActivatedAtUtc { get; private set; }

    public bool IsActive => RevokedAt is null;

    /// <summary>Assigned to another device that has not picked it up yet: nobody can punch on it.</summary>
    public bool IsPending => IsActive && SecretHash is null;

    /// <summary>Not retired and held by a device: a punch can land on it.</summary>
    public bool IsLive => IsActive && SecretHash is not null;

    private AttendanceTerminal() { } // For EF Core

    /// <summary>The French refusal for a borne name, or null when it is valid.</summary>
    public static string? NameRefusalFor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Donnez un nom à cette borne (ex. « Accueil »).";
        if (name.Trim().Length > MaxNameLength)
            return $"Le nom de la borne ne peut pas dépasser {MaxNameLength} caractères.";
        return null;
    }

    public static AttendanceTerminal Register(
        Guid id, Guid clinicId, string name, string secretHash, string byUserId, DateTime nowUtc)
    {
        if (NameRefusalFor(name) is { } refusal)
            throw new ArgumentException(refusal, nameof(name));
        if (string.IsNullOrWhiteSpace(secretHash))
            throw new ArgumentException("Secret hash is required.", nameof(secretHash));

        return new AttendanceTerminal
        {
            Id = id,
            ClinicId = clinicId,
            Name = name.Trim(),
            SecretHash = secretHash,
            CreatedByUserId = byUserId,
            CreatedAt = nowUtc,
            ActivatedAtUtc = nowUtc,
        };
    }

    /// <summary>A borne for another signed-in device, chosen from the admin's own computer. It has no secret until claimed.</summary>
    public static AttendanceTerminal AssignToDevice(
        Guid id, Guid clinicId, string name, Guid sessionFamilyId, string? deviceDescription, string byUserId, DateTime nowUtc)
    {
        if (NameRefusalFor(name) is { } refusal)
            throw new ArgumentException(refusal, nameof(name));
        if (sessionFamilyId == Guid.Empty)
            throw new ArgumentException("A device is required.", nameof(sessionFamilyId));

        return new AttendanceTerminal
        {
            Id = id,
            ClinicId = clinicId,
            Name = name.Trim(),
            PendingSessionFamilyId = sessionFamilyId,
            DeviceDescription = string.IsNullOrWhiteSpace(deviceDescription) ? null : deviceDescription.Trim(),
            CreatedByUserId = byUserId,
            CreatedAt = nowUtc,
        };
    }

    /// <summary>The chosen device picks its borne up: from now on its cookie is what proves a punch.</summary>
    public void Claim(string secretHash, DateTime nowUtc)
    {
        if (!IsPending)
            throw new InvalidOperationException("Cette borne n'attend plus d'appareil.");
        if (string.IsNullOrWhiteSpace(secretHash))
            throw new ArgumentException("Secret hash is required.", nameof(secretHash));
        SecretHash = secretHash;
        PendingSessionFamilyId = null;
        ActivatedAtUtc = nowUtc;
    }

    /// <summary>Takes effect at the device's next punch: the secret no longer matches an active borne.</summary>
    public void Revoke(DateTime nowUtc)
    {
        RevokedAt ??= nowUtc;
    }

    public void Touch(DateTime nowUtc)
    {
        LastUsedAt = nowUtc;
    }
}
