using System.Data;
using System.Globalization;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// AC-9.4 on the relay's row access: a cloud restored from a backup is behind its PC de secours. Each side hashes its rows
/// the same way (what the cloud keeps for itself left out), the PC reads the rows the cloud lacks, and the cloud knows
/// which of its rows it changed since the restore — those it keeps.
/// </summary>
public sealed partial class ClinicRelayRowStore
{
    /// <summary>
    /// Moves the restore mark of every cursor counted under another history (or <paramref name="clinicId"/>'s only) to
    /// its current position: a restore brings the old epoch back with the rows, so the first look after one finds it.
    /// </summary>
    public async Task MarkEpochAsync(Guid? clinicId, CancellationToken cancellationToken)
    {
        var epoch = await FeedEpochAsync(cancellationToken);
        await using var scope = _db.Database.CurrentTransaction is not null
            ? await AmbientAsync(cancellationToken)
            : await OpenAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await MarkEpochAsync(scope, clinicId, epoch, cancellationToken);
        if (_db.Database.CurrentTransaction is null)
        {
            await scope.CommitAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> RowHashesAsync(
        Guid clinicId, string table, CancellationToken cancellationToken)
    {
        var plan = Plan.Find(table) ?? throw new InvalidOperationException($"Table inconnue : {table}");
        await using var scope = await OpenAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var command = scope.Command(
                         $"SELECT {KeyExpression(plan, "a")}, md5((({RowJson(plan, "a")}){WithoutCloudKept(plan)})::text) "
                         + $"FROM {plan.QualifiedName} a WHERE {Scope(plan, "a", 0)}",
                         ("clinic", clinicId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                hashes[reader.GetString(0)] = reader.GetString(1);
            }
        }

        await scope.CommitAsync(cancellationToken);
        return hashes;
    }

    public async Task<IReadOnlyList<RelayRow>> ReadRowsAsync(
        Guid clinicId, IReadOnlyCollection<RelayRowKey> keys, CancellationToken cancellationToken)
    {
        await using var scope = await OpenAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var found = await ReadRowsAsync(scope, clinicId, keys, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return found
            .OrderBy(f => f.Key.Table, StringComparer.Ordinal).ThenBy(f => f.Key.Key, StringComparer.Ordinal)
            .Select(f => new RelayRow(f.Key.Table, f.Key.Key, JsonDocument.Parse(f.Value).RootElement.Clone()))
            .ToList();
    }

    public async Task<IReadOnlySet<RelayRowKey>> ChangedSinceRestoreAsync(
        Guid clinicId, IReadOnlyCollection<RelayRowKey> keys, CancellationToken cancellationToken)
    {
        var epoch = await FeedEpochAsync(cancellationToken);
        await using var scope = await AmbientAsync(cancellationToken);
        await MarkEpochAsync(scope, clinicId, epoch, cancellationToken);

        var changed = new HashSet<RelayRowKey>();
        if (keys.Count == 0)
        {
            return changed;
        }

        // Every change this cloud made after its restore point — never one an earlier return or gap wrote (Origin = Relay).
        await using var command = scope.Command(
            "SELECT DISTINCT c.\"Table\", c.\"EntityKey\" FROM \"ClinicChanges\" c "
            + "JOIN \"ClinicChangeCursors\" k ON k.\"ClinicId\" = c.\"ClinicId\" "
            + "WHERE c.\"ClinicId\" = @clinic AND c.\"Seq\" > k.\"EpochFromSeq\" AND c.\"Origin\" <> @relay",
            ("clinic", clinicId), ("relay", (int)ClinicChangeOrigin.Relay));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var wanted = keys.ToHashSet();
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = new RelayRowKey(reader.GetString(0), reader.GetString(1));
            if (wanted.Contains(key))
            {
                changed.Add(key);
            }
        }

        return changed;
    }

    private static async Task MarkEpochAsync(RelayScope scope, Guid? clinicId, string epoch, CancellationToken cancellationToken)
    {
        if (clinicId is { } clinic)
        {
            await scope.ExecuteAsync(
                "UPDATE \"ClinicChangeCursors\" SET \"Epoch\" = @epoch, \"EpochFromSeq\" = \"LastSeq\" "
                + "WHERE \"ClinicId\" = @clinic AND \"Epoch\" IS DISTINCT FROM @epoch",
                cancellationToken, ("epoch", epoch), ("clinic", clinic));
            return;
        }

        await scope.ExecuteAsync(
            "UPDATE \"ClinicChangeCursors\" SET \"Epoch\" = @epoch, \"EpochFromSeq\" = \"LastSeq\" "
            + "WHERE \"Epoch\" IS DISTINCT FROM @epoch",
            cancellationToken, ("epoch", epoch));
    }

    /// <summary>What neither side ever sends the other (AC-5.7), left out of a row's hash: the cloud's own settings.</summary>
    private static string WithoutCloudKept(ClinicRelayTable table) =>
        string.Concat(CloudKeptColumns(table).OrderBy(c => c, StringComparer.Ordinal).Select(c => $" - {Literal(c)}"));
}
