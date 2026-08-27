using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Backup;

/// <summary>
/// A restore that <b>reports</b> N rows restored must <b>persist</b> N rows
/// (<c>hosted-security-hardening</c> D.0 — the plan's Part 0).
///
/// <para><b>Why this test and not a reading of the code.</b> The defect it guards against was one
/// <c>ForgetRestoredRows()</c> — <c>ChangeTracker.Clear()</c> — placed <i>before</i> the save instead of after it:
/// the staged inserts were discarded, the operation reported every row as restored, and the database received
/// nothing. Nothing else could see it. The report was truthful about what it had been asked to do, the schema was
/// untouched so <c>verify-schema</c> read clean, and no exception was thrown anywhere.</para>
///
/// <para>⚠️ <b>It therefore asserts against what reached the store, never against
/// <c>outcome.Restored</c></b> — which is exactly what the defect left correct while the data vanished. The fake
/// below models the one thing that matters: staged rows survive to a save, and forgetting drops whatever has not
/// been saved yet.</para>
/// </summary>
public class ClinicArchiveRestorerTests
{
    private static readonly Guid ClinicId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task A_Restore_Reporting_Rows_Restored_Persists_Them()
    {
        var store = new StagingArchiveStore(("Patient", 3), ("Appointment", 2));
        var (zip, manifest) = ArchiveOf("Patient", "Appointment");

        var result = await RunAsync(zip, manifest, store);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Restored.Values.Sum());

        // The assertion the defect survives: not what was reported, but what the store actually kept.
        Assert.Equal(5, store.Persisted);
        Assert.Equal(0, store.Discarded);
    }

    /// <summary>
    /// Every table's rows reach the database, not only the last one's — a forget placed before the save loses one
    /// table per pass, so a single-table fixture would pass against the defect.
    /// </summary>
    [Fact]
    public async Task Each_Table_Is_Persisted_Before_The_Next_One_Is_Staged()
    {
        var store = new StagingArchiveStore(("Patient", 4), ("Appointment", 1), ("Invoice", 6));
        var (zip, manifest) = ArchiveOf("Patient", "Appointment", "Invoice");

        await RunAsync(zip, manifest, store);

        Assert.Equal(new[] { 4, 1, 6 }, store.PersistedPerSave);
    }

    /// <summary>
    /// A table that restored nothing must not be saved at all — the existing behaviour, pinned so the fix for the
    /// above cannot turn into a save per table on a restore that changes nothing.
    /// </summary>
    [Fact]
    public async Task A_Table_With_Nothing_To_Restore_Triggers_No_Save()
    {
        var store = new StagingArchiveStore(("Patient", 0));
        var (zip, manifest) = ArchiveOf("Patient");

        await RunAsync(zip, manifest, store);

        Assert.Empty(store.PersistedPerSave);
    }

    /// <summary>
    /// FR-4.1's last clause: a restore genuinely re-inserts records written elsewhere, so it declares a boundary
    /// rather than leaving a discontinuity that reads as tampering.
    /// </summary>
    [Fact]
    public async Task A_Restore_Records_A_Declared_Boundary()
    {
        var store = new StagingArchiveStore(("Patient", 1));
        var (zip, manifest) = ArchiveOf("Patient");
        var written = new List<AuditEntry>();

        await RunAsync(zip, manifest, store, written);

        var boundary = Assert.Single(written.Where(e => e.EntityType == AuditEntry.BoundaryEntityType));
        Assert.True(boundary.IsDeclaredGap);
        Assert.Equal(ClinicId, boundary.ClinicId);
    }

    // ---------------------------------------------------------------- harness

    private static async Task<ClinicManagement.Application.Common.Models.Result<ClinicArchiveRestoreReport>>
        RunAsync(
            ZipArchive zip,
            ClinicArchiveManifest manifest,
            StagingArchiveStore store,
            List<AuditEntry>? auditRows = null)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(store.Save()));

        var auditEntries = new Mock<ClinicManagement.Domain.Repositories.IAuditEntryRepository>();
        auditEntries
            .Setup(a => a.AddRangeAsync(
                It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyCollection<AuditEntry> rows, CancellationToken _) =>
            {
                auditRows?.AddRange(rows);
                return Task.CompletedTask;
            });

        var actor = new Mock<IAuditActorProvider>();
        actor.SetupGet(a => a.Current).Returns(new AuditActor("local|admin", "admin@cabinet.tn"));

        return await ClinicArchiveRestorer.ApplyAsync(
            zip,
            manifest,
            ClinicId,
            store,
            Mock.Of<IFileStorage>(),
            unitOfWork.Object,
            actor.Object,
            auditEntries.Object,
            NullLogger.Instance);
    }

    /// <summary>An in-memory archive whose data entries are present but empty — the fake store decides the counts.</summary>
    private static (ZipArchive Zip, ClinicArchiveManifest Manifest) ArchiveOf(params string[] tables)
    {
        var buffer = new MemoryStream();
        using (var writing = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var table in tables)
            {
                var entry = writing.CreateEntry(ClinicArchiveFormat.DataEntry(table));
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes("[]"));
            }
        }

        buffer.Position = 0;

        var manifest = new ClinicArchiveManifest
        {
            ClinicId = ClinicId,
            CreatedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Tables = tables.Select(t => new ClinicArchiveTableCount(t, 0)).ToList(),
        };

        return (new ZipArchive(buffer, ZipArchiveMode.Read), manifest);
    }

    /// <summary>
    /// The store as far as this test cares: <c>RestoreTableAsync</c> stages a table's rows,
    /// <c>ForgetRestoredRows</c> drops whatever is staged, and a save moves staged rows into
    /// <see cref="Persisted"/>. Rows dropped before a save land in <see cref="Discarded"/> — which is the whole
    /// signature of the defect.
    /// </summary>
    private sealed class StagingArchiveStore : IClinicArchiveStore
    {
        private readonly Queue<(string Table, int Rows)> _plan;

        public StagingArchiveStore(params (string Table, int Rows)[] plan) => _plan = new Queue<(string, int)>(plan);

        private int Staged { get; set; }

        public int Persisted { get; private set; }

        public int Discarded { get; private set; }

        public List<int> PersistedPerSave { get; } = new();

        public int Save()
        {
            Persisted += Staged;
            PersistedPerSave.Add(Staged);
            Staged = 0;
            return Persisted;
        }

        public Task<ClinicArchiveTableOutcome> RestoreTableAsync(
            string table, Guid clinicId, string json, CancellationToken cancellationToken = default)
        {
            var rows = _plan.Count > 0 ? _plan.Dequeue().Rows : 0;
            Staged += rows;
            return Task.FromResult(new ClinicArchiveTableOutcome(rows, 0, 0));
        }

        public void ForgetRestoredRows()
        {
            Discarded += Staged;
            Staged = 0;
        }

        public bool CanRestore(string table) => true;

        public Task<ClinicArchiveExport> ExportAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The restorer never exports.");
    }
}
