using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.UnitTests.Features.Backup;

/// <summary>
/// An object store that <b>keeps its bytes</b>, so a write path and a read path can be compared with each other
/// rather than each against a hand-written expectation — <c>FakeAccessLedger</c>'s reason, one feature over.
///
/// <para>It is what lets AC-5 be asserted as « the archive's bytes came back at the key the row already holds »
/// instead of « some method was called ».</para>
/// </summary>
internal sealed class FakeBlobStore : IFileStorage
{
    public Dictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);

    /// <summary>Keys whose read fails, so « a blob that cannot be read is a warning, not a failure » is reachable.</summary>
    public HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);

    /// <summary>Keys whose write fails — the restore's mirror of the case above.</summary>
    public HashSet<string> Unwritable { get; } = new(StringComparer.Ordinal);

    /// <summary>Every key handed to <see cref="RestoreAtKeyAsync"/>, in order, verbatim.</summary>
    public List<string> RestoredKeys { get; } = new();

    public void Put(string storageKey, string content) =>
        Blobs[storageKey] = Encoding.UTF8.GetBytes(content);

    public string TextAt(string storageKey) => Encoding.UTF8.GetString(Blobs[storageKey]);

    public Task<Stream> DownloadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (Unreadable.Contains(storageKey) || !Blobs.TryGetValue(storageKey, out var bytes))
        {
            throw new FileNotFoundException(storageKey);
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    public Task RestoreAtKeyAsync(
        Stream file, string contentType, string storageKey, CancellationToken cancellationToken = default)
    {
        if (Unwritable.Contains(storageKey))
        {
            throw new IOException(storageKey);
        }

        using var buffer = new MemoryStream();
        file.CopyTo(buffer);

        RestoredKeys.Add(storageKey);
        Blobs[storageKey] = buffer.ToArray();

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(Blobs.ContainsKey(storageKey));

    public Task<string> UploadAsync(
        Stream file, string contentType, Guid clinicId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The archive never mints a key.");

    public Task<string> UploadAsync(
        Stream file, string contentType, Guid clinicId, string relativePath,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The archive never mints a key.");

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A restore deletes nothing.");

    public Task ProbeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// The EF half of the archive, stubbed at its interface — the seam is JSON on both sides, which is exactly what
/// makes the packager and the restorer testable with no database.
///
/// <para>⚠️ It <b>throws</b> on a table it was not given, deliberately: a fake that quietly answered « rien » for
/// an unknown table would let a restorer walking the wrong entries pass by taking another path.</para>
/// </summary>
internal sealed class FakeArchiveStore : IClinicArchiveStore
{
    private readonly Dictionary<string, ClinicArchiveTableOutcome> _outcomes = new(StringComparer.Ordinal);

    public List<ClinicArchiveTableData> Tables { get; } = new();

    public List<string> StorageKeys { get; } = new();

    public List<string> Warnings { get; } = new();

    /// <summary>Tables this build claims to know. Defaults to whatever was seeded through <see cref="Table"/>.</summary>
    public HashSet<string> Restorable { get; } = new(StringComparer.Ordinal);

    /// <summary>Tables whose restore throws, so the « stopped part way » refusal is reachable with no database.</summary>
    public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Every call this fake received, in order — how « saved, then forgot » is asserted as a sequence. Settable so
    /// it can be shared with <see cref="CountingUnitOfWork"/>: detaching an <c>Added</c> row BEFORE its commit
    /// discards the insert in silence, so the ordering is the property, not the two counts.
    /// </summary>
    public List<string> Calls { get; init; } = new();

    public List<Guid> RestoredIntoClinics { get; } = new();

    /// <summary>
    /// Seeds one table. <paramref name="blobKeys"/> lands on the <b>outcome</b>, because that is where a real
    /// store now carries it: the keys a restore hands back are those of the rows it actually inserted, not every
    /// key the file names — which is what stops an archive writing bytes into another cabinet's prefix behind a
    /// row that was skipped.
    /// </summary>
    public FakeArchiveStore Table(
        string name, string json = "[]", int rows = 0,
        ClinicArchiveTableOutcome? outcome = null, params string[] blobKeys)
    {
        Tables.Add(new ClinicArchiveTableData(name, json, rows));
        Restorable.Add(name);
        _outcomes[name] = (outcome ?? ClinicArchiveTableOutcome.Empty) with { StorageKeys = blobKeys };

        return this;
    }

    public Task<ClinicArchiveExport> ExportAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"export:{clinicId}");

        return Task.FromResult(new ClinicArchiveExport(Tables, StorageKeys, Warnings));
    }

    public Task<ClinicArchiveTableOutcome> RestoreTableAsync(
        string table, Guid clinicId, string json, CancellationToken cancellationToken = default)
    {
        if (!_outcomes.TryGetValue(table, out var outcome))
        {
            throw new InvalidOperationException($"The restorer asked for an unplanned table: {table}");
        }

        Calls.Add($"restore:{table}");
        RestoredIntoClinics.Add(clinicId);

        if (Failing.Contains(table))
        {
            throw new InvalidOperationException($"insert failed on {table}");
        }

        return Task.FromResult(outcome);
    }

    public bool CanRestore(string table) => Restorable.Contains(table);

    public void ForgetRestoredRows() => Calls.Add("forget");
}

/// <summary>
/// The audit ledger, collecting rather than asserting: a restore's summary rows are the only trace children like
/// <c>Payment</c> leave, since the interceptor writes one row per aggregate <i>root</i> and those are not one.
/// </summary>
internal sealed class FakeAuditEntryRepository : IAuditEntryRepository
{
    public List<AuditEntry> Entries { get; } = new();

    public Task AddRangeAsync(
        IReadOnlyCollection<AuditEntry> entries, CancellationToken cancellationToken = default)
    {
        Entries.AddRange(entries);
        return Task.CompletedTask;
    }

    public Task<PagedResult<AuditEntry>> GetFilteredAsync(
        Guid clinicId, string? entityType = null, string? entityId = null, DateTime? from = null,
        DateTime? to = null, AuditAction? action = null, PageRequest? paging = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A restore never reads the ledger back.");

    public Task<IReadOnlyList<string>> GetRecordedEntityTypesAsync(
        Guid clinicId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A restore never reads the ledger back.");

    public Task<IReadOnlyList<ClinicActivityAuditRow>> GetActivityRowsAsync(
        Guid clinicId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A restore never reads the ledger back.");
}

/// <summary>
/// A unit of work that counts its commits. The restorer saves once per table and only where something was
/// staged, which is a property about <i>how many</i> saves happen and not about any one of them.
/// </summary>
internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    /// <summary>Shared with <see cref="FakeArchiveStore.Calls"/> when the ordering is what a test is asserting.</summary>
    public List<string> Calls { get; init; } = new();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        Calls.Add("save");

        return Task.FromResult(1);
    }

    public void SetExpectedVersion(object entity, uint expectedVersion) { }

    public void StopTracking(object entity) { }

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
