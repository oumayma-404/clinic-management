using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A « borne »: a reception PC or tablet an admin turned into the cabinet's clock-in point. The device holds a
/// long-lived random secret in a cookie; only its SHA-256 is stored, which is what proves a punch came from the
/// cabinet.
/// </summary>
public class AttendanceTerminal : AggregateRoot<Guid>
{
    public const int MaxNameLength = 60;

    public Guid ClinicId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>SHA-256 hex of the device's secret — high-entropy, so a fast hash is the right one.</summary>
    public string SecretHash { get; private set; } = string.Empty;

    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

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
        };
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
