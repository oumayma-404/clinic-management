using ClinicManagement.Application.Common;
using Microsoft.Extensions.Configuration;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Reads both sides of the schema comparison: what the EF <b>model</b> declares, and what the
/// <b>database</b> actually has (PostgreSQL's own catalog).
///
/// The cross-clinic row counts need no <c>IgnoreQueryFilters()</c>: the <c>verify-schema</c> console verb builds
/// its container from <c>AddInfrastructure</c> alone, so no <c>ICurrentClinicProvider</c> is registered, the
/// context's optional provider is null, and every global clinic filter is inactive.
///
/// Deliberately read-only — it never calls <c>SaveChanges</c> and stages no entity. The catalog queries go
/// through raw ADO rather than EF because they read <c>pg_*</c> views that are not in the model at all; every one
/// of them is a constant string with no interpolated input.
/// </summary>
public class SchemaVerificationReader : ISchemaVerificationReader
{
    private readonly ApplicationDbContext _context;
    private readonly IVendorMessagingAvailability _vendorMessaging;
    private readonly IConfiguration? _configuration;
    private readonly IDataProtectionProvider? _dataProtection;
    private readonly Security.IAuditChainKeyProvider? _auditChainKey;
    private readonly Security.IAuditChainSealStore? _sealStore;

    /// <param name="configuration">
    /// Read only for the internal root certificate's remaining life (FR-2.6) — a third "side" beside the model
    /// and the catalog. Optional so a caller that has none reports « not applicable » rather than failing to
    /// construct; the <c>verify-schema</c> verb always passes it.
    /// </param>
    /// <param name="dataProtection">
    /// The fourth side (FR-3.1): which generation of the key ring each stored secret is encrypted under. Optional
    /// for the same reason — and where it is absent the report says « not applicable » rather than « 0 left »,
    /// which would authorise deleting the old key files on the strength of a measurement nobody took.
    /// </param>
    /// <param name="auditChainKey">
    /// The fifth side (FR-4.1): the secret each audit entry's hash is keyed on. Optional for the same reason as
    /// the two above — and where it is absent the walk is « not applicable » rather than « intact », which would
    /// be a clean bill of health on a chain nobody checked.
    /// </param>
    public SchemaVerificationReader(
        ApplicationDbContext context,
        IVendorMessagingAvailability vendorMessaging,
        IConfiguration? configuration = null,
        IDataProtectionProvider? dataProtection = null,
        Security.IAuditChainKeyProvider? auditChainKey = null,
        Security.IAuditChainSealStore? sealStore = null)
    {
        _context = context;
        _vendorMessaging = vendorMessaging;
        _configuration = configuration;
        _dataProtection = dataProtection;
        _auditChainKey = auditChainKey;
        _sealStore = sealStore;
    }

    public async Task<SchemaFacts> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_context.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var extensions = await ReadExtensionsAsync(connection, cancellationToken);
        var constraints = await ReadConstraintsAsync(connection, cancellationToken);
        var database = new SchemaSide(
            await ReadDatabaseIndexesAsync(connection, cancellationToken),
            await ReadDatabaseForeignKeysAsync(connection, cancellationToken),
            await ReadDatabaseDecimalColumnsAsync(connection, cancellationToken));

        var model = ReadModelSide();
        var mappedDecimals = ReadMappedDecimals();
        var dataMigrations = await ReadDataMigrationCountsAsync(connection, cancellationToken);
        var auditLedger = await ReadAuditLedgerFactsAsync(connection, cancellationToken);
        var (subscriptionLedgers, coverKindPresent) =
            await ReadSubscriptionLedgersAsync(connection, cancellationToken);
        var messagingAllowances = await ReadMessagingAllowancesAsync(connection, cancellationToken);

        return new SchemaFacts(
            extensions, constraints, model, database, mappedDecimals, dataMigrations, auditLedger,
            subscriptionLedgers, coverKindPresent, messagingAllowances, ReadInternalCertificate(),
            await ReadSecretProtectionAsync(connection, cancellationToken),
            await ReadAuditChainAsync(connection, cancellationToken));
    }

    /// <summary>
    /// Walks every audit chain with the <b>real</b> <c>AuditChain.Walk</c> (FR-4.1) — never re-expressed in SQL,
    /// which is what stops the check and the thing it checks drifting apart.
    ///
    /// <para>⚠️ <b>Read chain by chain, ordered, and walked as it streams.</b> This is the only fact here bounded
    /// by a practice's whole history rather than by its cabinet or account count, so materialising the ledger
    /// would put a deployment's every save in memory at once. One ordered read plus a walk that keeps only the
    /// previous entry costs a constant.</para>
    ///
    /// <para>⚠️ Guarded on the column, like every count below it: against a database predating the migration the
    /// answer is « not applicable », not a walk of zero chains reporting everything intact.</para>
    /// </summary>
    private async Task<AuditChainFacts?> ReadAuditChainAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (_auditChainKey is null
            || !await ColumnExistsAsync(connection, "AuditEntries", "EntryHash", cancellationToken))
        {
            return null;
        }

        const string sql = """
            SELECT "Id", "ChainKey", "Sequence", "UserId", "EntityType", "EntityId", "Action",
                   "ChangedFields", "OccurredAt", "IsDeclaredGap", "PreviousHash", "EntryHash",
                   "ClinicId", "UserEmail"
            FROM "AuditEntries"
            ORDER BY "ChainKey", "Sequence"
            """;

        var chains = new List<AuditChainWalkResult>();
        var buffer = new List<AuditChainEntry>();
        Guid? currentChain = null;

        // ⚠️ The sealed tips, held OUTSIDE this database. Without them a chain whose newest entries were deleted
        // walks perfectly — no neighbour is missing — so `audit-chain-intact` reports it intact. This is the one
        // input that makes truncation visible at all, and an unreadable seal file THROWS rather than reading as
        // « nothing sealed »: falling back would silently restore that blind spot.
        var seals = _sealStore?.Read() ?? new Dictionary<Guid, Domain.Services.AuditChainSeal>();

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var chainKey = reader.GetGuid(1);

            if (currentChain is { } open && open != chainKey)
            {
                chains.Add(Domain.Services.AuditChain.Walk(
                    open, buffer, _auditChainKey.Key, seals.GetValueOrDefault(open)));
                buffer.Clear();
            }

            currentChain = chainKey;
            buffer.Add(new AuditChainEntry(
                reader.GetGuid(0),
                chainKey,
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetDateTime(8),
                reader.GetBoolean(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                // ⚠️ Both are part of the v2 canonical form. Omitting them here would not fail to compile and
                // would not fail a unit test — it would make `verify-schema` report EVERY chained row as
                // altered, on the one check whose whole value is being believed when it says so.
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                reader.IsDBNull(13) ? null : reader.GetString(13)));
        }

        if (currentChain is { } last)
        {
            chains.Add(Domain.Services.AuditChain.Walk(
                last, buffer, _auditChainKey.Key, seals.GetValueOrDefault(last)));
        }

        return new AuditChainFacts(chains);
    }

    /// <summary>
    /// FR-3.1's coverage figure: per family, how many stored ciphertexts are not yet under the key ring's
    /// current generation — the only thing that says <c>reprotect-secrets</c> finished.
    ///
    /// <para>⚠️ <b>The generation is learned from a live <c>Protect</c>, not from configuration</b>
    /// (<c>DataProtectionKeyGeneration</c>): what matters is the key this process would write with right now, and
    /// a settings-derived answer would disagree with it in exactly the situation the check exists for.</para>
    ///
    /// <para>⚠️ <b>Every family is guarded on its own column, exactly like the data-migration counts here.</b>
    /// The first version read through EF unconditionally and the whole verb died with
    /// <c>column c.GoogleRefreshTokenProtected does not exist</c> against any database predating the migration —
    /// which is precisely the « before » half of the before-and-after run this verb exists to support. A family
    /// whose column is absent is <b>omitted</b>, never reported as zero outstanding.</para>
    ///
    /// <para>⚠️ It reads the ciphertext of every protected row, which is why every family here is small and
    /// bounded by the number of cabinets or accounts — never by a practice's history.</para>
    /// </summary>
    private async Task<SecretProtectionFacts?> ReadSecretProtectionAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (_dataProtection is null)
        {
            return null;
        }

        Security.DataProtectionKeyGeneration.Generation generation;
        try
        {
            generation = Security.DataProtectionKeyGeneration.Current(
                _dataProtection.CreateProtector("ClinicManagement.KeyRingGeneration.Probe.v1"));
        }
        catch (Exception)
        {
            return null;
        }

        var families = new List<SecretFamilyFact>();
        foreach (var (table, column, sql) in SecretColumns)
        {
            var ciphertexts = await ReadCiphertextsAsync(connection, table, column, sql, cancellationToken);
            if (ciphertexts is null)
            {
                continue;
            }

            families.Add(new SecretFamilyFact(
                $"{table}.{column}", ciphertexts.Count, ciphertexts.Count(c => !generation.Covers(c))));
        }

        var certificates = _configuration is null
            ? null
            : TryResolveProtectingCertificates();

        return new SecretProtectionFacts(
            certificates?.IsConfigured ?? false,
            certificates?.Active is { } active
                ? (int)Math.Floor((active.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays)
                : null,
            families);
    }

    /// <summary>
    /// The six protected column families. The SQL is written out per family rather than composed from the table
    /// and column names, so this file's own rule still holds: every catalog and data query here is a
    /// <b>constant string with no interpolated input</b>.
    /// </summary>
    private static readonly (string Table, string Column, string Sql)[] SecretColumns =
    {
        ("ClinicReminderSettings", "SmsApiKeyEncrypted",
            """SELECT "SmsApiKeyEncrypted" FROM "ClinicReminderSettings" WHERE "SmsApiKeyEncrypted" IS NOT NULL"""),
        ("ClinicReminderSettings", "WhatsAppAccessTokenEncrypted",
            """SELECT "WhatsAppAccessTokenEncrypted" FROM "ClinicReminderSettings" WHERE "WhatsAppAccessTokenEncrypted" IS NOT NULL"""),
        ("ClinicReminderSettings", "SmtpPasswordEncrypted",
            """SELECT "SmtpPasswordEncrypted" FROM "ClinicReminderSettings" WHERE "SmtpPasswordEncrypted" IS NOT NULL"""),
        ("Users", "ProtectedTotpSecret",
            """SELECT "ProtectedTotpSecret" FROM "Users" WHERE "ProtectedTotpSecret" IS NOT NULL"""),
        ("PlatformAccounts", "ProtectedTotpSecret",
            """SELECT "ProtectedTotpSecret" FROM "PlatformAccounts" WHERE "ProtectedTotpSecret" IS NOT NULL"""),
        ("Clinics", "GoogleRefreshTokenProtected",
            """SELECT "GoogleRefreshTokenProtected" FROM "Clinics" WHERE "GoogleRefreshTokenProtected" IS NOT NULL"""),
    };

    /// <summary>Every non-null ciphertext of one column, or <c>null</c> where the column does not exist yet.</summary>
    private static async Task<List<string>?> ReadCiphertextsAsync(
        NpgsqlConnection connection,
        string table,
        string column,
        string sql,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(connection, table, column, cancellationToken))
        {
            return null;
        }

        var values = new List<string>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                values.Add(reader.GetString(0));
            }
        }

        return values;
    }

    /// <summary>
    /// The key ring's protecting certificate, or null where none is configured or it cannot be read. A refusal
    /// is <b>startup's</b> to raise, not this report's — reaching this verb means the deployment already passed
    /// that gate — so an exception here is swallowed into « pas de certificat » rather than failing the run.
    /// </summary>
    private Security.KeyRingProtectionCertificates.Resolution? TryResolveProtectingCertificates()
    {
        try
        {
            return Security.KeyRingProtectionCertificates.Resolve(_configuration!, DateTime.UtcNow);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The deployment's internal root certificate, or <c>null</c> where it configures none (FR-2.6).
    ///
    /// <para>The path is taken from the <b>database connection string's</b> <c>Root Certificate</c> first and
    /// from <c>MinIO:RootCertificate</c> second, because those are the two settings that actually name it — and
    /// asking them, rather than introducing a third key, is what stops this report describing a file no hop uses.
    /// The shipped compose files point both at the same <c>/certs/ca.crt</c>.</para>
    /// </summary>
    private InternalCertificateFact? ReadInternalCertificate()
    {
        if (_configuration is null)
        {
            return null;
        }

        var path = ResolveRootCertificatePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var inspection = Security.InternalCertificate.Inspect(path, DateTime.UtcNow);
        return new InternalCertificateFact(
            inspection.Path ?? path,
            inspection.DaysRemaining ?? 0,
            inspection.IsUsable,
            inspection.Detail);
    }

    private string? ResolveRootCertificatePath()
    {
        var connectionString = _context.Database.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                var fromConnection = new NpgsqlConnectionStringBuilder(connectionString).RootCertificate;
                if (!string.IsNullOrWhiteSpace(fromConnection))
                {
                    return fromConnection;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                // An unparseable connection string is TransportAssurance's refusal to word, not this report's:
                // fall through to the object store's key so the certificate line still says something.
            }
        }

        return _configuration?[Security.InternalCertificate.MinioRootCertificateKey];
    }

    // ------------------------------------------------------------------ the EF model side

    /// <summary>
    /// Projects the model's declared indexes, foreign keys and decimal columns. Reading these from the model —
    /// rather than a hand-maintained list — is what makes the report self-maintaining: an index added in a
    /// configuration file is verified without touching this class.
    /// </summary>
    private SchemaSide ReadModelSide()
    {
        var indexes = new List<IndexFact>();
        var foreignKeys = new List<ForeignKeyFact>();
        var decimals = new List<DecimalColumnFact>();

        foreach (var entity in _context.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (string.IsNullOrEmpty(table))
            {
                continue;
            }

            var storeObject = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier
                .Table(table, entity.GetSchema());

            foreach (var index in entity.GetIndexes())
            {
                indexes.Add(new IndexFact(
                    table,
                    index.GetDatabaseName() ?? string.Empty,
                    index.Properties.Select(p => p.GetColumnName(storeObject) ?? p.Name).ToList(),
                    index.IsUnique,
                    index.GetFilter()));
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                var principalTable = fk.PrincipalEntityType.GetTableName();
                if (string.IsNullOrEmpty(principalTable))
                {
                    continue;
                }

                // Owned types and table-splitting produce a "foreign key" from a table to ITSELF over its own
                // primary key — the identity link that keeps Patient.Address in the Patients row. PostgreSQL has
                // no such constraint, and reporting it as missing is a false positive (it produced 7 of them:
                // Patient's three owned value objects and ProcedureType's). A genuine self-reference such as
                // PatientFolder.ParentFolderId is NOT excluded, because its columns are not the primary key.
                if (fk.IsOwnership || IsTableSplittingIdentity(fk, entity, principalTable, table))
                {
                    continue;
                }

                foreignKeys.Add(new ForeignKeyFact(
                    table,
                    fk.Properties.Select(p => p.GetColumnName(storeObject) ?? p.Name).ToList(),
                    principalTable));
            }

            foreach (var property in entity.GetProperties())
            {
                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clrType != typeof(decimal))
                {
                    continue;
                }

                decimals.Add(new DecimalColumnFact(
                    table,
                    property.GetColumnName(storeObject) ?? property.Name,
                    property.GetPrecision(),
                    property.GetScale()));
            }
        }

        return new SchemaSide(indexes, foreignKeys, decimals);
    }

    /// <summary>
    /// True when this "foreign key" is really the identity link of table splitting: same table on both sides,
    /// over the declaring entity's own primary key. No such constraint exists in the database.
    /// </summary>
    private static bool IsTableSplittingIdentity(
        Microsoft.EntityFrameworkCore.Metadata.IForeignKey fk,
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entity,
        string principalTable,
        string declaringTable)
    {
        if (!string.Equals(principalTable, declaringTable, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var primaryKey = entity.FindPrimaryKey();
        if (primaryKey == null)
        {
            return false;
        }

        return fk.Properties.Count == primaryKey.Properties.Count
            && fk.Properties.All(p => primaryKey.Properties.Contains(p));
    }

    /// <summary>Every mapped decimal with the store type it resolves to — the model-side precision assertion.</summary>
    private IReadOnlyList<MappedDecimalFact> ReadMappedDecimals()
    {
        var mapped = new List<MappedDecimalFact>();

        foreach (var entity in _context.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (string.IsNullOrEmpty(table))
            {
                continue;
            }

            var storeObject = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier
                .Table(table, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clrType != typeof(decimal))
                {
                    continue;
                }

                mapped.Add(new MappedDecimalFact(
                    entity.ShortName(),
                    property.Name,
                    table,
                    property.GetColumnName(storeObject) ?? property.Name,
                    property.GetColumnType() ?? "(none)"));
            }
        }

        return mapped;
    }

    // ------------------------------------------------------------------ the database side

    private static async Task<IReadOnlyList<string>> ReadExtensionsAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<string>();
        await using var command = new NpgsqlCommand("SELECT extname FROM pg_extension", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<TableConstraintFact>> ReadConstraintsAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT rel.relname, con.conname, con.contype, pg_get_constraintdef(con.oid)
            FROM pg_constraint con
            JOIN pg_class rel ON rel.oid = con.conrelid
            JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
            WHERE nsp.nspname = 'public'
            """;

        var rows = new List<TableConstraintFact>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new TableConstraintFact(
                reader.GetString(0), reader.GetString(1), reader.GetChar(2), reader.GetString(3)));
        }

        return rows;
    }

    /// <summary>
    /// Indexes with their columns in order, straight from <c>pg_index</c> — not parsed out of the DDL text,
    /// because a parser over <c>indexdef</c> would silently mis-read an expression index.
    /// </summary>
    private static async Task<IReadOnlyList<IndexFact>> ReadDatabaseIndexesAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT rel.relname       AS table_name,
                   cls.relname       AS index_name,
                   idx.indisunique   AS is_unique,
                   pg_get_expr(idx.indpred, idx.indrelid) AS filter,
                   ARRAY(
                       SELECT pg_get_indexdef(idx.indexrelid, k + 1, true)
                       FROM generate_subscripts(idx.indkey, 1) AS k
                       ORDER BY k
                   ) AS columns
            FROM pg_index idx
            JOIN pg_class cls ON cls.oid = idx.indexrelid
            JOIN pg_class rel ON rel.oid = idx.indrelid
            JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
            WHERE nsp.nspname = 'public'
            """;

        var rows = new List<IndexFact>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var columns = ((string[])reader.GetValue(4))
                // pg_get_indexdef quotes identifiers; the model side does not, so normalise.
                .Select(c => c.Trim('"'))
                .ToList();

            rows.Add(new IndexFact(
                reader.GetString(0),
                reader.GetString(1),
                columns,
                reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<ForeignKeyFact>> ReadDatabaseForeignKeysAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT rel.relname AS table_name,
                   ref.relname AS referenced_table,
                   ARRAY(
                       SELECT att.attname
                       FROM unnest(con.conkey) WITH ORDINALITY AS u(attnum, ord)
                       JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = u.attnum
                       ORDER BY u.ord
                   ) AS columns
            FROM pg_constraint con
            JOIN pg_class rel ON rel.oid = con.conrelid
            JOIN pg_class ref ON ref.oid = con.confrelid
            JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
            WHERE nsp.nspname = 'public' AND con.contype = 'f'
            """;

        var rows = new List<ForeignKeyFact>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ForeignKeyFact(
                reader.GetString(0),
                ((string[])reader.GetValue(2)).ToList(),
                reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<DecimalColumnFact>> ReadDatabaseDecimalColumnsAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT table_name, column_name, numeric_precision, numeric_scale
            FROM information_schema.columns
            WHERE table_schema = 'public' AND data_type = 'numeric'
            """;

        var rows = new List<DecimalColumnFact>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new DecimalColumnFact(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3)));
        }

        return rows;
    }

    // ------------------------------------------------------------------ data-migration counts

    /// <summary>
    /// Counts that prove each backfill covered its rows. Every one is guarded on the table/column existing, so
    /// the verb runs cleanly on a database that predates the part which introduces it — reporting
    /// "not applicable" rather than a misleading 0.
    /// </summary>
    private static async Task<DataMigrationCounts> ReadDataMigrationCountsAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var typePrefix = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Appointments",
            requiredColumn: "Notes",
            sql: """SELECT COUNT(*) FROM "Appointments" WHERE "Notes" LIKE 'Type: %'""");

        // The same predicate the partial constraint uses: Cancelled = 5, NoShow = 6 are not busy slots, and a
        // NULL practitioner is a busy slot belonging to nobody, so there is no one to double-book.
        var overlaps = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Appointments",
            requiredColumn: "DoctorId",
            sql: """
                SELECT COUNT(*)
                FROM "Appointments" a
                JOIN "Appointments" b
                  ON a."DoctorId" = b."DoctorId"
                 AND a."Id" < b."Id"
                 AND a."AppointmentDateTime" < b."AppointmentDateTime" + (b."Duration" * interval '1 microsecond' / 10)
                 AND b."AppointmentDateTime" < a."AppointmentDateTime" + (a."Duration" * interval '1 microsecond' / 10)
                WHERE a."DoctorId" IS NOT NULL
                  AND a."Status" NOT IN (5, 6)
                  AND b."Status" NOT IN (5, 6)
                """);

        // Pre-migration only: once the batch migration runs it DROPS StockItems.ExpiryDate, so this question
        // is permanently unanswerable afterwards. Guarded on the column it reads, not on StockBatches --
        // guarding on the wrong table is what made this check throw 42703 the first time the migration applied.
        var legacyExpiry = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "StockItems",
            requiredColumn: "ExpiryDate",
            sql: """SELECT COUNT(*) FROM "StockItems" WHERE "ExpiryDate" IS NOT NULL""");

        var legacyExpiryWithoutBatch = await ColumnExistsAsync(connection, "StockBatches", "StockItemId", cancellationToken)
            ? await ScalarOrNullAsync(connection, cancellationToken,
                requiredTable: "StockItems",
                requiredColumn: "ExpiryDate",
                sql: """
                    SELECT COUNT(*)
                    FROM "StockItems" s
                    WHERE s."ExpiryDate" IS NOT NULL
                      AND NOT EXISTS (SELECT 1 FROM "StockBatches" b WHERE b."StockItemId" = s."Id")
                    """)
            : null;

        // Post-migration: the durable invariant FEFO depends on. An item holding stock with no lot to draw from
        // makes every consume report a full shortfall against stock that is physically on the shelf.
        var stockWithoutBatch = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "StockBatches",
            requiredColumn: "StockItemId",
            sql: """
                SELECT COUNT(*)
                FROM "StockItems" s
                WHERE s."CurrentStock" > 0
                  AND NOT EXISTS (SELECT 1 FROM "StockBatches" b WHERE b."StockItemId" = s."Id")
                """);

        var patientsTotal = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Patients",
            requiredColumn: "Id",
            sql: """SELECT COUNT(*) FROM "Patients" """);

        var missingNormalized = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Patients",
            requiredColumn: "NormalizedFullName",
            sql: """SELECT COUNT(*) FROM "Patients" WHERE "NormalizedFullName" IS NULL""");

        // Multi-act séances: the parent's ProcedureTypeId is a derived snapshot of the first AppointmentProcedures
        // row, so one without the other is drift the backfill exists to prevent. Guarded on the child table's
        // column rather than the parent's, since the parent scalar predates this migration by years.
        var actScalarWithoutRow = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "AppointmentProcedures",
            requiredColumn: "AppointmentId",
            sql: """
                SELECT COUNT(*)
                FROM "Appointments" a
                WHERE a."ProcedureTypeId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "AppointmentProcedures" p WHERE p."AppointmentId" = a."Id")
                """);

        // The category move. Guarded on `Category` (not `Description`, which predates it by years), so before the
        // migration runs this reads « not applicable » rather than counting rows nothing was going to touch.
        // The label list is inlined for the same reason it is inlined in the migration: this measures what THAT
        // migration was supposed to move, and must keep doing so after the canonical set grows.
        var categoryStillInDescription = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "ProcedureTypes",
            requiredColumn: "Category",
            sql: """
                SELECT COUNT(*)
                FROM "ProcedureTypes"
                WHERE TRIM("Description") IN (
                    'Consultation', 'Radiologie', 'Soins conservateurs', 'Endodontie', 'Parodontologie',
                    'Chirurgie/Extraction', 'Prothèse fixe', 'Prothèse amovible', 'Implantologie',
                    'Orthodontie', 'Esthétique', 'Pédodontie')
                """);

        // The stored-status twin. TreatmentPlanItem.Status is recomputed from the step rows on every write, so
        // the two agreeing is an invariant and not a backfill outcome — which is why the status arithmetic is
        // spelled out here rather than a row count being compared. The int literals are the enum's own stored
        // values (Planned = 0, Done = 1, InProgress = 2); the enum is append-only for exactly this reason.
        var planItemStatusDisagrees = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "TreatmentPlanItemSteps",
            requiredColumn: "SequenceNumber",
            sql: """
                SELECT COUNT(*)
                FROM "TreatmentPlanItems" i
                JOIN (
                    SELECT "TreatmentPlanItemId" AS iid,
                           COUNT(*) AS total,
                           COUNT("DoneDate") AS done
                    FROM "TreatmentPlanItemSteps"
                    GROUP BY "TreatmentPlanItemId"
                ) s ON s.iid = i."Id"
                WHERE i."Status" <> CASE
                        WHEN s.done = 0 THEN 0
                        WHEN s.done < s.total THEN 2
                        ELSE 1
                    END
                """);

        // Step ranks must be dense 0..n-1: every reader treats them as positional (« étape 2 sur 3 » is the
        // rank, and « la prochaine » is the lowest un-done one), so a duplicate makes "next" ambiguous between
        // two steps and a gap misprints the count. Nothing in the schema can say this.
        var planItemStepSequenceNotDense = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "TreatmentPlanItemSteps",
            requiredColumn: "SequenceNumber",
            sql: """
                SELECT COUNT(*)
                FROM (
                    SELECT "TreatmentPlanItemId"
                    FROM "TreatmentPlanItemSteps"
                    GROUP BY "TreatmentPlanItemId"
                    HAVING MIN("SequenceNumber") <> 0
                        OR MAX("SequenceNumber") <> COUNT(*) - 1
                        OR COUNT(DISTINCT "SequenceNumber") <> COUNT(*)
                ) d
                """);

        // L4a's backfill. Guarded on `BackupRetentionCount`, so before the migration runs this reads « not
        // applicable » rather than counting rows nothing was going to touch. It measures the *outcome* rather than
        // the row count, which is what makes it durable: whatever the migration did, no clinic may be left with a
        // retention or staleness threshold of zero.
        var unsetBackupSchedule = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Clinics",
            requiredColumn: "BackupRetentionCount",
            sql: """
                SELECT COUNT(*)
                FROM "Clinics"
                WHERE "BackupRetentionCount" <= 0 OR "BackupStaleAfterHours" <= 0
                """);

        // L8's invariant, over BOTH ledgers in one figure — a single number is the right shape because the answer
        // that matters is « did any write path bypass ChequeDetails.For? », not which table it happened in.
        //
        // ⚠️ `"Method" <> 1` is PaymentMethod.Cheque's ordinal, and it is spelled out here on purpose: this is the
        // one place the check has to reach *into* the stored representation, because the whole point is to catch a
        // row the domain never validated. Every other reference to that value in the solution goes through the
        // enum. If PaymentMethod is ever reordered, this line — and the persisted data — both need revisiting.
        var chequeDetailsOnNonCheque = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Payments",
            requiredColumn: "ChequeNumber",
            sql: """
                SELECT
                    (SELECT COUNT(*) FROM "Payments"
                     WHERE "Method" <> 1
                       AND ("ChequeNumber" IS NOT NULL OR "ChequeBankName" IS NOT NULL OR "ChequeDueDate" IS NOT NULL))
                  + (SELECT COUNT(*) FROM "InstallmentPayments"
                     WHERE "Method" <> 1
                       AND ("ChequeNumber" IS NOT NULL OR "ChequeBankName" IS NOT NULL OR "ChequeDueDate" IS NOT NULL))
                """);

        // Group B's invariant, over BOTH ledgers in one figure for the same reason as its sibling above: the
        // answer that matters is « did any write path bypass ChequeBankedStamp.For? », not which table it was in.
        //
        // ⚠️ Guarded on `ChequeBankedOn`, not on `ChequeNumber`: the two migrations are separate, so keying this on
        // L8's column would report a reassuring 0 on a database that has L8 and not Group B — the exact
        // « not applicable vs. 0 » confusion the guard exists to prevent.
        //
        // ⚠️ `"Method" <> 1` is PaymentMethod.Cheque's ordinal, spelled out here for the reason the sibling states:
        // this check has to reach into the stored representation, because its whole point is a row the domain
        // never validated.
        var bankedStampOnNonCheque = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Payments",
            requiredColumn: "ChequeBankedOn",
            sql: """
                SELECT
                    (SELECT COUNT(*) FROM "Payments"
                     WHERE "Method" <> 1
                       AND ("ChequeBankedOn" IS NOT NULL OR "ChequeBankedByUserId" IS NOT NULL
                            OR "ChequeBankedByName" IS NOT NULL))
                  + (SELECT COUNT(*) FROM "InstallmentPayments"
                     WHERE "Method" <> 1
                       AND ("ChequeBankedOn" IS NOT NULL OR "ChequeBankedByUserId" IS NOT NULL
                            OR "ChequeBankedByName" IS NOT NULL))
                """);

        // L9's backfill, measured as an OUTCOME rather than as a row count: whatever the migration did, no invoice
        // and no fiche whose visit names a practitioner may be left unattributed. Guarded on `Invoices.DoctorId`, so
        // before the migration this reads « not applicable » rather than a reassuring 0.
        var attributableButUnattributed = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Invoices",
            requiredColumn: "DoctorId",
            sql: """
                SELECT
                    (SELECT COUNT(*) FROM "Invoices" i
                     JOIN "Appointments" a ON a."Id" = i."AppointmentId"
                     WHERE i."DoctorId" IS NULL AND a."DoctorId" IS NOT NULL)
                  + (SELECT COUNT(*) FROM "DentalRecords" r
                     JOIN "Appointments" a ON a."Id" = r."AppointmentId"
                     WHERE r."DoctorId" IS NULL AND a."DoctorId" IS NOT NULL)
                """);

        // The fiche→visit backfill, measured as an OUTCOME rather than as a row count: whatever the migration did,
        // no fiche whose day holds exactly one candidate visit may still be unlinked.
        //
        // ⚠️ `AT TIME ZONE 'Africa/Tunis'` on BOTH sides, matching the migration: comparing raw UTC dates would
        // file an evening séance against the following day and report clean while every such row stayed unlinked.
        // 5 = Cancelled, 6 = NoShow — the enum's own ordinals (HasConversion<int>()), stated because SQL has no
        // access to the type, and excluded here for the reason the resolver excludes them: neither can be the
        // séance a fiche documents.
        var fichesResolvableStillUnlinked = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "DentalRecords",
            requiredColumn: "AppointmentId",
            sql: """
                SELECT COUNT(*)
                FROM "DentalRecords" r
                WHERE r."AppointmentId" IS NULL
                  AND (
                        SELECT COUNT(*)
                        FROM "Appointments" a
                        WHERE a."PatientId" = r."PatientId"
                          AND a."ClinicId"  = r."ClinicId"
                          AND a."Status" NOT IN (5, 6)
                          AND (a."AppointmentDateTime" AT TIME ZONE 'Africa/Tunis')::date
                            = (r."InterventionDate"    AT TIME ZONE 'Africa/Tunis')::date
                      ) = 1
                """);

        // Part 6's invariant. A JOIN rather than a column comparison because the two clinic ids live in different
        // tables — which is exactly why no constraint can state it, and why it needs a line here.
        var pushClinicMismatch = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "PushDeliveries",
            requiredColumn: "DeviceRegistrationId",
            sql: """
                SELECT COUNT(*)
                FROM "PushDeliveries" p
                JOIN "DeviceRegistrations" d ON d."Id" = p."DeviceRegistrationId"
                WHERE p."ClinicId" <> d."ClinicId"
                """);

        // clinic-self-signup. Two shapes in one figure, because the answer that matters is « is anything stuck
        // in this table? » rather than which way. The consumed cut-off is 30 days, matching the handler's own
        // retention, plus a day's slack so a purge that ran this morning does not read as drift this afternoon.
        var signupOrphans = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "ClinicSignups",
            requiredColumn: "TokenHash",
            sql: """
                SELECT
                    (SELECT COUNT(*) FROM "ClinicSignups" s
                     WHERE s."ConsumedAtUtc" IS NULL
                       AND EXISTS (SELECT 1 FROM "Users" u
                                   WHERE LOWER(u."Email") = s."Email" AND u."PasswordHash" IS NOT NULL))
                  + (SELECT COUNT(*) FROM "ClinicSignups"
                     WHERE "ConsumedAtUtc" IS NOT NULL
                       AND "ConsumedAtUtc" < NOW() - INTERVAL '31 days')
                """);

        // clinic-recovery-points. Only the invariant MarkSucceeded enforces — a success names where it landed. Whether
        // that object still EXISTS is a question about the object store, which this reader cannot ask and which the
        // restore answers with its own named refusal.
        var recoveryPointsWithNoKey = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "ClinicRecoveryPoints",
            requiredColumn: "StorageKey",
            // ⚠️ The ordinal is INTERPOLATED FROM THE ENUM, never typed as a literal: `Succeeded` is 2 and `Running`
            // is 1, and a hand-typed 1 here would count crashed rows and report zero for ever — a check that has
            // silently stopped checking. Same reasoning as the cheque indexes' refusal to filter on `Method = 1`.
            sql: $"""
                SELECT COUNT(*) FROM "ClinicRecoveryPoints"
                WHERE "Outcome" = {(int)BackupOutcome.Succeeded}
                  AND ("StorageKey" IS NULL OR TRIM("StorageKey") = '')
                """);

        // FR-1.6's third check — Info only, and honest about what it cannot see (see the DTO's own note).
        // `NOW()` is the database's clock; the difference against ours is the whole reading.
        double? clockOffsetSeconds = null;
        try
        {
            await using var clockCommand = connection.CreateCommand();
            clockCommand.CommandText = "SELECT EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'UTC'))";
            var dbEpoch = await clockCommand.ExecuteScalarAsync(cancellationToken);
            if (dbEpoch is not null && dbEpoch is not DBNull)
            {
                var dbSeconds = Convert.ToDouble(dbEpoch, System.Globalization.CultureInfo.InvariantCulture);
                var appSeconds = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
                clockOffsetSeconds = Math.Round(appSeconds - dbSeconds, 3);
            }
        }
        catch
        {
            // A reading we could not take is reported as « not applicable », never as zero drift.
        }

        // hosted-security-hardening FR-1.1. Deliberately NOT « admins without a factor »: that is a state the
        // product allows (they are refused at the ladder and sent to enrol). What must never exist is one still
        // holding a LIVE session — which is what the per-request check in LocalAuthEnforcementMiddleware
        // prevents, and the only thing here able to notice if it stops.
        var adminsWithoutFactorLive = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "SessionFamilies",
            requiredColumn: "CurrentCredentialHash",
            sql: """
                SELECT COUNT(DISTINCT u."Id")
                FROM "Users" u
                JOIN "SessionFamilies" f ON f."UserId" = u."Id"
                WHERE LOWER(u."Role") = 'admin'
                  AND u."TotpEnrolledAt" IS NULL
                  AND u."IsActive"
                  AND f."EndedAtUtc" IS NULL
                  AND f."ExpiresAtUtc" > NOW()
                """);

        var sessionFamilyOrphans = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "SessionFamilies",
            requiredColumn: "UserId",
            sql: """
                SELECT COUNT(*)
                FROM "SessionFamilies" f
                WHERE NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."Id" = f."UserId")
                """);

        // The seven clinical children of Patients, each checked against the patient it hangs off. One figure over
        // seven UNIONed counts rather than seven findings: the operator's question is « does any clinical row name
        // the wrong clinic? », and seven lines of zeros answer it worse than one. `PatientFiles` is the required
        // column probe for all seven because they are added by a single migration — no state exists in which one
        // of the columns is present and another is not.
        var clinicalChildrenWrongClinic = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "PatientFiles",
            requiredColumn: "ClinicId",
            sql: """
                SELECT
                    (SELECT COUNT(*) FROM "ToothStates" c
                     JOIN "Patients" p ON p."Id" = c."PatientId" WHERE c."ClinicId" <> p."ClinicId")
                  + (SELECT COUNT(*) FROM "PatientMedicalHistories" c
                     JOIN "Patients" p ON p."Id" = c."PatientId" WHERE c."ClinicId" <> p."ClinicId")
                  + (SELECT COUNT(*) FROM "PatientFolders" c
                     JOIN "Patients" p ON p."Id" = c."PatientId" WHERE c."ClinicId" <> p."ClinicId")
                  + (SELECT COUNT(*) FROM "PatientFiles" c
                     JOIN "Patients" p ON p."Id" = c."PatientId" WHERE c."ClinicId" <> p."ClinicId")
                  + (SELECT COUNT(*) FROM "PatientFamilyHistories" c
                     JOIN "Patients" p ON p."Id" = c."PatientId" WHERE c."ClinicId" <> p."ClinicId")
                  + (SELECT COUNT(*) FROM "MedicalDocuments" c
                     JOIN "Patients" p ON p."Id" = c."PatientId" WHERE c."ClinicId" <> p."ClinicId")
                  + (SELECT COUNT(*) FROM "DentalRecords" c
                     JOIN "Patients" p ON p."Id" = c."PatientId" WHERE c."ClinicId" <> p."ClinicId")
                """);

        // platform-console Part 1. The two TOTP columns are halves of one fact and no constraint says so; an
        // account in the broken half cannot sign in and reports only « code invalide ».
        var enrolledWithoutSecret = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "PlatformAccounts",
            requiredColumn: "TotpEnrolledAt",
            sql: """
                SELECT COUNT(*) FROM "PlatformAccounts"
                WHERE "TotpEnrolledAt" IS NOT NULL
                  AND ("ProtectedTotpSecret" IS NULL OR "ProtectedTotpSecret" = '')
                """);

        // platform-console Part 2. A cabinet the nightly pass has never reached — which the per-cabinet
        // try/catch makes survivable and therefore silent.
        var clinicsWithoutSnapshot = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "ClinicActivitySnapshots",
            requiredColumn: "ClinicId",
            sql: """
                SELECT COUNT(*) FROM "Clinics" c
                WHERE NOT EXISTS (SELECT 1 FROM "ClinicActivitySnapshots" s WHERE s."ClinicId" = c."Id")
                """);

        // The relations one Restate call makes true by construction. Any of them false means a second writer.
        var incoherentSnapshots = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "ClinicActivitySnapshots",
            requiredColumn: "Writes30d",
            sql: """
                SELECT COUNT(*) FROM "ClinicActivitySnapshots"
                WHERE "Writes7d" > "Writes30d"
                   OR "ActiveDays30d" > 30
                   OR ("Writes30d" = 0 AND "ActiveDays30d" > 0)
                   OR ("Writes30d" > 0 AND "LastWriteAt" IS NULL)
                """);

        // clinic-subscription FR-13. A flat count over EVERY cabinet — never one qualified by which door created
        // it, and never a list of known doors, because the failure this exists to catch is a *third* door added
        // later. Guarded on the entitlement table, so a pre-migration run reads « not applicable » rather than a
        // reassuring 0.
        var clinicsWithoutEntitlement = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "ClinicSubscriptions",
            requiredColumn: "ClinicId",
            sql: """
                SELECT COUNT(*)
                FROM "Clinics" c
                WHERE NOT EXISTS (
                    SELECT 1 FROM "ClinicSubscriptions" s WHERE s."ClinicId" = c."Id")
                """);

        // AC-6.2/AC-6.4, reported rather than asserted. ⚠️ `"Kind" = 3` is SubscriptionPeriodKind.Grandfathered's
        // ordinal, spelled out for the reason `"Method" <> 1` above is: this reaches into the stored representation
        // on purpose, because it is counting what a *migration* wrote, and the migration could not name the enum
        // either. Both spell the same constant; if the enum is ever reordered, this line and the data both need
        // revisiting.
        var grandfatheredEntries = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "SubscriptionPeriods",
            requiredColumn: "Kind",
            sql: """SELECT COUNT(*) FROM "SubscriptionPeriods" WHERE "Kind" = 3""");

        // FR-3.4. Guarded on the PROTECTED column, not the plaintext one: the plaintext column has existed since
        // the per-clinic Google connection shipped, so guarding on it would report a reassuring count of
        // « still plaintext » rows on a database where the feature has not been deployed at all.
        var plaintextGoogleTokens = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Clinics",
            requiredColumn: "GoogleRefreshTokenProtected",
            sql: """SELECT COUNT(*) FROM "Clinics" WHERE "GoogleRefreshToken" IS NOT NULL""");

        // stock-fournisseurs AC-8. Guarded on the LabWorkOrders column rather than on the Suppliers table, so a
        // database where the table exists but the link column does not reads « not applicable » instead of a
        // reassuring 0. Folded on lower(btrim(…)) — the same rule the migration grouped by, so this measures the
        // backfill's outcome rather than restating its SQL.
        var labOrdersResolvableStillUnlinked = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "LabWorkOrders",
            requiredColumn: "SupplierId",
            sql: """
                SELECT COUNT(*)
                FROM "LabWorkOrders" o
                WHERE o."SupplierId" IS NULL
                  AND o."Prosthetist" IS NOT NULL
                  AND btrim(o."Prosthetist") <> ''
                  AND EXISTS (
                      SELECT 1 FROM "Suppliers" s
                      WHERE s."ClinicId" = o."ClinicId"
                        AND lower(btrim(s."Name")) = lower(btrim(o."Prosthetist")))
                """);

        var suppliersTotal = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Suppliers",
            requiredColumn: "Name",
            sql: """SELECT COUNT(*) FROM "Suppliers" """);

        // calendar-import-revert AC-19. ⚠️ RECOVERABLE misses only: a placeholder patient with no run, and the
        // appointments of one. Counting « has a Google event id and no practitioner » would sweep in every
        // ordinary booking made without a doctor and pushed to Google, and this check would be red for ever on a
        // cabinet with nothing wrong — which is how a gate teaches an operator to ignore its exit code.
        var calendarImportRowsWithoutARun = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Patients",
            requiredColumn: "CalendarImportRunId",
            sql: """
                SELECT
                    (SELECT COUNT(*) FROM "Patients" p
                     WHERE p."CalendarImportPendingReviewSince" IS NOT NULL
                       AND p."CalendarImportRunId" IS NULL)
                  + (SELECT COUNT(*) FROM "Appointments" a
                     WHERE a."CalendarImportRunId" IS NULL
                       AND a."PatientId" IN (
                           SELECT p2."Id" FROM "Patients" p2
                           WHERE p2."CalendarImportPendingReviewSince" IS NOT NULL))
                """);

        var calendarImportRunsTotal = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "CalendarImportRuns",
            requiredColumn: "StartedAtUtc",
            sql: """SELECT COUNT(*) FROM "CalendarImportRuns" """);

        return new DataMigrationCounts(
            typePrefix, overlaps, legacyExpiry, legacyExpiryWithoutBatch, stockWithoutBatch,
            missingNormalized, patientsTotal, actScalarWithoutRow, categoryStillInDescription,
            unsetBackupSchedule, chequeDetailsOnNonCheque, bankedStampOnNonCheque,
            attributableButUnattributed, fichesResolvableStillUnlinked, pushClinicMismatch,
            signupOrphans, clinicalChildrenWrongClinic,
            enrolledWithoutSecret, clinicsWithoutSnapshot, incoherentSnapshots,
            clinicsWithoutEntitlement, grandfatheredEntries,
            // NAMED, not positional — this record's own test file records how a merge that appends
            // positionally lands a zero in the wrong slot with every assertion still passing.
            AdminsWithoutFactorHoldingLiveSession: adminsWithoutFactorLive,
            SessionFamilyOrphans: sessionFamilyOrphans,
            AppToDatabaseClockOffsetSeconds: clockOffsetSeconds,
            ClinicsWithPlaintextGoogleToken: plaintextGoogleTokens,
            RecoveryPointsClaimingSuccessWithNoKey: recoveryPointsWithNoKey,
            LabOrdersResolvableToASupplierStillUnlinked: labOrdersResolvableStillUnlinked,
            SuppliersTotal: suppliersTotal,
            CalendarImportRowsWithoutARun: calendarImportRowsWithoutARun,
            CalendarImportRunsTotal: calendarImportRunsTotal,
            PlanItemsWithStatusDisagreeingWithSteps: planItemStatusDisagrees,
            PlanItemsWithNonDenseStepSequence: planItemStepSequenceNotDense);
    }

    /// <summary>
    /// Every cabinet's stored <c>EndsOn</c> beside its whole ledger, so the service can fold it with the <b>real</b>
    /// <c>SubscriptionLedger</c> instead of a SQL re-implementation of the exclusive-cursor arithmetic (FR-9, R-6).
    ///
    /// <para>Returns <c>null</c> before the tables exist, which the service reports as « not applicable » rather
    /// than as a clean fold of nothing.</para>
    ///
    /// <para>Ordered <c>RecordedAtUtc</c> then <c>Id</c> — the same order <c>ClinicSubscriptionRepository</c> reads
    /// in, and which <c>SubscriptionLedger</c> re-applies anyway, so this side cannot silently become the one the
    /// answer depends on.</para>
    /// </summary>
    private static async Task<(IReadOnlyList<ClinicSubscriptionLedgerFact>? Ledgers, bool CoverKindPresent)>
        ReadSubscriptionLedgersAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(connection, "ClinicSubscriptions", "EndsOn", cancellationToken)
            || !await ColumnExistsAsync(connection, "SubscriptionPeriods", "RecordedOnClinicDay", cancellationToken))
        {
            return (null, false);
        }

        var stored = new Dictionary<Guid, (DateTime? EndsOn, SubscriptionPeriodKind? CoverKind)>();

        // The cover-kind column arrives with `platform-console` Part 4, so it is projected only where it exists —
        // and the caller is told which, because a null there is also a real value (« every entry cancelled »).
        var coverKindPresent =
            await ColumnExistsAsync(connection, "ClinicSubscriptions", "LatestCoverKind", cancellationToken);

        var subscriptionsSql = coverKindPresent
            ? """SELECT "ClinicId", "EndsOn", "LatestCoverKind" FROM "ClinicSubscriptions" """
            : """SELECT "ClinicId", "EndsOn", NULL::int FROM "ClinicSubscriptions" """;

        await using (var command = new NpgsqlCommand(subscriptionsSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                stored[reader.GetGuid(0)] = (
                    reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                    reader.IsDBNull(2) ? null : (SubscriptionPeriodKind)reader.GetInt32(2));
            }
        }

        var entriesByClinic = stored.Keys.ToDictionary(id => id, _ => new List<SubscriptionLedgerEntry>());

        const string entriesSql = """
            SELECT "Id", "ClinicId", "RecordedOnClinicDay", "RecordedAtUtc", "DurationMonths", "DurationDays",
                   "ExplicitEndsOn", "IsCancelled", "Kind"
            FROM "SubscriptionPeriods"
            ORDER BY "ClinicId", "RecordedAtUtc", "Id"
            """;
        await using (var command = new NpgsqlCommand(entriesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var clinicId = reader.GetGuid(1);
                if (!entriesByClinic.TryGetValue(clinicId, out var entries))
                {
                    // A ledger entry whose cabinet has no entitlement row. Not silently dropped from the world:
                    // `every-clinic-has-an-entitlement` is the check that reports it, and folding it here would
                    // need an entitlement to compare against.
                    continue;
                }

                entries.Add(new SubscriptionLedgerEntry(
                    reader.GetGuid(0),
                    reader.GetDateTime(2),
                    reader.GetDateTime(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    reader.GetBoolean(7),
                    (SubscriptionPeriodKind)reader.GetInt32(8)));
            }
        }

        return (
            stored
                .Select(pair => new ClinicSubscriptionLedgerFact(
                    pair.Key, pair.Value.EndsOn, pair.Value.CoverKind, entriesByClinic[pair.Key]))
                .ToList(),
            coverKindPresent);
    }

    /// <summary>
    /// Every cabinet beside its WhatsApp reminder allocation ledger and its counting rows, so the service can fold
    /// with the <b>real</b> <c>MessagingAllowanceLedger</c> rather than a SQL re-implementation of it (FR-2, R-6).
    ///
    /// <para>Driven from <c>Clinics</c> and not from either messaging table, deliberately: a cabinet with no
    /// counting row at all is exactly what <c>messaging-month-covers-every-clinic</c> exists to report, and keying
    /// the projection off the rows would make FR-3's failure the one state it cannot see.</para>
    ///
    /// <para>Ordered <c>RecordedAtUtc</c> then <c>Id</c> — the order the repository reads in, and which the fold
    /// re-applies anyway, so this side cannot silently become the one the answer depends on.</para>
    /// </summary>
    private async Task<MessagingAllowanceFacts?> ReadMessagingAllowancesAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(connection, "MessagingAllowanceEntries", "EffectiveMonth", cancellationToken)
            || !await ColumnExistsAsync(connection, "ClinicMessagingMonths", "MonthKey", cancellationToken))
        {
            return null;
        }

        var entriesByClinic = new Dictionary<Guid, List<MessagingAllowanceLedgerEntry>>();
        var monthsByClinic = new Dictionary<Guid, List<StoredMessagingMonth>>();
        var cabinets = new List<Guid>();

        await using (var command = new NpgsqlCommand("""SELECT "Id" FROM "Clinics" """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var clinicId = reader.GetGuid(0);
                cabinets.Add(clinicId);
                entriesByClinic[clinicId] = new List<MessagingAllowanceLedgerEntry>();
                monthsByClinic[clinicId] = new List<StoredMessagingMonth>();
            }
        }

        const string entriesSql = """
            SELECT "Id", "ClinicId", "Kind", "Messages", "EffectiveMonth", "RecordedAtUtc", "IsCancelled"
            FROM "MessagingAllowanceEntries"
            ORDER BY "ClinicId", "RecordedAtUtc", "Id"
            """;
        await using (var command = new NpgsqlCommand(entriesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                // An entry naming a cabinet that no longer exists cannot be folded against anything; the FK makes it
                // unreachable, and dropping it silently here beats inventing a cabinet for it.
                if (entriesByClinic.TryGetValue(reader.GetGuid(1), out var entries))
                {
                    entries.Add(new MessagingAllowanceLedgerEntry(
                        reader.GetGuid(0),
                        (MessagingAllowanceKind)reader.GetInt32(2),
                        reader.GetInt32(3),
                        reader.GetString(4),
                        reader.GetDateTime(5),
                        reader.GetBoolean(6)));
                }
            }
        }

        const string monthsSql = """
            SELECT "ClinicId", "MonthKey", "AllowanceMessages", "ConsumedMessages"
            FROM "ClinicMessagingMonths"
            ORDER BY "ClinicId", "MonthKey"
            """;
        await using (var command = new NpgsqlCommand(monthsSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (monthsByClinic.TryGetValue(reader.GetGuid(0), out var months))
                {
                    months.Add(new StoredMessagingMonth(
                        reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
                }
            }
        }

        return new MessagingAllowanceFacts(
            ClinicClock.CurrentMonthKey(),
            _vendorMessaging.SellsVendorMessaging,
            cabinets
                .Select(id => new ClinicMessagingLedgerFact(id, entriesByClinic[id], monthsByClinic[id]))
                .ToList());
    }

    /// <summary>
    /// Runs a count only when the table/column it depends on exists; otherwise returns null so the service can
    /// report "not applicable". Guarding here rather than swallowing an exception keeps a genuine SQL error
    /// visible instead of silently reading as "nothing to do".
    /// </summary>
    private static async Task<int?> ScalarOrNullAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken,
        string requiredTable,
        string requiredColumn,
        string sql)
    {
        if (!await ColumnExistsAsync(connection, requiredTable, requiredColumn, cancellationToken))
        {
            return null;
        }

        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    /// <summary>
    /// The audit ledger's one model-inexpressible fact: is <c>ClinicId</c> still nullable? Read from
    /// <c>information_schema</c> rather than from the model, because the drift this catches is a database that
    /// disagrees with the model — comparing the model to itself would report success either way.
    /// </summary>
    private static async Task<AuditLedgerFacts> ReadAuditLedgerFactsAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'AuditEntries' AND column_name = 'ClinicId'
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value is null or DBNull
            ? new AuditLedgerFacts(TableExists: false, ClinicIdIsNullable: null)
            : new AuditLedgerFacts(
                TableExists: true,
                ClinicIdIsNullable: string.Equals(value.ToString(), "YES", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> ColumnExistsAsync(
        NpgsqlConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @table AND column_name = @column
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null && Convert.ToInt32(value) > 0;
    }
}
