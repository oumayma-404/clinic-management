using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
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

    public async Task<IReadOnlyList<RelayCloudChange>> CloudChangesAfterAsync(
        Guid clinicId, long afterSeq, CancellationToken cancellationToken)
    {
        await using var scope = await AmbientAsync(cancellationToken);
        var changes = new List<RelayCloudChange>();
        await using var command = scope.Command(
            "SELECT \"Seq\", \"Table\", \"EntityKey\", \"Op\", \"Origin\", \"IdempotencyKey\", \"RecordedAtUtc\" "
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
                DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)));
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

    public async Task ApplyReturnAsync(
        Guid clinicId, RelayHandbackRequest request, RelayHandbackPlan plan, CancellationToken cancellationToken)
    {
        await using var scope = await AmbientAsync(cancellationToken);
        var tables = Plan;
        var byTable = plan.Apply
            .GroupBy(r => r.Table, StringComparer.Ordinal)
            .ToDictionary(g => tables.Find(g.Key) ?? throw new InvalidOperationException($"Table inconnue : {g.Key}"),
                g => g.ToList());

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
        foreach (var (table, rows) in upserted)
        {
            await EnsureNoneOutsideClinicAsync(scope, table, byTable[table].Where(r => r.Row is not null).Select(r => r.Key).ToList(),
                clinicId, cancellationToken);
        }

        var mergedUsers = await MergeSignInsAsync(scope, clinicId, request.SignIns, cancellationToken);
        var spentCodes = await MergeRecoveryCodesAsync(scope, clinicId, request.RecoveryCodesUsed, cancellationToken);

        // The cloud's log of what the return wrote, so the PC's copy follows (a dropped duplicate leaves it), and so a
        // later return knows these rows are the cabinet's own (Origin = Relay), never a conflict.
        var saveOf = request.Changes
            .GroupBy(c => new RelayRowKey(c.Table, c.Key))
            .ToDictionary(g => g.Key, g => g.MaxBy(c => c.Seq)!.IdempotencyKey);
        var log = plan.Apply
            .Select(r => (r.Table, r.Key, Op: r.Row is null ? ClinicChangeOp.Delete : ClinicChangeOp.Upsert,
                Save: saveOf.GetValueOrDefault(new RelayRowKey(r.Table, r.Key))))
            .Concat(plan.Dropped.Select(k => (k.Table, k.Key, Op: ClinicChangeOp.Delete, Save: saveOf.GetValueOrDefault(k))))
            .Concat(mergedUsers.Select(id => (Table: nameof(User), Key: id, Op: ClinicChangeOp.Upsert, Save: (string?)null)))
            .Concat(spentCodes.Select(id => (Table: nameof(UserRecoveryCode), Key: id, Op: ClinicChangeOp.Upsert, Save: (string?)null)))
            .OrderBy(c => c.Table, StringComparer.Ordinal).ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToList();
        if (log.Count == 0)
        {
            return;
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
