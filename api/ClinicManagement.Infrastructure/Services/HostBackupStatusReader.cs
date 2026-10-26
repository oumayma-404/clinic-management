using System.Globalization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Reads the nightly run's <c>backup.status</c> (written by <c>deploy/backup/backup.sh</c> on every run) and
/// PostgreSQL's <c>pg_stat_archiver</c> (the WAL-G stream). See <see cref="IHostBackupStatusReader"/> for why and
/// for the never-throws contract.
///
/// <para>⚠️ <b>The status file reaches this container through a read-only volume</b>
/// (<c>backup_status</c>, mounted at <c>Backup:StatusFile</c>'s directory by <c>docker-compose.hosted.yml</c>). An
/// unset key or a missing file reads as « no run recorded », which the rules grade <c>Stale</c> — the honest answer
/// for a deployment where this was never wired.</para>
///
/// <para>⚠️ <b>The WAL half is read, never written, and needs no privilege</b>: <c>pg_stat_archiver</c> is visible to
/// every role. It is the database's own account of what it shipped, which is why it is read here rather than asking
/// the <c>pitr</c> sidecar — that sidecar runs the database's image, so teaching it to write a status file would
/// rebuild the image the live database runs and restart it on the next deploy.</para>
/// </summary>
public sealed class HostBackupStatusReader : IHostBackupStatusReader
{
    /// <summary>Where the nightly run's status file is read from — <c>docker-compose.hosted.yml</c> sets it.</summary>
    public const string StatusFileKey = "Backup:StatusFile";

    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HostBackupStatusReader> _logger;

    public HostBackupStatusReader(
        ApplicationDbContext context, IConfiguration configuration, ILogger<HostBackupStatusReader> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HostBackupSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var nightly = await ReadNightlyAsync(cancellationToken);
        var wal = await ReadWalArchiveAsync(cancellationToken);
        return new HostBackupSnapshot(nightly, wal);
    }

    private async Task<NightlyBackupRecord?> ReadNightlyAsync(CancellationToken cancellationToken)
    {
        var path = _configuration[StatusFileKey];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            return Parse(lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Backup status file {Path} could not be read", path);
            return null;
        }
    }

    /// <summary>
    /// The status file's <c>key=value</c> lines. Public so the parsing — the half most likely to drift from what
    /// <c>backup.sh</c> writes — is tested against that script's exact output.
    /// </summary>
    public static NightlyBackupRecord? Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        if (!values.TryGetValue("outcome", out var outcome) || string.IsNullOrEmpty(outcome))
        {
            return null;
        }

        return new NightlyBackupRecord(
            outcome,
            values.TryGetValue("stage", out var stage) && stage.Length > 0 ? stage : null,
            ParseInstant(values.GetValueOrDefault("finished")),
            ParseInstant(values.GetValueOrDefault("lastSuccess")));
    }

    private static DateTime? ParseInstant(string? value) =>
        DateTime.TryParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var instant)
            ? DateTime.SpecifyKind(instant, DateTimeKind.Utc)
            : null;

    private async Task<WalArchiveRecord?> ReadWalArchiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Column aliases are the property names: EF maps an unmapped type's columns by name.
            var row = await _context.Database
                .SqlQueryRaw<ArchiverRow>(
                    "SELECT archived_count AS \"ArchivedCount\", last_archived_time AS \"LastArchivedTime\", "
                    + "failed_count AS \"FailedCount\", last_failed_time AS \"LastFailedTime\" FROM pg_stat_archiver")
                .SingleOrDefaultAsync(cancellationToken);

            return row is null
                ? null
                : new WalArchiveRecord(
                    row.ArchivedCount, AsUtc(row.LastArchivedTime), row.FailedCount, AsUtc(row.LastFailedTime));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "pg_stat_archiver could not be read");
            return null;
        }
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value is { } instant ? DateTime.SpecifyKind(instant.ToUniversalTime(), DateTimeKind.Utc) : null;

    /// <summary>One <c>pg_stat_archiver</c> row, shaped for EF's unmapped-type read.</summary>
    public sealed class ArchiverRow
    {
        public long ArchivedCount { get; set; }
        public DateTime? LastArchivedTime { get; set; }
        public long FailedCount { get; set; }
        public DateTime? LastFailedTime { get; set; }
    }
}
