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

    /// <summary>The PC is applying an update (FR-2 « Mise à jour »).</summary>
    public bool IsUpdating { get; private set; }

    public string? LastError { get; private set; }

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

        if (!string.IsNullOrWhiteSpace(heartbeat.LanAddresses))
        {
            LanAddresses = NormalizeAddresses(heartbeat.LanAddresses);
        }

        CertificateFingerprint = NormalizeFingerprint(heartbeat.CertificateFingerprint) ?? CertificateFingerprint;

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
    string? CertificateFingerprint = null);
