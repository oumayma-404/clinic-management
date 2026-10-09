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
    long HoldingUnderAckSeq = 0,
    // AC-6.2: where a device of the cabinet tries the PC, and the box it must share with it to count.
    int? HttpsPort = null,
    string? GatewayAddress = null,
    // D18: the handback the cloud applied, which this PC no longer holds (phase 2), and since when its return fails.
    Guid? ReturnedHandbackId = null,
    DateTime? ReturnStuckSinceUtc = null,
    // AC-9.4: the database history this copy follows; another than the cloud's means the cloud was restored behind it.
    string? FollowedEpoch = null,
    // D20b: the PC set its clock from the cloud's (when, by how much), or cannot set it (then it is not ready).
    DateTime? ClockCorrectedAtUtc = null,
    int? ClockCorrectedBySeconds = null,
    bool ClockUnfixable = false);

/// <summary>D18: one change of the PC's own log of the cut — the key only; its row travels beside it.</summary>
public sealed record RelayHandbackChange(
    long Seq, string Table, string Key, bool Deleted, string? IdempotencyKey, DateTime RecordedAtUtc);

/// <summary>One row of the PC's « Journal d'activité » written during the cut (AC-5.5), re-chained on the cloud.</summary>
public sealed record RelayHandbackJournalEntry(
    string UserId, string? UserEmail, string EntityType, string EntityId, int Action, string? ChangedFields,
    DateTime OccurredAtUtc, bool IsDeclaredGap = false);

/// <summary>FR-11: an account's sign-in traces on the PC, merged field by field — never the account itself.</summary>
public sealed record RelaySignInTrace(string UserId, DateTime? LastLoginAt, int FailedLoginAttempts, DateTime? LockoutEnd);

/// <summary>FR-11: a recovery code spent on the PC stays spent on the cloud.</summary>
public sealed record RelayRecoveryCodeUse(Guid Id, DateTime UsedAtUtc);

/// <summary>
/// D18: the cut's work, handed back once. <c>BaseAppliedSeq</c> is the last cloud change the PC held when it took over:
/// what the cloud changed after it, the PC never saw. <c>HandbackId</c> makes a repeated request a no-op.
/// </summary>
public sealed record RelayHandbackRequest(
    Guid HandbackId,
    long BaseAppliedSeq,
    DateTime? CutSinceUtc,
    IReadOnlyList<RelayHandbackChange> Changes,
    IReadOnlyList<Common.Interfaces.RelayRow> Rows,
    IReadOnlyList<RelayHandbackJournalEntry> Journal,
    IReadOnlyList<RelaySignInTrace> SignIns,
    IReadOnlyList<RelayRecoveryCodeUse> RecoveryCodesUsed);

/// <summary>
/// AC-9.4: what a restored cloud lost, sent back by its PC de secours — the rows the PC holds that the cloud lacks or holds
/// otherwise. <c>GapId</c> makes a repeated request a no-op.
/// </summary>
public sealed record RelayGapRequest(Guid GapId, IReadOnlyList<Common.Interfaces.RelayRow> Rows);

/// <summary>AC-9.4: one row's hash, per-side columns left out — what the PC compares with its own before sending.</summary>
public sealed record RelayRowHashDto(string Key, string Hash);

/// <summary>The cloud's answer: how many rows it applied, dropped as a duplicate (D17) and listed for review.</summary>
public sealed record RelayHandbackResultDto(bool AlreadyApplied, int Applied, int Dropped, int Listed);

/// <summary>The files the cut's rows name; the cloud answers with those it does not have yet.</summary>
public sealed record RelayHandbackFilesRequest(IReadOnlyList<string>? Keys);

/// <summary>
/// D16: one number the cloud is about to make final, sent to its PC de secours, which keeps it before the cloud commits.
/// <c>Sequence</c> is <c>invoice</c>, <c>devis</c> or <c>credit-note</c>; <c>Number</c> is <c>AAAA-NNNN</c>.
/// </summary>
public sealed record RelayNumberPromiseDto(Guid Id, Guid ClinicId, string Sequence, string Number, string? IdempotencyKey);

/// <summary>The PC's long poll: the promises it has just kept (acknowledged), in exchange for the next ones.</summary>
public sealed record RelayPromisesRequest(IReadOnlyList<Guid>? Acks);

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
    bool Reclaimed = false,
    // D18 phase 2: the cloud holds the cabinet's saves again after the handback this PC named — it may forget it.
    bool ReturnReleased = false);

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
    string? LockSentence = null,
    // D18 / US-7: lines of « Modifications à vérifier » and of « À reprendre » nobody has marked yet.
    int ReviewPending = 0,
    int ReEnterPending = 0);

/// <summary>
/// AC-6.2: what a Windows or Android app of the cabinet tries while the cloud is locked and the PC said nothing —
/// never a browser. <c>Probe</c> is false unless the caller comes from the cabinet's internet line and all three
/// facts are known; the app then reaches the PC only through its pinned certificate (D21).
/// </summary>
public sealed record RelayDeviceTargetDto(
    bool Probe,
    IReadOnlyList<string> Addresses,
    int? Port,
    string? CertificateFingerprint,
    int IntervalSeconds);

/// <summary>A device's answer: it reached the cloud (this call), and whether it reached the PC; its own gateways.</summary>
public sealed record RelayDeviceReportRequest(bool ReachesPc, IReadOnlyList<string>? Gateways);

/// <summary><c>Counted</c>: the device was on the cabinet's network during a lock. <c>Unlocked</c>: this report ended it.</summary>
public sealed record RelayDeviceReportDto(bool Counted, bool Unlocked);
