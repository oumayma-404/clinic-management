namespace ClinicManagement.Application.Features.Relay;

/// <summary>
/// A pairing code, shown once, for the installer to present (D8). <paramref name="NeedBytes"/> is the free space the
/// PC must have (AC-1.8), handed to the installer as <c>/NEEDBYTES=</c> so it refuses with the cloud's own figure.
/// </summary>
public sealed record RelayPairingCodeDto(Guid RelayId, string Code, DateTime ExpiresAtUtc, long NeedBytes = 0);

/// <summary>What a paired PC receives once: its identity and secret.</summary>
public sealed record RelayPairingDto(Guid RelayId, Guid ClinicId, string ClinicName, string Secret);

public sealed record RelayTokenDto(string AccessToken, DateTime ExpiresAt);

/// <summary>
/// The PC's heartbeat (FR-2). The heartbeat never refuses on version: a mismatch is a field of the ack (D10b).
/// <c>ConfirmedAck*</c> echo the last ack the PC received and <c>WantsToStandDown</c> asks for a disarm (D13, D14).
/// </summary>
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
    bool CopyStopped = false,
    long ConfirmedAckSeq = 0,
    bool ConfirmedAckArmed = false,
    bool WantsToStandDown = false,
    bool Holding = false,
    DateTime? HoldingSinceUtc = null,
    long HoldingUnderAckSeq = 0);

/// <summary>The cloud's answer. <c>AckSeq</c> is the ack's id (its send instant); <c>Armed</c> lets the PC take over after a cut (D13).</summary>
public sealed record RelayHeartbeatAck(
    DateTime CloudTimeUtc,
    long HighWater,
    string Epoch,
    bool Retired,
    bool UpdateNeeded,
    string CloudBuild,
    long AckSeq = 0,
    bool Armed = false,
    // D19: an admin took the cloud back after this PC's takeover — it stops and keeps the cut's work.
    bool Reclaimed = false);

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
    long NeedBytes = 0,
    // The lease (D13, US-7): the cabinet's saves are refused on the cloud now, since when, whether the PC said it holds
    // them, and AC-7.1's warning for « Reprendre la main » (also shown before a retire or a loss while locked, AC-8.6).
    bool CloudLocked = false,
    DateTime? LockedSinceUtc = null,
    bool PcHolding = false,
    string? ReclaimWarning = null,
    string? LockSentence = null);
