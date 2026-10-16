using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// The return (<c>clinic-pc-copy</c> D18) on the relay's row access: the PC reads its cut, the cloud applies it. Same SQL
/// shapes as the copy (<c>row_to_json</c> out, <c>json_populate_recordset</c> in), in the other direction.
/// </summary>
public sealed partial class ClinicRelayRowStore : IRelayHandbackStore
{
    // ---- the PC de secours ---------------------------------------------------------------------------------------

    public async Task<RelayHandbackRequest> ReadCutAsync(
        Guid clinicId, Guid handbackId, long baseAppliedSeq, DateTime cutSinceUtc, CancellationToken cancellationToken)
    {
        // The PC already refuses new saves (the lease says « handing back »). A save past its gate holds this row's lock
        // from its first change until its commit: taking the lock waits for every such save, and one arriving after
        // re-checks the lease under the same lock and is refused (ClinicChangeCapture). Nothing can land after the read.
        await using (var barrier = await OpenAsync(IsolationLevel.ReadCommitted, cancellationToken))
        {
            await barrier.ExecuteAsync(
                "SELECT \"LastSeq\" FROM \"ClinicChangeCursors\" WHERE \"ClinicId\" = @clinic FOR UPDATE",
                cancellationToken, ("clinic", clinicId));
            await barrier.CommitAsync(cancellationToken);
        }

        await using var scope = await OpenAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var changes = new List<RelayHandbackChange>();
        await using (var command = scope.Command(
                         "SELECT \"Seq\", \"Table\", \"EntityKey\", \"Op\", \"IdempotencyKey\", \"RecordedAtUtc\" "
                         + "FROM \"ClinicChanges\" WHERE \"ClinicId\" = @clinic ORDER BY \"Seq\"",
                         ("clinic", clinicId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = reader.GetString(1);
                if (RelayHandbackRules.NeverReturned.Contains(table))
                {
                    continue;
                }

                changes.Add(new RelayHandbackChange(
                    reader.GetInt64(0), table, reader.GetString(2), reader.GetInt32(3) == (int)ClinicChangeOp.Delete,
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)));
            }
        }

        var keys = changes.Select(c => new RelayRowKey(c.Table, c.Key)).Distinct().ToList();
        var current = await ReadRowsAsync(scope, clinicId, keys, cancellationToken);
        var rows = keys
            .Select(k => current.TryGetValue(k, out var json)
                ? new RelayRow(k.Table, k.Key, JsonDocument.Parse(json).RootElement.Clone())
                : new RelayRow(k.Table, k.Key, null))
            .ToList();

        var journal = await _db.AuditEntries.AsNoTracking()
            .Where(a => a.ClinicId == clinicId && a.OccurredAt >= cutSinceUtc)
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Sequence)
            .Select(a => new RelayHandbackJournalEntry(
                a.UserId, a.UserEmail, a.EntityType, a.EntityId, (int)a.Action, a.ChangedFields, a.OccurredAt, a.IsDeclaredGap))
            .ToListAsync(cancellationToken);

        var signIns = await _db.Users.AsNoTracking()
            .Where(u => u.ClinicId == clinicId)
            .Select(u => new RelaySignInTrace(u.Id, u.LastLoginAt, u.FailedLoginAttempts, u.LockoutEnd))
            .ToListAsync(cancellationToken);

        var codes = await _db.Set<UserRecoveryCode>().AsNoTracking()
            .Where(c => c.IsUsed && c.UsedAt != null && c.User.ClinicId == clinicId)
            .Select(c => new RelayRecoveryCodeUse(c.Id, c.UsedAt!.Value))
            .ToListAsync(cancellationToken);

        await scope.CommitAsync(cancellationToken);
        return new RelayHandbackRequest(handbackId, baseAppliedSeq, cutSinceUtc, changes, rows, journal, signIns, codes);
    }

    public IReadOnlyList<string> FileKeys(IReadOnlyList<RelayRow> rows)
    {
        var columns = BlobColumns()
            .GroupBy(b => b.Table.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(b => b.Column).ToList(), StringComparer.Ordinal);
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Row is not { } json || !columns.TryGetValue(row.Table, out var names))
            {
                continue;
            }

            foreach (var name in names)
            {
                if (json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } key)
                {
                    keys.Add(key);
                }
            }
        }

        return keys.ToList();
    }

    public Task ForgetCutAsync(Guid clinicId, CancellationToken cancellationToken) => DropCursorAsync(clinicId, cancellationToken);

    // ---- the cloud, inside the caller's transaction --------------------------------------------------------------

    public async Task<IReadOnlyList<string>> KeysWrittenByReturnAsync(
        Guid clinicId, string table, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        await using var scope = await OpenAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var keys = new List<string>();
        await using (var command = scope.Command(
                         "SELECT DISTINCT \"EntityKey\" FROM \"ClinicChanges\" WHERE \"ClinicId\" = @clinic AND \"Table\" = @table "
                         + "AND \"Origin\" = @origin AND \"Op\" = @op AND \"RecordedAtUtc\" >= @since",
                         ("clinic", clinicId), ("table", table), ("origin", (int)ClinicChangeOrigin.Relay),
                         ("op", (int)ClinicChangeOp.Upsert), ("since", DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc))))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add(reader.GetString(0));
            }
        }

        await scope.CommitAsync(cancellationToken);
        return keys;
    }

    public async Task<IReadOnlyList<RelayCloudChange>> CloudChangesAfterAsync(
        Guid clinicId, long afterSeq, CancellationToken cancellationToken)
    {
        await using var scope = await AmbientAsync(cancellationToken);
        var changes = new List<RelayCloudChange>();
        await using var command = scope.Command(
            "SELECT \"Seq\", \"Table\", \"EntityKey\", \"Op\", \"Origin\", \"IdempotencyKey\", \"RecordedAtUtc\", \"Automatic\" "
            + "FROM \"ClinicChanges\" WHERE \"ClinicId\" = @clinic AND \"Seq\" > @after ORDER BY \"Seq\"",
            ("clinic", clinicId), ("after", afterSeq));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            changes.Add(new RelayCloudChange(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3) == (int)ClinicChangeOp.Delete,
                reader.GetInt32(4) == (int)ClinicChangeOrigin.Relay,
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc),
                reader.GetBoolean(7)));
        }

        return changes;
    }

    public async Task<IReadOnlyDictionary<RelayRowKey, string>> CurrentRowsAsync(
        Guid clinicId, IReadOnlyCollection<RelayRowKey> keys, CancellationToken cancellationToken)
    {
        await using var scope = await AmbientAsync(cancellationToken);
        return await ReadRowsAsync(scope, clinicId, keys, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<RelayRowKey, string>> AuthorsAsync(
        Guid clinicId, IReadOnlyCollection<RelayRowKey> keys, CancellationToken cancellationToken)
    {
        var authors = new Dictionary<RelayRowKey, string>();
        if (keys.Count == 0)
        {
            return authors;
        }

        await using var scope = await AmbientAsync(cancellationToken);
        await using var command = scope.Command(
            "SELECT DISTINCT ON (a.\"EntityType\", a.\"EntityId\") a.\"EntityType\", a.\"EntityId\", "
            + "coalesce(a.\"UserEmail\", a.\"UserId\") FROM \"AuditEntries\" a "
            + "JOIN unnest(@types, @ids) AS k(t, i) ON k.t = a.\"EntityType\" AND k.i = a.\"EntityId\" "
            + "WHERE a.\"ClinicId\" = @clinic ORDER BY a.\"EntityType\", a.\"EntityId\", a.\"OccurredAt\" DESC",
            ("clinic", clinicId),
            ("types", keys.Select(k => k.Table).ToArray()),
            ("ids", keys.Select(k => k.Key).ToArray()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            authors[new RelayRowKey(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
        }

        return authors;
    }

    public IEnumerable<RelayRowKey> References(string table, JsonElement row)
    {
        var plan = Plan;
        var described = plan.Find(table);
        if (described is null)
        {
            yield break;
        }

        var store = StoreObjectIdentifier.Table(described.TableName, described.Schema);
        foreach (var fk in described.EntityType.GetForeignKeys().Where(f => f.Properties.Count == 1))
        {
            var principal = plan.Find(fk.PrincipalEntityType.ClrType);
            if (principal is null || principal.KeyColumns.Count != 1)
            {
                continue;
            }

            var column = fk.Properties[0].GetColumnName(store) ?? fk.Properties[0].Name;
            if (row.TryGetProperty(column, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                yield return new RelayRowKey(principal.Name,
                    value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText());
            }
        }
    }

    public RelayRowKey? Owner(string table, JsonElement row)
    {
        var described = Plan.Find(table);
        if (described is not { Scope: ClinicRelayTableScope.Child, ParentTable: { } parentTable, ParentColumn: { } column }
            || !row.TryGetProperty(column, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return new RelayRowKey(parentTable, value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText());
    }

    public async Task<IReadOnlyList<RelayRowKey>> ApplyReturnAsync(
        Guid clinicId, RelayHandbackRequest request, RelayHandbackPlan plan, CancellationToken cancellationToken)
    {
        await using var scope = await AmbientAsync(cancellationToken);
        foreach (var unknown in plan.Apply.Select(r => r.Table).Where(t => Plan.Find(t) is null))
        {
            throw new InvalidOperationException($"Table inconnue : {unknown}");
        }

        // ⚠️ One record the database refuses never stops the rest (review finding 2, 2026-10-13): the whole cut goes in
        // one go; refused, it goes again record by record, each under its own savepoint, and a record refused alone is set
        // aside — the caller lists it « À reprendre ». Before, one booking clash rolled the whole cut back and the PC
        // retried every 30 s for ever, the cloud read-only for everybody meanwhile.
        var notApplied = new List<RelayRowKey>();
        await scope.ExecuteAsync("SAVEPOINT relay_return", cancellationToken);
        try
        {
            await ApplyRowsAsync(scope, clinicId, request.HandbackId, plan.Apply, cancellationToken);
            await scope.ExecuteAsync("RELEASE SAVEPOINT relay_return", cancellationToken);
        }
        catch (Npgsql.PostgresException ex) when (IsRefusedRow(ex))
        {
            await scope.ExecuteAsync("ROLLBACK TO SAVEPOINT relay_return", cancellationToken);
            foreach (var record in Records(plan.Apply))
            {
                await scope.ExecuteAsync("SAVEPOINT relay_record", cancellationToken);
                try
                {
                    await ApplyRowsAsync(scope, clinicId, request.HandbackId, record, cancellationToken);
                    await scope.ExecuteAsync("RELEASE SAVEPOINT relay_record", cancellationToken);
                }
                catch (Npgsql.PostgresException one) when (IsRefusedRow(one))
                {
                    await scope.ExecuteAsync("ROLLBACK TO SAVEPOINT relay_record", cancellationToken);
                    notApplied.AddRange(record.Select(r => new RelayRowKey(r.Table, r.Key)));
                }
            }
        }

        // The cut's provisional numbers become the cloud's next real ones — the PC de secours never gives a legal number.
        var renumbered = await RenumberProvisionalAsync(scope, clinicId, cancellationToken);

        var mergedUsers = await MergeSignInsAsync(scope, clinicId, request.SignIns, cancellationToken);
        var spentCodes = await MergeRecoveryCodesAsync(scope, clinicId, request.RecoveryCodesUsed, cancellationToken);

        // The cloud's log of what the return wrote, so the PC's copy follows (a dropped duplicate leaves it), and so a
        // later return knows these rows are the cabinet's own (Origin = Relay), never a conflict.
        var refused = notApplied.ToHashSet();
        var saveOf = request.Changes
            .GroupBy(c => new RelayRowKey(c.Table, c.Key))
            .ToDictionary(g => g.Key, g => g.MaxBy(c => c.Seq)!.IdempotencyKey);
        var log = plan.Apply
            .Where(r => !refused.Contains(new RelayRowKey(r.Table, r.Key)))
            .Select(r => (r.Table, r.Key, Op: r.Row is null ? ClinicChangeOp.Delete : ClinicChangeOp.Upsert,
                Save: saveOf.GetValueOrDefault(new RelayRowKey(r.Table, r.Key))))
            .Concat(plan.Dropped.Select(k => (k.Table, k.Key, Op: ClinicChangeOp.Delete, Save: saveOf.GetValueOrDefault(k))))
            // The rows the cloud kept, and those it refused: logged so the PC's copy takes the cloud's version back (its own
            // was not applied — a row the cloud does not have is read as gone).
            .Concat((plan.Kept ?? Array.Empty<RelayRowKey>()).Concat(refused)
                .Select(k => (k.Table, k.Key, Op: ClinicChangeOp.Upsert, Save: (string?)null)))
            .Concat(renumbered.Select(k => (k.Table, k.Key, Op: ClinicChangeOp.Upsert, Save: (string?)null)))
            .Concat(mergedUsers.Select(id => (Table: nameof(User), Key: id, Op: ClinicChangeOp.Upsert, Save: (string?)null)))
            .Concat(spentCodes.Select(id => (Table: nameof(UserRecoveryCode), Key: id, Op: ClinicChangeOp.Upsert, Save: (string?)null)))
            .GroupBy(c => (c.Table, c.Key))
            .Select(g => g.First())
            .OrderBy(c => c.Table, StringComparer.Ordinal).ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToList();
        if (log.Count == 0)
        {
            return notApplied;
        }

        object? last;
        await using (var advance = scope.Command(
                         "UPDATE \"ClinicChangeCursors\" SET \"LastSeq\" = \"LastSeq\" + @n WHERE \"ClinicId\" = @clinic RETURNING \"LastSeq\"",
                         ("n", (long)log.Count), ("clinic", clinicId)))
        {
            last = await advance.ExecuteScalarAsync(cancellationToken);
        }

        if (last is null or DBNull)
        {
            throw new InvalidOperationException("Le journal des modifications du cabinet n'est pas ouvert sur le cloud.");
        }

        var seq = Convert.ToInt64(last, CultureInfo.InvariantCulture) - log.Count;
        var now = DateTime.UtcNow;
        var entries = log.Select(c =>
        {
            var obj = new JsonObject
            {
                ["ClinicId"] = clinicId.ToString("D"),
                ["Seq"] = ++seq,
                ["Table"] = c.Table,
                ["EntityKey"] = c.Key,
                ["Op"] = (int)c.Op,
                ["Origin"] = (int)ClinicChangeOrigin.Relay,
                ["IdempotencyKey"] = c.Save,
                ["RecordedAtUtc"] = now.ToString("O", CultureInfo.InvariantCulture),
            };
            return obj.ToJsonString();
        });

        foreach (var chunk in entries.Chunk(ChunkRows))
        {
            await scope.ExecuteAsync(
                "INSERT INTO \"ClinicChanges\" (\"ClinicId\", \"Seq\", \"Table\", \"EntityKey\", \"Op\", \"Origin\", "
                + "\"IdempotencyKey\", \"RecordedAtUtc\") SELECT \"ClinicId\", \"Seq\", \"Table\", \"EntityKey\", \"Op\", "
                + "\"Origin\", \"IdempotencyKey\", \"RecordedAtUtc\" FROM json_populate_recordset(NULL::\"ClinicChanges\", @rows::json)",
                cancellationToken, ("rows", JsonArrayOf(chunk)));
        }

        return notApplied;
    }

    /// <summary>
    /// Writes <paramref name="rows"/>: each cloud row first saved whole (RelayReturnBeforeImages — whatever the return gets
    /// wrong can be put back), deletions children-first, every key checked to be this cabinet's before and after, then the
    /// upserts parents-first. The booking constraint is deferred while the rows land — a visit moved into a slot another
    /// left is not a clash — and checked before this returns, so a refusal surfaces here, inside the caller's savepoint.
    /// </summary>
    private async Task ApplyRowsAsync(
        RelayScope scope, Guid clinicId, Guid returnId, IReadOnlyList<RelayRow> rows, CancellationToken cancellationToken)
    {
        var tables = Plan;
        var byTable = rows
            .GroupBy(r => r.Table, StringComparer.Ordinal)
            .ToDictionary(g => tables.Find(g.Key)!, g => g.ToList());

        await scope.ExecuteAsync("SET CONSTRAINTS ALL DEFERRED", cancellationToken);

        foreach (var table in tables.Tables.Where(byTable.ContainsKey))
        {
            foreach (var deleted in new[] { true, false })
            {
                var keys = byTable[table].Where(r => (r.Row is null) == deleted).Select(r => r.Key).ToList();
                foreach (var chunk in keys.Chunk(ChunkRows))
                {
                    await scope.ExecuteAsync(
                        "INSERT INTO \"RelayReturnBeforeImages\" (\"Id\", \"ClinicId\", \"ReturnId\", \"Table\", \"EntityKey\", "
                        + "\"RowJson\", \"Deleted\", \"SavedAtUtc\") "
                        + $"SELECT gen_random_uuid(), @clinic, @return, @table, {KeyExpression(table, "a")}, ({RowJson(table, "a")})::text, "
                        + $"@deleted, now() FROM {table.QualifiedName} a WHERE {KeyMatch(table, "a")} AND {Scope(table, "a", 0)}",
                        cancellationToken, ("keys", KeysJson(table, chunk)), ("clinic", clinicId), ("return", returnId),
                        ("table", table.Name), ("deleted", deleted));
                }
            }
        }

        foreach (var table in tables.Tables.Reverse().Where(byTable.ContainsKey))
        {
            var tombstones = byTable[table].Where(r => r.Row is null).Select(r => r.Key).ToList();
            foreach (var chunk in tombstones.Chunk(ChunkRows))
            {
                await scope.ExecuteAsync(
                    $"DELETE FROM {table.QualifiedName} a WHERE {KeyMatch(table, "a")} AND {Scope(table, "a", 0)}",
                    cancellationToken, ("keys", KeysJson(table, chunk)), ("clinic", clinicId));
            }
        }

        // A PC writes its own cabinet's rows and nothing else: no key may already belong to another cabinet...
        foreach (var table in tables.Tables.Where(byTable.ContainsKey))
        {
            var keys = byTable[table].Where(r => r.Row is not null).Select(r => r.Key).ToList();
            await EnsureNoneOutsideClinicAsync(scope, table, keys, clinicId, cancellationToken);
        }

        var upserted = new List<(ClinicRelayTable Table, List<JsonElement> Rows)>();
        foreach (var table in tables.Tables.Where(byTable.ContainsKey))
        {
            var live = byTable[table].Where(r => r.Row is not null).Select(r => r.Row!.Value).ToList();
            await UpsertAsync(scope, table, live, cancellationToken, CloudKeptColumns(table));
            upserted.Add((table, live));
        }

        await WriteDeferredAsync(scope, upserted, cancellationToken);

        // ...and every row written must sit in this cabinet once written (its ClinicId, or its parent's).
        foreach (var (table, _) in upserted)
        {
            await EnsureNoneOutsideClinicAsync(scope, table, byTable[table].Where(r => r.Row is not null).Select(r => r.Key).ToList(),
                clinicId, cancellationToken);
        }

        await scope.ExecuteAsync("SET CONSTRAINTS ALL IMMEDIATE", cancellationToken);
    }

    /// <summary>
    /// The cut's rows as records, in the order they can land: deletions first (a re-saved fiche's old teeth must go before
    /// its new ones arrive), then each record — a row with the rows it owns (a fiche and its acts, a note and its lines) —
    /// in the relay plan's parents-first order, so a new patient lands before the visit booked for him.
    /// </summary>
    private IEnumerable<IReadOnlyList<RelayRow>> Records(IReadOnlyList<RelayRow> rows)
    {
        var byKey = rows.ToDictionary(r => new RelayRowKey(r.Table, r.Key));
        var order = Plan.Tables.Select((t, i) => (t.Name, i)).ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);

        RelayRowKey RootOf(RelayRow row)
        {
            var key = new RelayRowKey(row.Table, row.Key);
            var seen = new HashSet<RelayRowKey>();
            while (seen.Add(key) && byKey.TryGetValue(key, out var current) && current.Row is { } json
                   && Owner(current.Table, json) is { } parent)
            {
                key = parent;
            }

            return key;
        }

        var deletions = rows.Where(r => r.Row is null)
            .OrderByDescending(r => order.GetValueOrDefault(r.Table))
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => (IReadOnlyList<RelayRow>)new[] { r });
        var records = rows.Where(r => r.Row is not null)
            .GroupBy(RootOf)
            .OrderBy(g => order.GetValueOrDefault(g.Key.Table))
            .ThenBy(g => g.Key.Key, StringComparer.Ordinal)
            .Select(g => (IReadOnlyList<RelayRow>)g.ToList());
        return deletions.Concat(records);
    }

    /// <summary>A row the database itself refuses (a clash, a duplicate value, a parent gone): an integrity violation, class 23.</summary>
    private static bool IsRefusedRow(Npgsql.PostgresException ex) => ex.SqlState.StartsWith("23", StringComparison.Ordinal);

    /// <summary>
    /// The documents numbered on the PC de secours during a cut, with their provisional number, and the column a real
    /// number follows the order of. Their types are <see cref="NumberedDocuments.Sequences"/>' (a guard holds the two equal).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RenumberedInIssueOrder = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(Invoice)] = nameof(Invoice.IssueDate),
        [nameof(TreatmentPlan)] = nameof(TreatmentPlan.AcceptedDate),
        [nameof(CreditNote)] = nameof(CreditNote.IssueDate),
    };

    /// <summary>
    /// Every provisional number this cabinet holds becomes the next real one of its year, in the order the documents were
    /// issued, after the highest real number — so the sequence stays gapless and nothing is numbered twice. Returns the
    /// keys renumbered, so the PC's copy takes the real numbers.
    /// </summary>
    private async Task<IReadOnlyList<RelayRowKey>> RenumberProvisionalAsync(
        RelayScope scope, Guid clinicId, CancellationToken cancellationToken)
    {
        var renumbered = new List<RelayRowKey>();
        foreach (var (name, issuedAt) in RenumberedInIssueOrder)
        {
            var table = Plan.Find(name);
            if (table is null)
            {
                continue;
            }

            var number = Q(ColumnOf(table, "Number"));
            var order = Q(ColumnOf(table, issuedAt));
            var created = Q(ColumnOf(table, "CreatedAt"));
            var id = Q(table.KeyColumns[0]);
            var next = "(coalesce(r.m, 0) + p.n)::text";
            await using var command = scope.Command(
                $"WITH prov AS (SELECT a.{id} AS id, substr(a.{number}, 1, 4) AS y, row_number() OVER ("
                + $"PARTITION BY substr(a.{number}, 1, 4) ORDER BY a.{order} NULLS LAST, a.{created}, a.{id}) AS n "
                + $"FROM {table.QualifiedName} a WHERE {Scope(table, "a", 0)} AND a.{number} ~ @provisional), "
                + $"real AS (SELECT substr(a.{number}, 1, 4) AS y, max(split_part(a.{number}, '-', 2)::int) AS m "
                + $"FROM {table.QualifiedName} a WHERE {Scope(table, "a", 0)} AND a.{number} ~ '^[0-9]{{4}}-[0-9]+$' GROUP BY 1) "
                + $"UPDATE {table.QualifiedName} t SET {number} = p.y || '-' || lpad({next}, greatest(4, length({next})), '0') "
                + $"FROM prov p LEFT JOIN real r ON r.y = p.y WHERE t.{id} = p.id RETURNING t.{id}::text",
                ("clinic", clinicId), ("provisional", ProvisionalNumber.SqlPattern));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                renumbered.Add(new RelayRowKey(name, reader.GetString(0)));
            }
        }

        return renumbered;
    }

    /// <summary>
    /// AC-5.7: what the PC never holds stays the cloud's — the Google link and the join code (redacted on the way out),
    /// a sealed secret, and each side's own sign-in traces.
    /// </summary>
    private static IReadOnlySet<string> CloudKeptColumns(ClinicRelayTable table)
    {
        var names = new List<string>();
        if (ClinicRelayScope.Redacted.TryGetValue(table.Name, out var redacted))
        {
            names.AddRange(redacted);
        }

        if (ClinicRelayScope.WrappedSecrets.TryGetValue(table.Name, out var wrapped))
        {
            names.AddRange(wrapped);
        }

        if (ClinicRelayScope.PerSideColumns.TryGetValue(table.Name, out var perSide))
        {
            names.AddRange(perSide);
        }

        return names.Select(n => ColumnOf(table, n)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>FR-11: the latest sign-in, the higher failure count, the later lockout — and nothing else of the account.</summary>
    private static async Task<IReadOnlyList<string>> MergeSignInsAsync(
        RelayScope scope, Guid clinicId, IReadOnlyList<RelaySignInTrace> traces, CancellationToken cancellationToken)
    {
        var merged = new List<string>();
        foreach (var trace in traces)
        {
            await using var command = scope.Command(
                "UPDATE \"Users\" SET \"LastLoginAt\" = GREATEST(\"LastLoginAt\", @login::timestamptz), "
                + "\"FailedLoginAttempts\" = GREATEST(\"FailedLoginAttempts\", @failed::int), "
                + "\"LockoutEnd\" = GREATEST(\"LockoutEnd\", @lockout::timestamptz) "
                + "WHERE \"Id\" = @id AND \"ClinicId\" = @clinic AND ("
                + "\"LastLoginAt\" IS DISTINCT FROM GREATEST(\"LastLoginAt\", @login::timestamptz) "
                + "OR \"FailedLoginAttempts\" < @failed::int "
                + "OR \"LockoutEnd\" IS DISTINCT FROM GREATEST(\"LockoutEnd\", @lockout::timestamptz)) RETURNING \"Id\"",
                ("login", (object?)Utc(trace.LastLoginAt) ?? DBNull.Value), ("failed", trace.FailedLoginAttempts),
                ("lockout", (object?)Utc(trace.LockoutEnd) ?? DBNull.Value), ("id", trace.UserId), ("clinic", clinicId));
            if (await command.ExecuteScalarAsync(cancellationToken) is string id)
            {
                merged.Add(id);
            }
        }

        return merged;
    }

    /// <summary>FR-11: a recovery code spent on the PC is spent on the cloud — it can never open an account twice.</summary>
    private static async Task<IReadOnlyList<string>> MergeRecoveryCodesAsync(
        RelayScope scope, Guid clinicId, IReadOnlyList<RelayRecoveryCodeUse> uses, CancellationToken cancellationToken)
    {
        var spent = new List<string>();
        foreach (var use in uses)
        {
            await using var command = scope.Command(
                "UPDATE \"UserRecoveryCodes\" r SET \"IsUsed\" = TRUE, \"UsedAt\" = @at FROM \"Users\" u "
                + "WHERE r.\"Id\" = @id AND r.\"UserId\" = u.\"Id\" AND u.\"ClinicId\" = @clinic AND NOT r.\"IsUsed\" "
                + "RETURNING r.\"Id\"",
                ("at", Utc(use.UsedAtUtc)!), ("id", use.Id), ("clinic", clinicId));
            if (await command.ExecuteScalarAsync(cancellationToken) is Guid id)
            {
                spent.Add(id.ToString("D"));
            }
        }

        return spent;
    }

    /// <summary>Refuses the whole return when one of <paramref name="keys"/> is a row of another cabinet.</summary>
    private async Task EnsureNoneOutsideClinicAsync(
        RelayScope scope, ClinicRelayTable table, IReadOnlyList<string> keys, Guid clinicId, CancellationToken cancellationToken)
    {
        foreach (var chunk in keys.Chunk(ChunkRows))
        {
            await using var command = scope.Command(
                $"SELECT count(*) FROM {table.QualifiedName} a WHERE {KeyMatch(table, "a")} AND NOT ({Scope(table, "a", 0)})",
                ("keys", KeysJson(table, chunk)), ("clinic", clinicId));
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0)
            {
                throw new InvalidOperationException($"Le retour nomme des lignes {table.Name} d'un autre cabinet.");
            }
        }
    }

    private static DateTime? Utc(DateTime? value) =>
        value is { } v ? DateTime.SpecifyKind(v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v, DateTimeKind.Utc) : null;

    /// <summary>Current rows by key, read in <paramref name="scope"/>; a key with no row is absent.</summary>
    private async Task<Dictionary<RelayRowKey, string>> ReadRowsAsync(
        RelayScope scope, Guid clinicId, IEnumerable<RelayRowKey> keys, CancellationToken cancellationToken)
    {
        var found = new Dictionary<RelayRowKey, string>();
        foreach (var group in keys.GroupBy(k => k.Table, StringComparer.Ordinal))
        {
            var table = Plan.Find(group.Key);
            if (table is null)
            {
                continue;
            }

            foreach (var chunk in group.Select(k => k.Key).Distinct(StringComparer.Ordinal).Chunk(ChunkRows))
            {
                await using var command = scope.Command(
                    $"SELECT {KeyExpression(table, "a")}, ({RowJson(table, "a")})::text FROM {table.QualifiedName} a "
                    + $"WHERE {KeyMatch(table, "a")} AND {Scope(table, "a", 0)}",
                    ("clinic", clinicId), ("keys", KeysJson(table, chunk)));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    found[new RelayRowKey(table.Name, reader.GetString(0))] = reader.GetString(1);
                }
            }
        }

        return found;
    }

    /// <summary>The caller's transaction (D18): the return commits with the journal and the relay row, or not at all.</summary>
    private async Task<RelayScope> AmbientAsync(CancellationToken cancellationToken)
    {
        var transaction = _db.Database.CurrentTransaction
                          ?? throw new InvalidOperationException("Le retour s'applique dans la transaction de l'appelant.");
        var scope = new RelayScope(_db.Database.GetDbConnection(), transaction, owned: false);
        await scope.ExecuteAsync("SET LOCAL TimeZone = 'UTC'", cancellationToken);
        return scope;
    }
}
