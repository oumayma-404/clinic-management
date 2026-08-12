using System.IO.Compression;
using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Backup.Archive;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Backup;

/// <summary>
/// Applying an archive — the half both doors share (<c>clinic-data-archive-and-restore</c> AC-2, AC-4, AC-5, AC-9).
///
/// <para><b>What is testable here and what is not.</b> Deciding whether a row is present, identical or different is
/// a database question and belongs to <see cref="ClinicArchiveStoreMaterializationTests"/>, which exercises the
/// comparison and the materialisation directly. What lives here is everything the restorer itself owns: the order
/// tables are applied in, that a save happens only where something was staged and that rows are forgotten
/// <i>after</i> it, that a table this build does not know is <b>named</b> rather than skipped in silence, that the
/// blobs go back at their own keys and never over bytes that are already there, and that the actor is declared as a
/// restore before a single row is staged.</para>
/// </summary>
public class ClinicArchiveRestorerTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime ArchivedAt = new(2026, 7, 4, 21, 15, 0, DateTimeKind.Utc);

    private const string FlatLegacyKey = "8f3a2c11-0002-4f0e-9a11-1c2d3e4f5060-20240117104500.pdf";
    private const string PrefixedKey = "clinics/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/radios/panoramique.png";

    private readonly FakeBlobStore _blobs = new();
    private readonly List<string> _log = new();

    private CountingUnitOfWork UnitOfWork() => new() { Calls = _log };

    private FakeArchiveStore Store() => new() { Calls = _log };

    private static ClinicArchiveManifest Manifest(params (string Entity, int Rows)[] tables) => new()
    {
        SchemaVersion = ClinicArchiveFormat.SchemaVersion,
        ClinicId = ClinicA,
        ClinicName = "Cabinet Ben Ali",
        CreatedAtUtc = ArchivedAt,
        Tables = tables.Select(t => new ClinicArchiveTableCount(t.Entity, t.Rows)).ToList(),
    };

    private readonly FakeAuditEntryRepository _auditEntries = new();

    private async Task<ClinicArchiveRestoreReport> ApplyAsync(
        ZipArchive zip,
        ClinicArchiveManifest manifest,
        IClinicArchiveStore store,
        IUnitOfWork unitOfWork,
        IAuditActorProvider? actor = null)
    {
        var applied = await ClinicArchiveRestorer.ApplyAsync(
            zip, manifest, ClinicA, store, _blobs, unitOfWork,
            actor ?? new ProcessAuditActorProvider(), _auditEntries, NullLogger.Instance, CancellationToken.None);

        Assert.True(applied.IsSuccess, applied.Error);

        return applied.Value!;
    }

    // ------------------------------------------------------------------ AC-2 / AC-3 / AC-4

    // [AC-3] Tables are applied in the MANIFEST's order — the export's own, parents before children — and into the
    // clinic the caller named rather than the one the file claims.
    [Fact]
    public async Task Tables_Are_Applied_In_The_Manifests_Order_And_Into_The_Given_Clinic()
    {
        var store = Store();
        store.Table("Clinic", outcome: new ClinicArchiveTableOutcome(1, 0, 0));
        store.Table("Patient", outcome: new ClinicArchiveTableOutcome(3, 0, 0));
        store.Table("Invoice", outcome: new ClinicArchiveTableOutcome(2, 0, 0));

        using var zip = ZipOf(("data/Clinic.json", "[]"), ("data/Patient.json", "[]"), ("data/Invoice.json", "[]"));

        await ApplyAsync(zip, Manifest(("Clinic", 1), ("Patient", 3), ("Invoice", 2)), store, UnitOfWork());

        Assert.Equal(
            new[] { "restore:Clinic", "restore:Patient", "restore:Invoice" },
            _log.Where(c => c.StartsWith("restore:", StringComparison.Ordinal)));
        Assert.All(store.RestoredIntoClinics, id => Assert.Equal(ClinicA, id));
    }

    // [AC-2] The second restore: everything is already there and identical, so nothing is written at all — not a
    // save, not a blob. « Déjà présent » is what a nervous owner running it twice must see.
    [Fact]
    public async Task Restoring_An_Archive_A_Second_Time_Writes_Nothing()
    {
        var store = Store();
        store.Table("Patient", outcome: new ClinicArchiveTableOutcome(0, 3, 0));

        var unitOfWork = UnitOfWork();
        using var zip = ZipOf(("data/Patient.json", "[]"));

        var report = await ApplyAsync(zip, Manifest(("Patient", 3)), store, unitOfWork);

        Assert.Equal(0, unitOfWork.Saves);
        Assert.Equal(0, report.TotalRestored);
        Assert.Equal(3, report.TotalAlreadyPresent);
        Assert.Equal(3, report.AlreadyPresent["Patient"]);
        Assert.Empty(report.Restored);
    }

    // [AC-4] A row that exists but DIFFERS is counted apart from « déjà présent » and never joins the restored
    // count — « 3 conflits sur Patient » sends an owner to three records, where one blended total says nothing.
    [Fact]
    public async Task A_Row_That_Differs_Is_Counted_Apart_From_One_That_Is_Identical()
    {
        var store = Store();
        store.Table("Patient", outcome: new ClinicArchiveTableOutcome(1, 5, 2));

        var report = await ApplyAsync(
            ZipOf(("data/Patient.json", "[]")), Manifest(("Patient", 8)), store, UnitOfWork());

        Assert.Equal(1, report.Restored["Patient"]);
        Assert.Equal(5, report.AlreadyPresent["Patient"]);
        Assert.Equal(2, report.Conflicts["Patient"]);
        Assert.Equal(1, report.TotalRestored);
        Assert.Equal(2, report.TotalConflicts);
    }

    // A save happens once per table that staged something, and rows are forgotten AFTER it. The ordering is the
    // property: detaching an Added entry before its commit discards the insert silently — no exception, no row,
    // and a report that says « restauré ».
    [Fact]
    public async Task Rows_Are_Forgotten_Only_After_Their_Table_Has_Been_Committed()
    {
        var store = Store();
        store.Table("Patient", outcome: new ClinicArchiveTableOutcome(3, 0, 0));
        store.Table("Invoice", outcome: new ClinicArchiveTableOutcome(0, 4, 0));

        var unitOfWork = UnitOfWork();
        using var zip = ZipOf(("data/Patient.json", "[]"), ("data/Invoice.json", "[]"));

        await ApplyAsync(zip, Manifest(("Patient", 3), ("Invoice", 4)), store, unitOfWork);

        // One save — Invoice staged nothing — and « forget » never precedes it.
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal(new[] { "restore:Patient", "save", "forget", "restore:Invoice" }, _log);
    }

    // The report answers « quelle sauvegarde ai-je remise ? », which is the first question after running one.
    [Fact]
    public async Task The_Report_Names_The_Archive_It_Applied()
    {
        var store = Store();
        store.Table("Patient", outcome: new ClinicArchiveTableOutcome(1, 0, 0));

        var report = await ApplyAsync(
            ZipOf(("data/Patient.json", "[]")), Manifest(("Patient", 1)), store, UnitOfWork());

        Assert.Equal(ArchivedAt, report.ArchivedAtUtc);
        Assert.Equal(ClinicA, report.ClinicId);
    }

    // ------------------------------------------------------------------ the gaps, named rather than silent

    // A table this build does not know — the archive is newer, or the table was retired. « 4 tables ignorées » is
    // what tells an owner the copy is not complete; skipping it in silence reads as a successful restore.
    [Fact]
    public async Task A_Table_This_Build_Cannot_Restore_Is_Named_And_Never_Applied()
    {
        var store = Store();
        store.Table("Patient", outcome: new ClinicArchiveTableOutcome(1, 0, 0));

        using var zip = ZipOf(("data/Patient.json", "[]"), ("data/Machin.json", "[]"));

        var report = await ApplyAsync(zip, Manifest(("Patient", 1), ("Machin", 9)), store, UnitOfWork());

        Assert.Contains(report.Warnings, w => w.Contains("Machin", StringComparison.Ordinal));
        Assert.DoesNotContain("restore:Machin", _log);
    }

    // A manifest promising a table the file does not carry: a truncated download looks exactly like a smaller
    // practice unless the difference is stated.
    [Fact]
    public async Task A_Table_Promised_But_Absent_From_The_File_Is_Named()
    {
        var store = Store();
        store.Table("Patient", outcome: new ClinicArchiveTableOutcome(1, 0, 0));
        store.Table("Invoice");

        using var zip = ZipOf(("data/Patient.json", "[]"));

        var report = await ApplyAsync(zip, Manifest(("Patient", 1), ("Invoice", 12)), store, UnitOfWork());

        // Named in French: the sentence is read by a cabinet owner, not by whoever wrote the CLR type.
        Assert.Contains(report.Warnings, w => w.Contains("Note d'honoraires", StringComparison.Ordinal));
        Assert.DoesNotContain("restore:Invoice", _log);
    }

    // ------------------------------------------------------------------ AC-5, the blobs

    // [AC-5][EC-4] The bytes go back at the key the ROW already holds, verbatim — a flat pre-US-5 key included.
    // Re-prefixing would write them where the restored row does not look, and the file would download as
    // « introuvable » on a row that looks perfectly healthy.
    [Fact]
    public async Task A_Restored_File_Is_Written_Back_At_Its_Original_Key()
    {
        var store = Store();
        store.Table("PatientFile", outcome: new ClinicArchiveTableOutcome(2, 0, 0),
            blobKeys: new[] { FlatLegacyKey, PrefixedKey });

        using var zip = ZipOf(
            ("data/PatientFile.json", "[]"),
            ($"blobs/{FlatLegacyKey}", "ordonnance"),
            ($"blobs/{PrefixedKey}", "panoramique"));

        var report = await ApplyAsync(zip, Manifest(("PatientFile", 2)), store, UnitOfWork());

        Assert.Equal(2, report.BlobsRestored);
        Assert.Equal(new[] { FlatLegacyKey, PrefixedKey }, _blobs.RestoredKeys);
        Assert.Equal("ordonnance", _blobs.TextAt(FlatLegacyKey));
    }

    // [AC-5] The blob half of the additive rule: bytes already in the store are left exactly as they are, so
    // putting an archive back cannot undo a file the practice has replaced since.
    [Fact]
    public async Task Bytes_That_Are_Already_There_Are_Left_Alone()
    {
        _blobs.Put(PrefixedKey, "la version corrigée");

        var store = Store();
        store.Table("PatientFile", outcome: new ClinicArchiveTableOutcome(0, 1, 0), blobKeys: new[] { PrefixedKey });

        using var zip = ZipOf(("data/PatientFile.json", "[]"), ($"blobs/{PrefixedKey}", "l'ancienne version"));

        var report = await ApplyAsync(zip, Manifest(("PatientFile", 1)), store, UnitOfWork());

        Assert.Equal(0, report.BlobsRestored);
        Assert.Empty(_blobs.RestoredKeys);
        Assert.Equal("la version corrigée", _blobs.TextAt(PrefixedKey));
    }

    // A blob that will not write costs that file and nothing else — the row it belongs to is already back, and
    // losing a whole practice's restore over one unreadable file would be the wrong trade.
    [Fact]
    public async Task A_Blob_That_Cannot_Be_Written_Is_A_Warning_Not_A_Failure()
    {
        _blobs.Unwritable.Add(PrefixedKey);

        var store = Store();
        store.Table("PatientFile", outcome: new ClinicArchiveTableOutcome(2, 0, 0),
            blobKeys: new[] { FlatLegacyKey, PrefixedKey });

        using var zip = ZipOf(
            ("data/PatientFile.json", "[]"),
            ($"blobs/{FlatLegacyKey}", "ordonnance"),
            ($"blobs/{PrefixedKey}", "panoramique"));

        var report = await ApplyAsync(zip, Manifest(("PatientFile", 2)), store, UnitOfWork());

        Assert.Equal(2, report.TotalRestored);
        Assert.Equal(1, report.BlobsRestored);
        Assert.Contains(report.Warnings, w => w.Contains(PrefixedKey, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ AC-9, the actor

    // [AC-9] The scope is declared a restore BEFORE anything is staged, so every row it writes reads as a restore
    // rather than as one colleague typing three thousand fiches in an afternoon.
    [Fact]
    public async Task The_Scope_Is_Declared_A_Restore_Before_The_First_Row_Is_Staged()
    {
        var actor = new RecordingActorProvider(_log);

        var store = Store();
        store.Table("Patient", outcome: new ClinicArchiveTableOutcome(3, 0, 0));

        await ApplyAsync(
            ZipOf(("data/Patient.json", "[]")), Manifest(("Patient", 3)), store, UnitOfWork(), actor);

        Assert.Equal("restoring", _log[0]);
        Assert.True(actor.Current.IsRestore);
    }

    // [AC-9] And it DECORATES whoever is in scope rather than replacing them: « qui a restauré ? » stays
    // answerable while « ces trois mille fiches ont-elles été saisies ? » answers no. Both questions matter.
    [Fact]
    public async Task The_Person_Who_Ran_The_Restore_Is_Still_Named()
    {
        var actor = new ProcessAuditActorProvider();
        actor.RunAs("console-operator");

        var store = Store();
        store.Table("Patient", outcome: new ClinicArchiveTableOutcome(1, 0, 0));

        await ApplyAsync(
            ZipOf(("data/Patient.json", "[]")), Manifest(("Patient", 1)), store, UnitOfWork(), actor);

        Assert.StartsWith(AuditActor.RestorePrefix, actor.Current.UserId, StringComparison.Ordinal);
        Assert.Contains("console-operator", actor.Current.UserId, StringComparison.Ordinal);
    }

    // [AC-9] A child table leaves NO trace of its own in « Journal d'activité »: the interceptor writes one row per
    // aggregate ROOT, and a restore inserts children independently of their parents — so four thousand payments
    // re-inserted into invoices that still exist wrote nothing at all, and the money reappeared everywhere with
    // no answer to « d'où vient-il ? ». Declaring the actor a restore is necessary and not sufficient: there has
    // to be a row for the prefix to travel on.
    [Fact]
    public async Task Each_Restored_Table_Leaves_A_Ledger_Row_Naming_What_It_Did()
    {
        var store = Store();
        store.Table("Payment", outcome: new ClinicArchiveTableOutcome(4000, 2, 1));

        await ApplyAsync(ZipOf(("data/Payment.json", "[]")), Manifest(("Payment", 4003)), store, UnitOfWork());

        var row = Assert.Single(_auditEntries.Entries);

        Assert.Equal("Payment", row.EntityType);
        Assert.Equal(ClinicA, row.ClinicId);
        Assert.StartsWith(AuditActor.RestorePrefix, row.UserId, StringComparison.Ordinal);
        Assert.Contains("4000", row.ChangedFields!, StringComparison.Ordinal);
    }

    // A table that touched nothing writes no ledger row — a restore of an unchanged cabinet must not bury its own
    // journal in thirty rows saying « rien ».
    [Fact]
    public async Task A_Table_With_Nothing_To_Do_Writes_No_Ledger_Row()
    {
        var store = Store();
        store.Table("Payment");

        await ApplyAsync(ZipOf(("data/Payment.json", "[]")), Manifest(("Payment", 0)), store, UnitOfWork());

        Assert.Empty(_auditEntries.Entries);
    }

    // ------------------------------------------------------------------ the failure path

    // [AC-4] A fault part way through is a REFUSAL naming the table, not an exception: the caller's transaction is
    // what makes « aucune donnée n'a été modifiée » true, and the owner needs to know where it stopped. Before
    // this the tables before it were committed and the message was a generic 500.
    [Fact]
    public async Task A_Table_That_Fails_Stops_The_Restore_And_Names_Itself()
    {
        var store = Store();
        store.Table("Patient", outcome: new ClinicArchiveTableOutcome(3, 0, 0));
        store.Table("Invoice", outcome: new ClinicArchiveTableOutcome(1, 0, 0));
        store.Failing.Add("Invoice");

        using var zip = ZipOf(("data/Patient.json", "[]"), ("data/Invoice.json", "[]"));

        var applied = await ClinicArchiveRestorer.ApplyAsync(
            zip, Manifest(("Patient", 3), ("Invoice", 1)), ClinicA, store, _blobs, UnitOfWork(),
            new ProcessAuditActorProvider(), _auditEntries, NullLogger.Instance, CancellationToken.None);

        Assert.True(applied.IsFailure);
        Assert.Contains("Note d'honoraires", applied.Error!, StringComparison.Ordinal);
        Assert.Equal(ClinicArchiveFormat.InvalidCode, applied.Code);
    }

    // ------------------------------------------------------------------ the report the owner reads

    // The three dictionaries are keyed on CLR type names, which no cabinet owner reads — « PatientMedicalHistory ·
    // 12 remis » at the moment they are most anxious. The key stays on the wire and the French name travels with
    // it, the repo's standing convention.
    [Fact]
    public async Task Every_Entity_In_The_Report_Carries_Its_French_Name()
    {
        var store = Store();
        store.Table("PatientMedicalHistory", outcome: new ClinicArchiveTableOutcome(12, 0, 0));
        store.Table("InstallmentPayment", outcome: new ClinicArchiveTableOutcome(0, 0, 3));

        var report = await ApplyAsync(
            ZipOf(("data/PatientMedicalHistory.json", "[]"), ("data/InstallmentPayment.json", "[]")),
            Manifest(("PatientMedicalHistory", 12), ("InstallmentPayment", 3)), store, UnitOfWork());

        Assert.Equal("Antécédent médical", report.EntityLabels["PatientMedicalHistory"]);
        Assert.Equal("Paiement d'échéance", report.EntityLabels["InstallmentPayment"]);
    }

    // The manifest's own warnings are attacker-authored: they are deserialized out of the uploaded file, so
    // whoever supplies it controls unbounded French prose that would render on the vendor's console as the
    // server's own diagnostics — and could carry a patient's name into a read whose guarantee is a closed set of
    // field names. What this build could not restore, it says itself.
    [Fact]
    public async Task The_Uploaded_Manifests_Own_Warnings_Never_Reach_The_Report()
    {
        var store = Store();
        store.Table("Patient", outcome: new ClinicArchiveTableOutcome(1, 0, 0));

        var manifest = Manifest(("Patient", 1)) with
        {
            Warnings = new[] { "Contactez le support au 55 123 456 pour valider cette restauration." },
        };

        var report = await ApplyAsync(ZipOf(("data/Patient.json", "[]")), manifest, store, UnitOfWork());

        Assert.DoesNotContain(report.Warnings, w => w.Contains("55 123 456", StringComparison.Ordinal));
    }

    // [AC-5] `RestoreAtKeyAsync` is the one door around the US-5 invariant that an unprefixed key is not something
    // a caller can write, so a key naming ANOTHER cabinet's prefix must be refused — otherwise an archive whose
    // rows all already exist still creates objects inside the victim's prefix in the shared bucket.
    [Fact]
    public async Task A_Blob_Key_Naming_Another_Cabinet_Is_Refused()
    {
        const string foreign = "clinics/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/radios/vol.png";

        var store = Store();
        store.Table("PatientFile", outcome: new ClinicArchiveTableOutcome(1, 0, 0),
            blobKeys: new[] { foreign, PrefixedKey });

        using var zip = ZipOf(
            ("data/PatientFile.json", "[]"),
            ($"blobs/{foreign}", "voler"),
            ($"blobs/{PrefixedKey}", "panoramique"));

        var report = await ApplyAsync(zip, Manifest(("PatientFile", 1)), store, UnitOfWork());

        Assert.Equal(new[] { PrefixedKey }, _blobs.RestoredKeys);
        Assert.Equal(1, report.BlobsRestored);
        Assert.Contains(report.Warnings, w => w.Contains("autre cabinet", StringComparison.Ordinal));
    }


    /// <summary>Records the declaration in the shared log, so « before the first row » is an ordering assertion.</summary>
    private sealed class RecordingActorProvider : IAuditActorProvider
    {
        private readonly List<string> _log;
        private AuditActor _actor = new("local|admin", "admin@cabinet.tn");

        public RecordingActorProvider(List<string> log) => _log = log;

        public AuditActor Current => _actor;

        public void RunAs(string processName) { }

        public void RestoringAnArchive()
        {
            _log.Add("restoring");
            _actor = _actor.AsRestore();
        }
    }

    // ------------------------------------------------------------------ helpers

    private static ZipArchive ZipOf(params (string Name, string Content)[] entries)
    {
        var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }
}
