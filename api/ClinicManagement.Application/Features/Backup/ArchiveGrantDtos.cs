namespace ClinicManagement.Application.Features.Backup;

/// <summary>One grant as the list shows it. The secret is absent by construction — it is not stored in plaintext.</summary>
public record ArchiveGrantDto(
    Guid Id,
    string Label,
    DateTime CreatedAtUtc,
    DateTime? LastUsedAtUtc,
    DateTime? RevokedAtUtc);

/// <summary>
/// What issuing a grant returns — the only moment <see cref="Secret"/> exists anywhere but the caller's screen
/// (AC-2). A lost secret is replaced by revoking and re-issuing, never by reading it back.
/// </summary>
public record IssuedArchiveGrantDto(
    Guid Id,
    string Label,
    string Secret,
    DateTime CreatedAtUtc);

/// <summary>What a device grant is exchanged for: an ordinary access token, on the issuing admin's identity.</summary>
public record ArchiveGrantTokenDto(string AccessToken, DateTime ExpiresAt);
