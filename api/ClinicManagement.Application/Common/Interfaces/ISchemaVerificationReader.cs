namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Reads the schema facts the <c>verify-schema</c> report asserts: what the <b>EF model</b> says should exist,
/// what the <b>database</b> actually has, and the row counts that prove a data migration finished its job.
///
/// Implemented in Infrastructure because it needs both sides — PostgreSQL's catalog (<c>pg_extension</c>,
/// <c>pg_constraint</c>, <c>pg_indexes</c>, <c>information_schema.columns</c>) and EF's model metadata. The
/// <c>verify-schema</c> verb builds its container from <c>AddInfrastructure</c> only — never
/// <c>AddApplication</c> — so no <c>ICurrentClinicProvider</c> is registered, the global clinic query filters
/// stay inactive, and the backfill counts span every clinic without <c>IgnoreQueryFilters()</c>.
///
/// This seam exists for the same reason <see cref="IMoneyReconciliationReader"/> does: the assertions live in
/// Application so they are unit-testable against a mocked reader (the UnitTests project references Application,
/// and nothing in it touches a database).
/// </summary>
public interface ISchemaVerificationReader
{
    Task<SchemaFacts> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Everything the schema report needs, read in one pass.</summary>
public sealed record SchemaFacts(
    IReadOnlyList<string> InstalledExtensions,
    IReadOnlyList<TableConstraintFact> Constraints,
    SchemaSide Model,
    SchemaSide Database,
    IReadOnlyList<MappedDecimalFact> MappedDecimals,
    DataMigrationCounts DataMigrations);

/// <summary>
/// One side of the comparison — what the model expects, or what the database has. Symmetrical on purpose: the
/// report diffs the two rather than checking either against a hand-maintained list, so a schema object added in
/// a configuration file is verified automatically. A hardcoded expectation list is the exact shape of bug this
/// feature's own plan flags three times (a "contract" test that never fails on a new area).
/// </summary>
public sealed record SchemaSide(
    IReadOnlyList<IndexFact> Indexes,
    IReadOnlyList<ForeignKeyFact> ForeignKeys,
    IReadOnlyList<DecimalColumnFact> DecimalColumns);

/// <summary>
/// One table constraint, with its kind and full definition. The definition is what lets the report assert an
/// exclusion constraint is <b>partial</b> — a non-partial one makes a cancelled slot permanently unbookable,
/// which is why AC-P1.16 required the predicate.
/// </summary>
public sealed record TableConstraintFact(string Table, string Name, char Kind, string Definition);

/// <summary>One index, identified by table + the ordered columns it covers (names differ between EF and PG).</summary>
public sealed record IndexFact(string Table, string Name, IReadOnlyList<string> Columns, bool IsUnique, string? Filter)
{
    /// <summary>Table + columns — the identity the two sides are matched on, since names can legitimately differ.</summary>
    public string Signature => $"{Table}({string.Join(", ", Columns)})";
}

/// <summary>One foreign key: which table's column(s) point at which table.</summary>
public sealed record ForeignKeyFact(string Table, IReadOnlyList<string> Columns, string ReferencedTable)
{
    public string Signature => $"{Table}({string.Join(", ", Columns)}) -> {ReferencedTable}";
}

/// <summary>A numeric column's precision/scale, on whichever side is being described.</summary>
public sealed record DecimalColumnFact(string Table, string Column, int? Precision, int? Scale)
{
    public string Signature => $"{Table}.{Column}";

    public string Rendered => $"({Precision?.ToString() ?? "?"},{Scale?.ToString() ?? "?"})";
}

/// <summary>A decimal property as the EF model maps it, with the store type it resolves to.</summary>
public sealed record MappedDecimalFact(string Entity, string Property, string Table, string Column, string StoreType);

/// <summary>
/// Row counts that prove each data migration in this feature actually finished — a schema change can be present
/// while its backfill silently covered nothing.
///
/// Each count is <b>nullable</b>: null means "the column or table this measures does not exist yet", which is
/// the honest answer before the part that introduces it has run. Reporting 0 there would claim a backfill
/// succeeded when it has not happened at all.
/// </summary>
public sealed record DataMigrationCounts(
    int? AppointmentsWithTypePrefixRemaining,
    int? OverlappingAppointmentPairs,
    int? StockItemsWithLegacyExpiry,
    int? StockItemsWithLegacyExpiryLackingBatch,
    int? PatientsMissingNormalizedName,
    int? PatientsTotal);
