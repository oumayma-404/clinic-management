using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// The relay's row access, in SQL rather than through entities: <c>row_to_json</c> out and <c>json_populate_recordset</c>
/// back in are exact inverses, so no value's type, converter or owned column passes through C# on the way (D3, D6).
/// Every table and column name comes from the EF model or the catalog, never from a request.
/// </summary>
public sealed partial class ClinicRelayRowStore : IClinicRelayRowStore, IRelayBlobIndex
{
    /// <summary>Past this many changed keys a batch is a re-seed (D6b).</summary>
    public const int MaxBatchKeys = 20_000;

    private const int ChunkRows = 500;
    private const string OwnedProperty = "$owned";

    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> ColumnCache = new(StringComparer.Ordinal);

    private readonly ApplicationDbContext _db;
    private readonly IUserSecretProtector _secrets;

    public ClinicRelayRowStore(ApplicationDbContext db, IUserSecretProtector secrets)
    {
        _db = db;
        _secrets = secrets;
    }

    private ClinicRelayPlan Plan => ClinicRelayScope.For(_db.Model);

    public Task EnsureCursorAsync(Guid clinicId, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "INSERT INTO \"ClinicChangeCursors\" (\"ClinicId\", \"LastSeq\") VALUES (@clinic, 0) ON CONFLICT DO NOTHING",
            cancellationToken, ("clinic", clinicId));

    public async Task DropCursorAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        await ExecuteAsync("DELETE FROM \"ClinicChangeCursors\" WHERE \"ClinicId\" = @clinic", cancellationToken, ("clinic", clinicId));
        await ExecuteAsync("DELETE FROM \"ClinicChanges\" WHERE \"ClinicId\" = @clinic", cancellationToken, ("clinic", clinicId));
    }

    /// <summary>D27: one batch per transaction, so a long-unpruned log never holds a lock over the cabinet's saves.</summary>
    private const int PruneBatch = 5000;

    public async Task<int> PruneChangesAsync(
        Guid clinicId, long belowSeq, DateTime recordedBeforeUtc, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            await using var scope = await OpenAsync(IsolationLevel.ReadCommitted, cancellationToken);
            int deleted;
            await using (var command = scope.Command(
                             "DELETE FROM \"ClinicChanges\" WHERE \"ClinicId\" = @clinic AND \"Seq\" IN ("
                             + "SELECT \"Seq\" FROM \"ClinicChanges\" WHERE \"ClinicId\" = @clinic AND \"Seq\" < @below "
                             + "AND \"RecordedAtUtc\" < @before ORDER BY \"Seq\" LIMIT @limit)",
                             ("clinic", clinicId), ("below", belowSeq),
                             ("before", DateTime.SpecifyKind(recordedBeforeUtc, DateTimeKind.Utc)), ("limit", PruneBatch)))
            {
                deleted = await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await scope.CommitAsync(cancellationToken);
            total += deleted;
            if (deleted < PruneBatch)
            {
                return total;
            }
        }
    }

    public async Task<long> HighWaterAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        var value = await ScalarAsync(
            null, "SELECT \"LastSeq\" FROM \"ClinicChangeCursors\" WHERE \"ClinicId\" = @clinic", cancellationToken,
            ("clinic", clinicId));
        return value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public async Task<string> FeedEpochAsync(CancellationToken cancellationToken)
    {
        var oid = await TryScalarAsync("SELECT oid::text FROM pg_database WHERE datname = current_database()", cancellationToken);
        var system = await TryScalarAsync("SELECT system_identifier::text FROM pg_control_system()", cancellationToken);
        var timeline = await TryScalarAsync("SELECT timeline_id::text FROM pg_control_checkpoint()", cancellationToken);
        return $"{system ?? "?"}-{timeline ?? "?"}-{oid ?? "?"}";
    }

    public async Task<string?> FingerprintAsync(Guid clinicId, long seq, CancellationToken cancellationToken)
    {
        if (seq <= 0)
        {
            return null;
        }

        await using var scope = await OpenAsync(IsolationLevel.ReadCommitted, cancellationToken);
        return await FingerprintAsync(scope, clinicId, seq, cancellationToken);
    }

    public async Task<RelayFeedBatch> ReadChangesAsync(
        Guid clinicId, long after, string? fingerprint, RelayOutboundWrap wrap, CancellationToken cancellationToken)
    {
        var epoch = await FeedEpochAsync(cancellationToken);
        await using var scope = await OpenAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var highWater = await HighWaterAsync(scope, clinicId, cancellationToken);
        if (after > highWater)
        {
            return new RelayFeedBatch(epoch, after, highWater, null, RelayFeedOutcome.WentBack, Array.Empty<RelayRow>());
        }

        if (after > 0 && !string.IsNullOrEmpty(fingerprint)
            && await FingerprintAsync(scope, clinicId, after, cancellationToken) != fingerprint)
        {
            return new RelayFeedBatch(epoch, after, highWater, null, RelayFeedOutcome.WentBack, Array.Empty<RelayRow>());
        }

        var keys = new List<(string Table, string Key)>();
        await using (var command = scope.Command(
                         "SELECT \"Table\", \"EntityKey\" FROM \"ClinicChanges\" WHERE \"ClinicId\" = @clinic "
                         + "AND \"Seq\" > @after AND \"Seq\" <= @high GROUP BY 1, 2 LIMIT @limit",
                         ("clinic", clinicId), ("after", after), ("high", highWater), ("limit", MaxBatchKeys + 1)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var head = await FingerprintAsync(scope, clinicId, highWater, cancellationToken);
        if (keys.Count > MaxBatchKeys)
        {
            return new RelayFeedBatch(epoch, after, highWater, head, RelayFeedOutcome.ReseedRequired, Array.Empty<RelayRow>());
        }

        var rows = new List<RelayRow>(keys.Count);
        foreach (var group in keys.GroupBy(k => k.Table, StringComparer.Ordinal))
        {
            var table = Plan.Find(group.Key);
            if (table is null)
            {
                continue;
            }

            var wanted = group.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var chunk in wanted.Chunk(ChunkRows))
            {
                var sql = $"SELECT {KeyExpression(table, "a")}, ({RowJson(table, "a")})::text FROM {table.QualifiedName} a "
                          + $"WHERE {KeyMatch(table, "a")} AND {Scope(table, "a", 0)}";
                await using var command = scope.Command(sql, ("clinic", clinicId), ("keys", KeysJson(table, chunk)));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    found[reader.GetString(0)] = reader.GetString(1);
                }
            }

            foreach (var key in wanted)
            {
                rows.Add(found.TryGetValue(key, out var json)
                    ? new RelayRow(table.Name, key, Outbound(table, json, wrap))
                    : new RelayRow(table.Name, key, null));
            }
        }

        await scope.CommitAsync(cancellationToken);
        return new RelayFeedBatch(epoch, after, highWater, head, RelayFeedOutcome.Ok, rows);
    }

    public async Task WriteSnapshotAsync(
        Guid clinicId, IReadOnlyCollection<string>? tables, RelayOutboundWrap wrap, Stream output,
        CancellationToken cancellationToken)
    {
        var epoch = await FeedEpochAsync(cancellationToken);
        await using var scope = await OpenAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var highWater = await HighWaterAsync(scope, clinicId, cancellationToken);
        var head = await FingerprintAsync(scope, clinicId, highWater, cancellationToken);

        await using var writer = new Utf8JsonWriter(output);
        writer.WriteStartObject();
        writer.WriteString("epoch", epoch);
        writer.WriteNumber("highWater", highWater);
        writer.WriteString("head", head);
        writer.WriteStartArray("tables");

        foreach (var table in Selected(tables))
        {
            writer.WriteStartObject();
            writer.WriteString("table", table.Name);
            writer.WriteStartArray("rows");
            await using (var command = scope.Command(
                             $"SELECT ({RowJson(table, "a")})::text FROM {table.QualifiedName} a WHERE {Scope(table, "a", 0)} "
                             + $"ORDER BY {KeyExpression(table, "a")}",
                             ("clinic", clinicId)))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var json = reader.GetString(0);
                    if (NeedsOutboundTransform(table))
                    {
                        Outbound(table, json, wrap)!.Value.WriteTo(writer);
                    }
                    else
                    {
                        writer.WriteRawValue(json, skipInputValidation: true);
                    }
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RelayTableDigest>> DigestAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        await using var scope = await OpenAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var digests = new List<RelayTableDigest>();
        foreach (var table in Plan.Tables)
        {
            var withoutPerSide = ClinicRelayScope.PerSideColumns.TryGetValue(table.Name, out var perSide)
                ? string.Concat(perSide.Select(p => $" - {Literal(ColumnOf(table, p))}"))
                : string.Empty;
            var sql = $"SELECT count(*), md5(coalesce(string_agg(md5(s.j::text), ',' ORDER BY s.k), '')) FROM ("
                      + $"SELECT ({RowJson(table, "a")}){withoutPerSide} AS j, {KeyExpression(table, "a")} AS k "
                      + $"FROM {table.QualifiedName} a WHERE {Scope(table, "a", 0)}) s";
            await using var command = scope.Command(sql, ("clinic", clinicId));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            digests.Add(new RelayTableDigest(table.Name, reader.GetInt64(0), reader.GetString(1)));
        }

        await scope.CommitAsync(cancellationToken);
        return digests;
    }

    public async Task ApplyBatchAsync(
        Guid clinicId, IReadOnlyList<RelayRow> rows, RelayInboundUnwrap unwrap, CancellationToken cancellationToken)
    {
        var byTable = rows
            .GroupBy(r => r.Table, StringComparer.Ordinal)
            .ToDictionary(g => Plan.Find(g.Key) ?? throw new InvalidOperationException($"Table inconnue : {g.Key}"),
                g => g.ToList());

        await using var scope = await OpenAsync(IsolationLevel.ReadCommitted, cancellationToken);

        foreach (var table in Plan.Tables.Reverse().Where(byTable.ContainsKey))
        {
            var tombstones = byTable[table].Where(r => r.Row is null).Select(r => r.Key).ToList();
            foreach (var chunk in tombstones.Chunk(ChunkRows))
            {
                await scope.ExecuteAsync(
                    $"DELETE FROM {table.QualifiedName} a WHERE {KeyMatch(table, "a")}",
                    cancellationToken, ("keys", KeysJson(table, chunk)));
            }
        }

        var upserted = new List<(ClinicRelayTable Table, List<JsonElement> Rows)>();
        foreach (var table in Plan.Tables.Where(byTable.ContainsKey))
        {
            var live = byTable[table].Where(r => r.Row is not null).Select(r => Inbound(table, r.Row!.Value, unwrap)).ToList();
            await UpsertAsync(scope, table, live, cancellationToken);
            upserted.Add((table, live));
        }

        await WriteDeferredAsync(scope, upserted, cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    public async Task ReplaceAsync(
        Guid clinicId, Stream snapshot, IReadOnlyCollection<string>? onlyTables, RelayInboundUnwrap unwrap,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(snapshot, cancellationToken: cancellationToken);
        var sections = document.RootElement.GetProperty("tables").EnumerateArray()
            .ToDictionary(t => t.GetProperty("table").GetString()!, t => t.GetProperty("rows"), StringComparer.Ordinal);

        var selected = Selected(onlyTables).Where(t => sections.ContainsKey(t.Name)).ToList();
        await using var scope = await OpenAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var upserted = new List<(ClinicRelayTable Table, List<JsonElement> Rows)>();
        foreach (var table in selected)
        {
            var live = sections[table.Name].EnumerateArray().Select(r => Inbound(table, r, unwrap)).ToList();
            await UpsertAsync(scope, table, live, cancellationToken);
            upserted.Add((table, live));
        }

        await WriteDeferredAsync(scope, upserted, cancellationToken);

        // The rest of each table goes after every upsert, children first, so nothing is left pointing at a deleted row.
        foreach (var (table, live) in Enumerable.Reverse(upserted))
        {
            var keep = JsonArrayOf(live.Select(r => KeyObject(table, r)));
            await scope.ExecuteAsync(
                $"DELETE FROM {table.QualifiedName} a WHERE {Scope(table, "a", 0)} AND NOT EXISTS ("
                + $"SELECT 1 FROM json_populate_recordset(NULL::{table.QualifiedName}, @keep::json) k "
                + $"WHERE {string.Join(" AND ", table.KeyColumns.Select(c => $"k.{Q(c)} = a.{Q(c)}"))})",
                cancellationToken, ("clinic", clinicId), ("keep", keep));
        }

        await scope.CommitAsync(cancellationToken);
    }

    public async Task<bool> ClinicNamesKeyAsync(Guid clinicId, string storageKey, CancellationToken cancellationToken)
    {
        await using var scope = await OpenAsync(IsolationLevel.ReadCommitted, cancellationToken);
        foreach (var (table, column) in BlobColumns())
        {
            await using var command = scope.Command(
                $"SELECT 1 FROM {table.QualifiedName} a WHERE a.{Q(column)} = @key AND {Scope(table, "a", 0)} LIMIT 1",
                ("clinic", clinicId), ("key", storageKey));
            if (await command.ExecuteScalarAsync(cancellationToken) is not null)
            {
                await scope.CommitAsync(cancellationToken);
                return true;
            }
        }

        await scope.CommitAsync(cancellationToken);
        return false;
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        await using var scope = await OpenAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (table, column) in BlobColumns())
        {
            await using var command = scope.Command(
                $"SELECT a.{Q(column)} FROM {table.QualifiedName} a WHERE a.{Q(column)} IS NOT NULL AND {Scope(table, "a", 0)}",
                ("clinic", clinicId));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add(reader.GetString(0));
            }
        }

        await scope.CommitAsync(cancellationToken);
        return keys.ToList();
    }

    /// <summary>The archive's declared blob columns, resolved against the relay plan — one list for both copies.</summary>
    private IEnumerable<(ClinicRelayTable Table, string Column)> BlobColumns() =>
        ClinicArchiveScope.BlobProperties
            .Select(b => (Table: Plan.Find(b.Key), Properties: b.Value))
            .Where(b => b.Table is not null)
            .SelectMany(b => b.Properties.Select(p => (b.Table!, ColumnOf(b.Table!, p))));

    private IEnumerable<ClinicRelayTable> Selected(IReadOnlyCollection<string>? tables) =>
        tables is null || tables.Count == 0
            ? Plan.Tables
            : Plan.Tables.Where(t => tables.Contains(t.Name, StringComparer.Ordinal));

    private async Task UpsertAsync(
        RelayScope scope, ClinicRelayTable table, IReadOnlyList<JsonElement> rows, CancellationToken cancellationToken,
        IReadOnlySet<string>? preserve = null)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var columns = (await ColumnsAsync(scope, table.Schema, table.TableName, cancellationToken))
            .Where(c => !table.DeferredColumns.Contains(c, StringComparer.Ordinal))
            .ToList();
        var updates = columns
            .Where(c => !table.KeyColumns.Contains(c, StringComparer.Ordinal))
            .Where(c => preserve is null || !preserve.Contains(c))
            .ToList();
        var columnList = string.Join(", ", columns.Select(Q));
        var conflict = updates.Count == 0
            ? "DO NOTHING"
            : "DO UPDATE SET " + string.Join(", ", updates.Select(c => $"{Q(c)} = EXCLUDED.{Q(c)}"));

        foreach (var chunk in rows.Chunk(ChunkRows))
        {
            var json = JsonArrayOf(chunk.Select(r => r.GetRawText()));
            await scope.ExecuteAsync(
                $"INSERT INTO {table.QualifiedName} ({columnList}) SELECT {columnList} FROM "
                + $"json_populate_recordset(NULL::{table.QualifiedName}, @rows::json) "
                + $"ON CONFLICT ({string.Join(", ", table.KeyColumns.Select(Q))}) {conflict}",
                cancellationToken, ("rows", json));

            foreach (var owned in table.OwnedCollections)
            {
                await ReplaceOwnedAsync(scope, table, owned, chunk, json, cancellationToken);
            }
        }
    }

    private async Task ReplaceOwnedAsync(
        RelayScope scope, ClinicRelayTable table, ClinicRelayOwnedTable owned, IReadOnlyList<JsonElement> owners,
        string ownersJson, CancellationToken cancellationToken)
    {
        var ownerMatch = string.Join(" AND ",
            owned.OwnerColumns.Zip(owned.OwnerKeyColumns, (mine, theirs) => $"o.{Q(mine)} = r.{Q(theirs)}"));
        await scope.ExecuteAsync(
            $"DELETE FROM {owned.QualifiedName} o USING json_populate_recordset(NULL::{table.QualifiedName}, @rows::json) r "
            + $"WHERE {ownerMatch}",
            cancellationToken, ("rows", ownersJson));

        var children = owners
            .Where(r => r.TryGetProperty(OwnedProperty, out var o) && o.TryGetProperty(owned.Navigation, out _))
            .SelectMany(r => r.GetProperty(OwnedProperty).GetProperty(owned.Navigation).EnumerateArray())
            .Select(c => c.GetRawText())
            .ToList();
        if (children.Count == 0)
        {
            return;
        }

        var columns = (await ColumnsAsync(scope, owned.Schema, owned.TableName, cancellationToken))
            .Where(c => !owned.GeneratedColumns.Contains(c, StringComparer.Ordinal))
            .Select(Q)
            .ToList();
        var list = string.Join(", ", columns);
        await scope.ExecuteAsync(
            $"INSERT INTO {owned.QualifiedName} ({list}) SELECT {list} FROM json_populate_recordset(NULL::{owned.QualifiedName}, @rows::json)",
            cancellationToken, ("rows", JsonArrayOf(children)));
    }

    private static async Task WriteDeferredAsync(
        RelayScope scope, IEnumerable<(ClinicRelayTable Table, List<JsonElement> Rows)> upserted,
        CancellationToken cancellationToken)
    {
        foreach (var (table, rows) in upserted.Where(u => u.Table.DeferredColumns.Count > 0 && u.Rows.Count > 0))
        {
            var set = string.Join(", ", table.DeferredColumns.Select(c => $"{Q(c)} = r.{Q(c)}"));
            var match = string.Join(" AND ", table.KeyColumns.Select(c => $"a.{Q(c)} = r.{Q(c)}"));
            foreach (var chunk in rows.Chunk(ChunkRows))
            {
                await scope.ExecuteAsync(
                    $"UPDATE {table.QualifiedName} a SET {set} FROM json_populate_recordset(NULL::{table.QualifiedName}, @rows::json) r WHERE {match}",
                    cancellationToken, ("rows", JsonArrayOf(chunk.Select(r => r.GetRawText()))));
            }
        }
    }

    // ---- SQL fragments -----------------------------------------------------------------------------------------

    private static string Q(string identifier) => ClinicRelaySql.Quote(identifier);

    private static string Literal(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>The key as the change log writes it: <see cref="ClinicChange.KeySeparator"/>-joined text.</summary>
    private static string KeyExpression(ClinicRelayTable table, string alias) =>
        table.KeyColumns.Count == 1
            ? $"{alias}.{Q(table.KeyColumns[0])}::text"
            : $"concat_ws(E'\\x1f', {string.Join(", ", table.KeyColumns.Select(c => $"{alias}.{Q(c)}::text"))})";

    private static string KeyMatch(ClinicRelayTable table, string alias) =>
        $"EXISTS (SELECT 1 FROM json_populate_recordset(NULL::{table.QualifiedName}, @keys::json) k WHERE "
        + string.Join(" AND ", table.KeyColumns.Select(c => $"k.{Q(c)} = {alias}.{Q(c)}")) + ")";

    /// <summary>The SQL predicate scoping <paramref name="alias"/> to <c>@clinic</c>: its own column, or up the parent chain.</summary>
    private string Scope(ClinicRelayTable table, string alias, int depth)
    {
        switch (table.Scope)
        {
            case ClinicRelayTableScope.Self:
                return $"{alias}.{Q(table.KeyColumns[0])} = @clinic";
            case ClinicRelayTableScope.Direct:
                return $"{alias}.{Q(table.ClinicColumn!)} = @clinic";
            default:
                var parent = Plan.Find(table.ParentTable!)!;
                var parentAlias = $"p{depth}";
                return $"{alias}.{Q(table.ParentColumn!)} IN (SELECT {parentAlias}.{Q(table.ParentKeyColumn!)} "
                       + $"FROM {parent.QualifiedName} {parentAlias} WHERE {Scope(parent, parentAlias, depth + 1)})";
        }
    }

    /// <summary>The row as jsonb, its owned lists nested under <c>$owned</c> with their generated ids left out (D5).</summary>
    private static string RowJson(ClinicRelayTable table, string alias)
    {
        var row = $"row_to_json({alias})::jsonb";
        if (table.OwnedCollections.Count == 0)
        {
            return row;
        }

        var parts = table.OwnedCollections.Select(owned =>
        {
            var strip = string.Concat(owned.GeneratedColumns.Select(c => $" - {Literal(c)}"));
            var element = $"(row_to_json(o)::jsonb{strip})";
            var match = string.Join(" AND ",
                owned.OwnerColumns.Zip(owned.OwnerKeyColumns, (mine, theirs) => $"o.{Q(mine)} = {alias}.{Q(theirs)}"));
            return $"{Literal(owned.Navigation)}, coalesce((SELECT jsonb_agg({element} ORDER BY {element}::text) "
                   + $"FROM {owned.QualifiedName} o WHERE {match}), '[]'::jsonb)";
        });

        return $"{row} || jsonb_build_object({Literal(OwnedProperty)}, jsonb_build_object({string.Join(", ", parts)}))";
    }

    private static string ColumnOf(ClinicRelayTable table, string propertyName)
    {
        var property = table.EntityType.FindProperty(propertyName)
                       ?? throw new InvalidOperationException($"{table.Name}.{propertyName} n'existe pas.");
        return property.GetColumnName(StoreObjectIdentifier.Table(table.TableName, table.Schema)) ?? propertyName;
    }

    private static string KeysJson(ClinicRelayTable table, IEnumerable<string> keys) =>
        JsonArrayOf(keys.Select(key =>
        {
            var parts = key.Split(ClinicChange.KeySeparator);
            var obj = new JsonObject();
            for (var i = 0; i < table.KeyColumns.Count; i++)
            {
                obj[table.KeyColumns[i]] = i < parts.Length ? parts[i] : null;
            }

            return obj.ToJsonString();
        }));

    private static string KeyObject(ClinicRelayTable table, JsonElement row)
    {
        var obj = new JsonObject();
        foreach (var column in table.KeyColumns)
        {
            obj[column] = row.TryGetProperty(column, out var value) ? JsonNode.Parse(value.GetRawText()) : null;
        }

        return obj.ToJsonString();
    }

    private static string JsonArrayOf(IEnumerable<string> items) => "[" + string.Join(",", items) + "]";

    // ---- secrets on the wire -----------------------------------------------------------------------------------

    private static bool NeedsOutboundTransform(ClinicRelayTable table) =>
        ClinicRelayScope.WrappedSecrets.ContainsKey(table.Name) || ClinicRelayScope.Redacted.ContainsKey(table.Name);

    private JsonElement? Outbound(ClinicRelayTable table, string json, RelayOutboundWrap wrap)
    {
        if (!NeedsOutboundTransform(table))
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        var node = JsonNode.Parse(json)!.AsObject();
        if (ClinicRelayScope.Redacted.TryGetValue(table.Name, out var redacted))
        {
            foreach (var property in redacted)
            {
                node[ColumnOf(table, property)] = null;
            }
        }

        if (ClinicRelayScope.WrappedSecrets.TryGetValue(table.Name, out var wrapped))
        {
            foreach (var property in wrapped)
            {
                var column = ColumnOf(table, property);
                var value = node[column]?.GetValue<string>();
                node[column] = !string.IsNullOrEmpty(value) && _secrets.TryUnprotect(value, out var plain)
                    ? RelaySecretEnvelope.Seal(plain, wrap.RelayPublicKey)
                    : null;
            }
        }

        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static JsonElement Inbound(ClinicRelayTable table, JsonElement row, RelayInboundUnwrap unwrap)
    {
        if (!ClinicRelayScope.WrappedSecrets.TryGetValue(table.Name, out var wrapped))
        {
            return row;
        }

        var node = JsonNode.Parse(row.GetRawText())!.AsObject();
        foreach (var property in wrapped)
        {
            var column = ColumnOf(table, property);
            var value = node[column]?.GetValue<string>();
            node[column] = string.IsNullOrEmpty(value) ? null : unwrap.UnwrapToProtected(value);
        }

        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    // ---- plumbing ----------------------------------------------------------------------------------------------

    private static async Task<long> HighWaterAsync(RelayScope scope, Guid clinicId, CancellationToken cancellationToken)
    {
        await using var command = scope.Command(
            "SELECT \"LastSeq\" FROM \"ClinicChangeCursors\" WHERE \"ClinicId\" = @clinic", ("clinic", clinicId));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<string?> FingerprintAsync(
        RelayScope scope, Guid clinicId, long seq, CancellationToken cancellationToken)
    {
        if (seq <= 0)
        {
            return null;
        }

        await using var command = scope.Command(
            "SELECT \"Table\", \"EntityKey\", \"Op\", \"RecordedAtUtc\" FROM \"ClinicChanges\" "
            + "WHERE \"ClinicId\" = @clinic AND \"Seq\" = @seq",
            ("clinic", clinicId), ("seq", seq));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var text = string.Join('\u001f',
            seq.ToString(CultureInfo.InvariantCulture), reader.GetString(0), reader.GetString(1),
            reader.GetInt32(2).ToString(CultureInfo.InvariantCulture),
            reader.GetDateTime(3).ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static async Task<IReadOnlyList<string>> ColumnsAsync(
        RelayScope scope, string? schema, string table, CancellationToken cancellationToken)
    {
        var cacheKey = (schema ?? string.Empty) + "." + table;
        if (ColumnCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var columns = new List<string>();
        await using (var command = scope.Command(
                         "SELECT column_name FROM information_schema.columns WHERE table_schema = coalesce(@schema, current_schema()) "
                         + "AND table_name = @table AND is_generated = 'NEVER' ORDER BY ordinal_position",
                         ("schema", (object?)schema ?? DBNull.Value), ("table", table)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(0));
            }
        }

        ColumnCache[cacheKey] = columns;
        return columns;
    }

    private async Task<RelayScope> OpenAsync(IsolationLevel isolation, CancellationToken cancellationToken)
    {
        var transaction = await _db.Database.BeginTransactionAsync(isolation, cancellationToken);
        var scope = new RelayScope(_db.Database.GetDbConnection(), transaction);
        await scope.ExecuteAsync("SET LOCAL TimeZone = 'UTC'", cancellationToken);
        return scope;
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var scope = await OpenAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await scope.ExecuteAsync(sql, cancellationToken, parameters);
        await scope.CommitAsync(cancellationToken);
    }

    private async Task<object?> ScalarAsync(
        RelayScope? scope, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var own = scope is null ? await OpenAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
        var active = scope ?? own!;
        await using var command = active.Command(sql, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (own is not null)
        {
            await own.CommitAsync(cancellationToken);
        }

        return value is DBNull ? null : value;
    }

    /// <summary>Outside any transaction, so a refused catalog function (no privilege) cannot abort the caller's.</summary>
    private async Task<string?> TryScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await _db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        catch (DbException)
        {
            return null;
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>One transaction on the context's connection, with typed-by-value parameters.</summary>
    private sealed class RelayScope : IAsyncDisposable
    {
        private readonly DbConnection _connection;
        private readonly IDbContextTransaction _transaction;
        private readonly bool _owned;
        private bool _committed;

        /// <param name="owned">False for the caller's own transaction (D18): neither committed nor rolled back here.</param>
        public RelayScope(DbConnection connection, IDbContextTransaction transaction, bool owned = true)
        {
            _connection = connection;
            _transaction = transaction;
            _owned = owned;
        }

        public DbCommand Command(string sql, params (string Name, object? Value)[] parameters)
        {
            var command = _connection.CreateCommand();
            command.Transaction = _transaction.GetDbTransaction();
            command.CommandText = sql;
            command.CommandTimeout = 300;
            foreach (var (name, value) in parameters)
            {
                ClinicChangeCapture.AddParameter(command, name, value);
            }

            return command;
        }

        public async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
        {
            await using var command = Command(sql, parameters);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            if (_owned)
            {
                await _transaction.CommitAsync(cancellationToken);
            }

            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_owned)
            {
                return;
            }

            if (!_committed)
            {
                await _transaction.RollbackAsync();
            }

            await _transaction.DisposeAsync();
        }
    }
}
