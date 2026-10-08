namespace ClinicManagement.Application.Features.Relay;

/// <summary>
/// A pairing code, shown once, for the installer to present (D8). <paramref name="NeedBytes"/> is the free space the
/// PC must have (AC-1.8), handed to the installer as <c>/NEEDBYTES=</c> so it refuses with the cloud's own figure.
/// </summary>
public sealed record RelayPairingCodeDto(Guid RelayId, string Code, DateTime ExpiresAtUtc, long NeedBytes = 0);

/// <summary>What a paired PC receives once: its identity and secret.</summary>
public sealed record RelayPairingDto(Guid RelayId, Guid ClinicId, string ClinicName, string Secret);

public sealed record RelayTokenDto(string AccessToken, DateTime ExpiresAt);

/// <summary>The PC's heartbeat (FR-2). The heartbeat never refuses on version: a mismatch is a field of the ack (D10b).</summary>
public sealed record RelayHeartbeatRequest(
    long AppliedSeq,
    int SeedPercent,
    bool SeedComplete,
    int FilesTotal,
    int FilesCopied,
    long? DiskFreeBytes,
    bool IsUpdating,
    string? Build,
    DateTime? PcClockUtc,
    string? LanAddresses,
    IReadOnlyList<string>? MismatchTables,
    string? LastError,
    string? CertificateFingerprint,
    bool CopyStopped = false);

public sealed record RelayHeartbeatAck(
    DateTime CloudTimeUtc,
    long HighWater,
    string Epoch,
    bool Retired,
    bool UpdateNeeded,
    string CloudBuild);

/// <summary>« Paramètres → PC de secours » (AC-2.1).</summary>
/// <summary>The PC de secours's own view of itself (AC-8.1), read on the PC.</summary>
public sealed record RelayLocalStatusDto(bool Retired, DateTime? RetiredAtUtc, string Sentence);

public sealed record RelayStatusDto(
    bool Exists,
    Guid? RelayId,
    string? Label,
    string State,
    string Sentence,
    bool IsProblem,
    DateTime? SinceUtc,
    int? SeedPercent,
    IReadOnlyList<string> LanAddresses,
    string? CertificateFingerprint,
    DateTime? PairedAtUtc,
    DateTime? SeededAtUtc,
    DateTime? RetiredAtUtc,
    int FilesTotal,
    int FilesCopied,
    long? DiskFreeBytes,
    bool LostOrStolen = false,
    // The Windows app's offer (AC-1.1, AC-1.10): whether a PC de secours may be set up now, and how much room it needs.
    bool CanInstall = false,
    long NeedBytes = 0);
