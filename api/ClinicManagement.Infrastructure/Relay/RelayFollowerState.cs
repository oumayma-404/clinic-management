using System.Text.Json;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// Where the PC de secours stands in the cloud's history (<c>clinic-pc-copy</c> D12). Kept in <c>.local/</c> beside the
/// credentials and <b>never in the database</b>: a re-seed replaces the copied tables wholesale, and a position stored
/// among them would be overwritten by the very copy it describes.
/// </summary>
public sealed record RelayFollowerState
{
    /// <summary>The cloud history this copy follows; null until the first copy (D12).</summary>
    public string? Epoch { get; init; }

    /// <summary>The highest change-log seq applied here.</summary>
    public long AppliedSeq { get; init; }

    /// <summary>The fingerprint of the change row at <see cref="AppliedSeq"/>, sent with every pull (continuity, D12).</summary>
    public string? HeadFingerprint { get; init; }

    /// <summary>When the rows of the first copy landed; null while it is under way.</summary>
    public DateTime? RowsSeededAtUtc { get; init; }

    /// <summary>A re-seed is owed — the cloud said the backlog is too long, or a batch would not apply.</summary>
    public bool ReseedNeeded { get; init; }

    /// <summary>After a failed whole copy, the next one waits until then.</summary>
    public DateTime? RetrySeedAfterUtc { get; init; }

    /// <summary>The copy stopped for good on a cloud that is not the history it followed; set only by D12's tests.</summary>
    public string? StoppedReason { get; init; }

    /// <summary>
    /// AC-9.4: the gap this PC is sending a cloud that went back in time — kept across attempts, so a lost answer is sent
    /// again under the same id and the cloud applies it once.
    /// </summary>
    public Guid? GapId { get; init; }

    /// <summary>The cloud runs another build: copy calls are refused until this PC updates (D10b).</summary>
    public bool UpdateNeeded { get; init; }

    /// <summary>The cloud no longer knows or wants this PC (retired, lost, unknown).</summary>
    public bool Released { get; init; }

    /// <summary>When this PC learned it was released; null on a file written before the field existed.</summary>
    public DateTime? ReleasedAtUtc { get; init; }

    /// <summary>When « Effacer la copie » emptied this PC (AC-8.2). Kept so the cloud is told even if it was unreachable then.</summary>
    public DateTime? ErasedAtUtc { get; init; }

    /// <summary>The cloud has recorded the erasure — the copy loop stops reporting it.</summary>
    public bool ErasureReported { get; init; }

    public DateTime? LastDigestAtUtc { get; init; }
    public IReadOnlyList<string> MismatchTables { get; init; } = Array.Empty<string>();
    public int FilesTotal { get; init; }
    public int FilesCopied { get; init; }

    /// <summary>The last failure, in French, reported on the next heartbeat; cleared by the next success.</summary>
    public string? LastError { get; init; }

    // ⚠️ The write lease (the last ack, the holding) is NOT here: it moves during a tick, and this file is rewritten
    // from the copy loaded at the start of one. See RelayLease (.local/relay-lease.json).

    // ---- the self-update (D10b) — one cloud build, one series of attempts --------------------------------------

    /// <summary>The cloud build this PC is updating to; null when no update is under way.</summary>
    public string? UpdateBuild { get; init; }

    /// <summary>When this PC first learned it must update to <see cref="UpdateBuild"/>.</summary>
    public DateTime? UpdateStartedAtUtc { get; init; }

    /// <summary>The cloud answered « pas encore » for its build: nothing to update to, so the PC does not claim to be updating.</summary>
    public bool UpdateWaitingForInstaller { get; init; }

    /// <summary>The installer's SHA-256 as the cloud sent it beside the bytes; nothing runs that does not match.</summary>
    public string? UpdateSha256 { get; init; }

    /// <summary>The next download attempt waits until then — a 404 or a failed download is never a loop.</summary>
    public DateTime? UpdateRetryAfterUtc { get; init; }

    /// <summary>The installer was started for <see cref="UpdateBuild"/>. Saved BEFORE the start, so one build is run once.</summary>
    public DateTime? UpdateLaunchedAtUtc { get; init; }

    /// <summary>Why the update did not land, in French; reported until the cloud's build changes.</summary>
    public string? UpdateError { get; init; }

    public bool RowsSeeded => RowsSeededAtUtc is not null;
}

/// <summary>Reads and writes <see cref="RelayFollowerState"/> atomically (temp file, then move).</summary>
public sealed class RelayFollowerStateStore
{
    public const string FileName = "relay-state.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;

    public RelayFollowerStateStore(string? localDir = null)
    {
        _path = Path.Combine(localDir ?? LocalInstallPaths.LocalDir, FileName);
    }

    public string FilePath => _path;

    /// <summary>The saved state, or a fresh one. ⚠️ An unreadable file is NOT a fresh start: it would re-seed with no
    /// epoch to compare, i.e. follow whatever cloud answers — so it reads as stopped and asks for a human.</summary>
    public RelayFollowerState Load()
    {
        if (!File.Exists(_path))
        {
            return new RelayFollowerState();
        }

        try
        {
            return JsonSerializer.Deserialize<RelayFollowerState>(File.ReadAllText(_path), Json) ?? new RelayFollowerState();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new RelayFollowerState
            {
                StoppedReason = "L'état de la copie est illisible sur ce PC : la copie est arrêtée par précaution.",
            };
        }
    }

    /// <summary>Writes only when the state changed — a tick saves after every step, and most steps move nothing.</summary>
    public void Save(RelayFollowerState state)
    {
        var json = JsonSerializer.Serialize(state, Json);
        if (string.Equals(json, _lastSaved, StringComparison.Ordinal) && File.Exists(_path))
        {
            return;
        }

        AtomicFile.Write(_path, json);
        _lastSaved = json;
    }

    private string? _lastSaved;

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
