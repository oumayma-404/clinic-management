using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>One « PC de secours » setup attempt (<c>clinic-pc-copy</c>); heartbeats write it every few seconds, so it is
/// unaudited and unversioned — its lifecycle steps write their own journal rows (FR-8).</summary>
public class ClinicRelay : AggregateRoot<Guid>
{
    private const int SecretBytes = 32;

    /// <summary>A relay token's subject is <c>relay|{id}</c>: no <see cref="User"/> row carries it, so no account middleware mistakes it for a person.</summary>
    public const string SubjectPrefix = "relay|";

    public string Subject => SubjectPrefix + Id.ToString("D");

    /// <summary>How long a pairing code may wait for the installer (D8).</summary>
    public static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>A setup that never finishes its first copy is released after this (EC-9).</summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(24);

    // Column widths, declared here so the PC's free-text reports are capped where they are written: a heartbeat
    // carrying a long error must update the row, not fail the save (a 76-character build string did, on the first
    // end-to-end run). The EF configuration reads the same constants.
    public const int MaxBuildLength = 200;
    public const int MaxErrorLength = 1000;
    public const int MaxMismatchLength = 2000;

    public Guid ClinicId { get; private set; }

    /// <summary>The PC's Windows name, shown everywhere the PC is named (« PC-ACCUEIL »).</summary>
    public string Label { get; private set; } = string.Empty;

    public ClinicRelayStatus Status { get; private set; }

    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>SHA-256 of the one-time pairing code; cleared once used.</summary>
    public string? PairingCodeHash { get; private set; }
    public DateTime? PairingCodeExpiresAtUtc { get; private set; }

    /// <summary>SHA-256 of the long-lived relay secret the PC exchanges for short tokens.</summary>
    public string? SecretHash { get; private set; }

    /// <summary>The PC's RSA public key (SPKI, base64) — the cloud wraps TOTP secrets for this PC only (D8).</summary>
    public string? PublicKey { get; private set; }

    /// <summary>SHA-256 of the PC's server certificate, hex — what devices pin (D21).</summary>
    public string? CertificateFingerprint { get; private set; }

    /// <summary>The PC's LAN addresses as last reported, comma-separated.</summary>
    public string? LanAddresses { get; private set; }

    public string? Build { get; private set; }
    public DateTime? PairedAtUtc { get; private set; }

    /// <summary>When the first copy completed (FR-8 « première copie terminée »).</summary>
    public DateTime? SeededAtUtc { get; private set; }

    /// <summary>First-copy progress 0–100 (AC-1.9).</summary>
    public int SeedPercent { get; private set; }

    /// <summary>The highest change-log seq the PC has applied, as it last reported.</summary>
    public long AppliedSeq { get; private set; }

    /// <summary>The cloud's high-water when it last answered; « ready » is <c>AppliedSeq == this</c> at that answer (FR-2).</summary>
    public long HighWaterAtLastAck { get; private set; }

    /// <summary>When the cloud last heard from the PC.</summary>
    public DateTime? LastSeenAtUtc { get; private set; }

    /// <summary>When the PC was last ready; drives « Copie à jour il y a … » and « En retard » (FR-2).</summary>
    public DateTime? LastReadyAtUtc { get; private set; }

    public int FilesTotal { get; private set; }
    public int FilesCopied { get; private set; }
    public long? DiskFreeBytes { get; private set; }

    /// <summary>The PC's clock minus the cloud's, as measured at its last contact (EC-10).</summary>
    public int? ClockSkewSeconds { get; private set; }

    /// <summary>Set while the hourly check finds a difference the PC could not repair (FR-9).</summary>
    public DateTime? MismatchSinceUtc { get; private set; }
    public string? MismatchTables { get; private set; }

    /// <summary>
    /// Set while the PC has stopped following because the cloud went back in time (D12, AC-9.4). Its copy is then
    /// newer than the cloud, so « caught up » would read as « Prêt » — this is what keeps it from saying so.
    /// </summary>
    public DateTime? CopyStoppedSinceUtc { get; private set; }

    /// <summary>The PC is applying an update (FR-2 « Mise à jour »).</summary>
    public bool IsUpdating { get; private set; }

    public string? LastError { get; private set; }

    // ---- the write lease (D13, D14) — see ClinicWriteLease --------------------------------------------------------

    /// <summary>The last ack sent. Its id IS its send instant in ticks (strictly increasing), so no table remembers send times.</summary>
    public long LastAckSeq { get; private set; }

    /// <summary>
    /// The FIRST « armé » ack sent after the confirmed one, or not greater than <see cref="ConfirmedAckSeq"/> when none was —
    /// the earliest armed ack the PC may hold that it has not confirmed, which is where the silence clock must start.
    /// </summary>
    public long PendingArmedAckSeq { get; private set; }

    /// <summary>The newest ack the PC has said it received — the cloud's silence clock runs from when THAT ack was sent.</summary>
    public long ConfirmedAckSeq { get; private set; }

    /// <summary>What that confirmed ack told the PC (D14: a disarm counts once the PC has confirmed it).</summary>
    public bool ConfirmedAckArmed { get; private set; }

    /// <summary>
    /// Whether the PC may be holding an « armé » ack: the one it confirmed said so, or one sent since it may have received.
    /// </summary>
    public bool MayBeArmed => ConfirmedAckArmed || PendingArmedAckSeq > ConfirmedAckSeq;

    /// <summary>
    /// Since when the PC has said it holds the cabinet's saves (it took over during a cut). Set by the first heartbeat
    /// that says so and cleared only when the PC hands the cut's work back — never by a later heartbeat, which may be an
    /// older one arriving late. While set, the cloud refuses the cabinet's saves whatever the acks say.
    /// </summary>
    public DateTime? PcHoldingSinceUtc { get; private set; }

    // ---- « Reprendre la main » (D19, US-7) -------------------------------------------------------------------------

    /// <summary>
    /// The last ack sent when an admin took the cloud back: no ack up to it arms the PC any more, and a takeover the PC
    /// made under one of them is overruled when it reconnects (<see cref="IsOverruledHolding"/>). 0 = never reclaimed.
    /// </summary>
    public long ReclaimedAtAckSeq { get; private set; }

    public DateTime? ReclaimedAtUtc { get; private set; }
    public string? ReclaimedByUserId { get; private set; }

    /// <summary>
    /// When the PC reconnected holding a takeover the reclaim overruled: it stopped, and keeps that cut's work for
    /// « À reprendre » (AC-7.3). What makes its « Copie arrêtée » name the right cause.
    /// </summary>
    public DateTime? CutOverruledAtUtc { get; private set; }

    // ---- device reports (AC-6.2) -------------------------------------------------------------------------------------

    /// <summary>Who took the cloud back when it was the cabinet's own devices, not an admin (<see cref="ReclaimedByUserId"/>).</summary>
    public const string ReclaimedByDevices = "relay-devices";

    /// <summary>Two « cloud yes, PC no » reports this far apart, and none that reached the PC, unlock a silent PC's cabinet.</summary>
    public static readonly TimeSpan DeviceReportsSpan = TimeSpan.FromSeconds(30);

    /// <summary>The PC's HTTPS port, so a device can try it directly (its addresses are <see cref="LanAddresses"/>).</summary>
    public int? HttpsPort { get; private set; }

    /// <summary>The PC's default gateway — the cabinet's box — as the PC last saw it.</summary>
    public string? GatewayAddress { get; private set; }

    /// <summary>The internet address the PC's last heartbeat came from: the cabinet's, seen by the cloud.</summary>
    public string? PublicAddress { get; private set; }

    /// <summary>The lock the device facts below belong to (its start); a new lock starts them afresh.</summary>
    public DateTime? DeviceReportsLockSinceUtc { get; private set; }

    public DateTime? DevicesUnreachableFirstAtUtc { get; private set; }
    public DateTime? DevicesUnreachableLastAtUtc { get; private set; }

    /// <summary>A device of the cabinet reached the PC during this lock — then the PC is alive, and nothing is unlocked.</summary>
    public DateTime? DevicesReachedPcAtUtc { get; private set; }

    public DateTime? RetiredAtUtc { get; private set; }
    public ClinicRelayRetirement? RetiredReason { get; private set; }
    public string? RetiredByUserId { get; private set; }

    private ClinicRelay() { }

    /// <summary>Starts a setup attempt and returns the one-time code the installer will present (shown once).</summary>
    public static (ClinicRelay Relay, string PairingCode) BeginPairing(
        Guid clinicId, string label, string createdByUserId, DateTime nowUtc)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("Un PC de secours appartient à un cabinet.", nameof(clinicId));
        }

        var code = NewToken();
        var relay = new ClinicRelay
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            Label = NormalizeLabel(label),
            Status = ClinicRelayStatus.Pairing,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = nowUtc,
            PairingCodeHash = Hash(code),
            PairingCodeExpiresAtUtc = nowUtc + PairingCodeLifetime,
        };
        return (relay, code);
    }

    /// <summary>Whether this row still holds the clinic's one place (AC-1.10): retired, expired-unused and abandoned rows do not.</summary>
    public bool OccupiesTheClinic(DateTime nowUtc) => Status switch
    {
        ClinicRelayStatus.Retired => false,
        ClinicRelayStatus.Pairing => PairingCodeExpiresAtUtc is { } expires && expires > nowUtc,
        ClinicRelayStatus.Seeding => !IsAbandoned(nowUtc),
        _ => true,
    };

    /// <summary>EC-9: paired but no first copy within <see cref="AbandonedAfter"/>.</summary>
    public bool IsAbandoned(DateTime nowUtc) =>
        Status == ClinicRelayStatus.Seeding
        && SeededAtUtc is null
        && (LastSeenAtUtc ?? PairedAtUtc ?? CreatedAtUtc) + AbandonedAfter <= nowUtc;

    public bool CodeMatches(string code, DateTime nowUtc) =>
        Status == ClinicRelayStatus.Pairing
        && PairingCodeHash is not null
        && PairingCodeExpiresAtUtc > nowUtc
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(PairingCodeHash), Encoding.ASCII.GetBytes(Hash(code)));

    /// <summary>The installer presents the code; the PC becomes this relay and receives its secret (shown once).</summary>
    public string Pair(
        string label, string publicKey, string? certificateFingerprint, string? lanAddresses, string? build,
        DateTime nowUtc)
    {
        if (Status != ClinicRelayStatus.Pairing)
        {
            throw new InvalidOperationException("Ce code d'installation a déjà été utilisé.");
        }

        if (string.IsNullOrWhiteSpace(publicKey))
        {
            throw new ArgumentException("La clé publique du PC de secours est requise.", nameof(publicKey));
        }

        var secret = NewToken();
        Label = string.IsNullOrWhiteSpace(label) ? Label : NormalizeLabel(label);
        PublicKey = publicKey.Trim();
        CertificateFingerprint = NormalizeFingerprint(certificateFingerprint);
        LanAddresses = NormalizeAddresses(lanAddresses);
        Build = Cap(build, MaxBuildLength);
        SecretHash = Hash(secret);
        PairingCodeHash = null;
        PairingCodeExpiresAtUtc = null;
        PairedAtUtc = nowUtc;
        LastSeenAtUtc = nowUtc;
        Status = ClinicRelayStatus.Seeding;
        return secret;
    }

    public bool SecretMatches(string secret) =>
        SecretHash is not null
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(SecretHash), Encoding.ASCII.GetBytes(Hash(secret)));

    /// <summary>
    /// The PC's echo of the last ack it received (D14). An ack older than the one already confirmed, or one never sent,
    /// changes nothing — the clock may only move forward, and only to an ack the cloud really sent.
    /// </summary>
    public void RecordAckConfirmation(long ackSeq, bool armed)
    {
        if (Status == ClinicRelayStatus.Retired || ackSeq <= ConfirmedAckSeq || ackSeq > LastAckSeq)
        {
            return;
        }

        ConfirmedAckSeq = ackSeq;
        ConfirmedAckArmed = armed;
    }

    /// <summary>The next ack (D13): its id is its send instant, made strictly increasing so a clock step cannot reuse one.</summary>
    public long IssueAck(bool armed, DateTime nowUtc)
    {
        LastAckSeq = Math.Max(LastAckSeq + 1, nowUtc.Ticks);
        if (armed && PendingArmedAckSeq <= ConfirmedAckSeq)
        {
            PendingArmedAckSeq = LastAckSeq;
        }

        return LastAckSeq;
    }

    /// <summary>
    /// « Reprendre la main » (D19, US-7): the cloud takes the cabinet's saves back. Every ack sent so far stops arming the
    /// PC, and the PC's word that it holds the saves is set aside — a takeover made under one of those acks is overruled
    /// when that PC reconnects. Allowed while the cloud is fenced (the caller checks).
    /// </summary>
    public void Reclaim(string? byUserId, DateTime nowUtc)
    {
        ReclaimedAtAckSeq = LastAckSeq;
        ReclaimedAtUtc = nowUtc;
        ReclaimedByUserId = byUserId;
        PcHoldingSinceUtc = null;
    }

    /// <summary>
    /// AC-6.2: a device on the cabinet's network (the caller decided that) says whether it reaches the PC during the
    /// lock that began at <paramref name="lockedSinceUtc"/>. True when this report is the one that unlocks: two
    /// « cloud yes, PC no » reports at least <see cref="DeviceReportsSpan"/> apart, and no device that reached the PC.
    /// </summary>
    public bool RecordDeviceReport(bool reachesPc, DateTime lockedSinceUtc, DateTime nowUtc)
    {
        if (DeviceReportsLockSinceUtc != lockedSinceUtc)
        {
            DeviceReportsLockSinceUtc = lockedSinceUtc;
            DevicesUnreachableFirstAtUtc = null;
            DevicesUnreachableLastAtUtc = null;
            DevicesReachedPcAtUtc = null;
        }

        if (reachesPc)
        {
            DevicesReachedPcAtUtc = nowUtc;
            return false;
        }

        DevicesUnreachableFirstAtUtc ??= nowUtc;
        DevicesUnreachableLastAtUtc = nowUtc;
        return DevicesReachedPcAtUtc is null && nowUtc - DevicesUnreachableFirstAtUtc.Value >= DeviceReportsSpan;
    }

    /// <summary>A heartbeat saying « je tiens les enregistrements » under an ack an admin's reclaim overruled.</summary>
    public bool IsOverruledHolding(RelayHeartbeat heartbeat) =>
        heartbeat.Holding && ReclaimedAtUtc is not null && heartbeat.HoldingUnderAckSeq <= ReclaimedAtAckSeq;

    /// <summary>What the PC reports on each heartbeat; true when this one completed the first copy (FR-8).</summary>
    public bool RecordHeartbeat(RelayHeartbeat heartbeat, long highWater, DateTime nowUtc)
    {
        if (Status == ClinicRelayStatus.Retired)
        {
            return false;
        }

        LastSeenAtUtc = nowUtc;
        AppliedSeq = Math.Max(0, heartbeat.AppliedSeq);
        HighWaterAtLastAck = highWater;
        SeedPercent = Math.Clamp(heartbeat.SeedPercent, 0, 100);
        FilesTotal = Math.Max(0, heartbeat.FilesTotal);
        FilesCopied = Math.Clamp(heartbeat.FilesCopied, 0, FilesTotal);
        DiskFreeBytes = heartbeat.DiskFreeBytes;
        IsUpdating = heartbeat.IsUpdating;
        LastError = Cap(heartbeat.LastError, MaxErrorLength);
        Build = Cap(heartbeat.Build, MaxBuildLength) ?? Build;
        ClockSkewSeconds = heartbeat.PcClockUtc is { } pc ? (int)Math.Round((pc - nowUtc).TotalSeconds) : null;
        CopyStoppedSinceUtc = heartbeat.CopyStopped ? CopyStoppedSinceUtc ?? nowUtc : null;
        if (IsOverruledHolding(heartbeat))
        {
            // The admin took the cloud back after this takeover: the PC stops on this answer and keeps the cut's work.
            CutOverruledAtUtc ??= nowUtc;
        }
        else if (heartbeat.Holding && PcHoldingSinceUtc is null)
        {
            // The PC's clock may run ahead of the cloud's; a takeover is never in the cloud's future.
            PcHoldingSinceUtc = heartbeat.HoldingSinceUtc is { } since && since < nowUtc ? since : nowUtc;
        }

        if (!string.IsNullOrWhiteSpace(heartbeat.LanAddresses))
        {
            LanAddresses = NormalizeAddresses(heartbeat.LanAddresses);
        }

        CertificateFingerprint = NormalizeFingerprint(heartbeat.CertificateFingerprint) ?? CertificateFingerprint;
        HttpsPort = heartbeat.HttpsPort is > 0 and <= 65535 ? heartbeat.HttpsPort : HttpsPort;
        GatewayAddress = NormalizeAddress(heartbeat.GatewayAddress) ?? GatewayAddress;
        PublicAddress = NormalizeAddress(heartbeat.PublicAddress) ?? PublicAddress;

        var seededNow = false;
        if (heartbeat.SeedComplete && SeededAtUtc is null)
        {
            SeededAtUtc = nowUtc;
            Status = ClinicRelayStatus.Active;
            seededNow = true;
        }

        if (heartbeat.MismatchTables is { Count: > 0 } tables)
        {
            MismatchSinceUtc ??= nowUtc;
            MismatchTables = Cap(string.Join(",", tables.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)), MaxMismatchLength);
        }
        else
        {
            MismatchSinceUtc = null;
            MismatchTables = null;
        }

        if (IsCaughtUp)
        {
            LastReadyAtUtc = nowUtc;
        }

        return seededNow;
    }

    /// <summary>The PC held everything up to the cloud's high-water when the cloud last answered (FR-2 « Prêt »).</summary>
    public bool IsCaughtUp => Status == ClinicRelayStatus.Active && AppliedSeq >= HighWaterAtLastAck;

    /// <summary>
    /// A setup that ended before the installer presented the code — Windows' prompt refused, too little room, a failed
    /// download (AC-1.11). Frees the clinic's one place at once instead of after the code's 10 minutes. False, and
    /// nothing changes, once the code has been used: a paired PC is retired by an admin, never by whoever holds a code.
    /// </summary>
    public bool ReleaseUnusedCode(DateTime nowUtc)
    {
        if (Status != ClinicRelayStatus.Pairing)
        {
            return false;
        }

        Retire(ClinicRelayRetirement.Abandoned, byUserId: null, nowUtc);
        return true;
    }

    /// <summary>Idempotent: the first retirement is the one that counts.</summary>
    public void Retire(ClinicRelayRetirement reason, string? byUserId, DateTime nowUtc)
    {
        if (Status == ClinicRelayStatus.Retired)
        {
            return;
        }

        Status = ClinicRelayStatus.Retired;
        RetiredAtUtc = nowUtc;
        RetiredReason = reason;
        RetiredByUserId = byUserId;
        PairingCodeHash = null;
        PairingCodeExpiresAtUtc = null;
    }

    /// <summary>When the PC reported « Effacer la copie » done (AC-8.2); null while the copy may still be on it.</summary>
    public DateTime? ErasedAtUtc { get; private set; }

    /// <summary>
    /// The retired PC says its copy is gone (AC-8.2). Only a retired PC can have erased — an active one still holds the
    /// copy by definition. Returns false when already recorded, so a repeated report writes no second journal row.
    /// </summary>
    public bool MarkErased(DateTime nowUtc)
    {
        if (Status != ClinicRelayStatus.Retired)
        {
            throw new InvalidOperationException("Seul un PC de secours retiré peut effacer sa copie.");
        }

        if (ErasedAtUtc is not null)
        {
            return false;
        }

        ErasedAtUtc = nowUtc;
        return true;
    }

    /// <summary>When the PC said it was being uninstalled (AC-8.3); null while it is still installed.</summary>
    public DateTime? UninstalledAtUtc { get; private set; }

    /// <summary>
    /// The PC is being uninstalled (AC-8.3), which counts as retiring it. A PC retired earlier keeps its reason and date —
    /// « perdu ou volé » above all. Returns false when already recorded, so a repeated report writes no second journal row.
    /// </summary>
    public bool MarkUninstalled(DateTime nowUtc)
    {
        Retire(ClinicRelayRetirement.Uninstalled, byUserId: null, nowUtc);
        if (UninstalledAtUtc is not null)
        {
            return false;
        }

        UninstalledAtUtc = nowUtc;
        return true;
    }

    /// <summary>
    /// « Déclarer perdu ou volé » (AC-8.4): retires the PC if it is not already, and records the reason even on a PC
    /// retired earlier — a PC put in a cupboard and then stolen is exactly the case.
    /// </summary>
    public void DeclareLost(string? byUserId, DateTime nowUtc)
    {
        Retire(ClinicRelayRetirement.LostOrStolen, byUserId, nowUtc);
        RetiredReason = ClinicRelayRetirement.LostOrStolen;
    }

    public IReadOnlyList<string> LanAddressList =>
        string.IsNullOrWhiteSpace(LanAddresses)
            ? Array.Empty<string>()
            : LanAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Cap(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    private static string NormalizeLabel(string label)
    {
        var trimmed = (label ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "PC de secours";
        }

        return trimmed.Length > 120 ? trimmed[..120] : trimmed;
    }

    private static string? NormalizeFingerprint(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        var hex = new string(fingerprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return hex.Length == 64 ? hex : null;
    }

    private static string? NormalizeAddresses(string? addresses)
    {
        if (string.IsNullOrWhiteSpace(addresses))
        {
            return null;
        }

        var list = addresses.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())
            .Where(a => a.Length is > 0 and <= 64)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        return list.Count == 0 ? null : string.Join(",", list);
    }

    /// <summary>
    /// AC-6.2, EC-23: the request came from the cabinet's internet line, as the PC's own heartbeats did. IPv4 must be the
    /// same address; IPv6 the same /64 (each device has its own address there). A family mismatch never matches.
    /// </summary>
    public bool IsFromCabinetInternet(string? callerAddress)
    {
        if (PublicAddress is null
            || !System.Net.IPAddress.TryParse(PublicAddress, out var cabinet)
            || NormalizeAddress(callerAddress) is not { } normalized
            || !System.Net.IPAddress.TryParse(normalized, out var caller)
            || caller.AddressFamily != cabinet.AddressFamily)
        {
            return false;
        }

        if (caller.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return caller.Equals(cabinet);
        }

        return caller.GetAddressBytes().AsSpan(0, 8).SequenceEqual(cabinet.GetAddressBytes().AsSpan(0, 8));
    }

    /// <summary>
    /// AC-6.2: a device is on the cabinet's network when it comes from the cabinet's internet line AND its own gateway is
    /// the PC's — a phone on mobile data fails the first, a device at home behind the same kind of box the second.
    /// </summary>
    public bool IsOnCabinetNetwork(string? callerAddress, IEnumerable<string>? deviceGateways) =>
        GatewayAddress is not null
        && IsFromCabinetInternet(callerAddress)
        && deviceGateways is not null
        && deviceGateways.Take(8).Any(g => NormalizeAddress(g) == GatewayAddress);

    /// <summary>One IP address in its canonical form — an IPv4 seen through IPv6 compares as the IPv4 it is.</summary>
    public static string? NormalizeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !System.Net.IPAddress.TryParse(address.Trim(), out var ip))
        {
            return null;
        }

        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }
}

/// <summary>One heartbeat's report from the PC de secours.</summary>
public sealed record RelayHeartbeat(
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
    string? CertificateFingerprint = null,
    bool CopyStopped = false,
    bool Holding = false,
    DateTime? HoldingSinceUtc = null,
    long HoldingUnderAckSeq = 0,
    // AC-6.2: how a device on the cabinet's network reaches the PC, and how it knows it is on that network.
    int? HttpsPort = null,
    string? GatewayAddress = null,
    string? PublicAddress = null);
