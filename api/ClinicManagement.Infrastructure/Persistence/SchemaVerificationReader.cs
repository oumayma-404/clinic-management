using ClinicManagement.Application.Common.Interfaces;
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

    public SchemaVerificationReader(ApplicationDbContext context)
    {
        _context = context;
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

        return new SchemaFacts(
            extensions, constraints, model, database, mappedDecimals, dataMigrations, auditLedger);
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

        // US-4's invariant, and the only one here with no application write path behind it: until the admin
        // surface lands, these columns are filled in by hand, so this query is the whole guard.
        var partialTtnIdentity = await ScalarOrNullAsync(connection, cancellationToken,
            requiredTable: "Clinics",
            requiredColumn: "TtnCertificateKey",
            sql: """
                SELECT COUNT(*)
                FROM "Clinics"
                WHERE ("TtnApiSecretEncrypted" IS NOT NULL AND "TtnUsername" IS NULL)
                   OR ("TtnCertificatePasswordEncrypted" IS NOT NULL AND "TtnCertificateKey" IS NULL)
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

        return new DataMigrationCounts(
            typePrefix, overlaps, legacyExpiry, legacyExpiryWithoutBatch, stockWithoutBatch,
            missingNormalized, patientsTotal, actScalarWithoutRow, categoryStillInDescription,
            unsetBackupSchedule, chequeDetailsOnNonCheque, attributableButUnattributed, pushClinicMismatch,
            partialTtnIdentity, signupOrphans);
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
