using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.Features.Backup.Archive;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Backup;

/// <summary>
/// The archive as a <b>file</b>: what the zip contains, what the manifest promises, and what this build refuses to
/// read back (<c>clinic-data-archive-and-restore</c> AC-1, AC-5, AC-7).
///
/// <para><b>The write and the read are exercised against each other</b> wherever possible — the manifest a test
/// checks is the one <see cref="ClinicArchivePackager.WriteAsync"/> actually wrote, parsed by
/// <see cref="ClinicArchivePackager.ReadManifest"/>. A hand-written expectation on either side would let the two
/// drift into a file this build produces and cannot open, which is the one defect a backup must not have.</para>
/// </summary>
public class ClinicArchivePackagerTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string ClinicName = "Cabinet Ben Ali";

    /// <summary>A key written before <c>multi-tenant-cloud</c> US-5: flat, with no <c>clinics/{id}/</c> prefix (EC-4).</summary>
    private const string FlatLegacyKey = "8f3a2c11-0002-4f0e-9a11-1c2d3e4f5060-20240117104500.pdf";

    private const string PrefixedKey = "clinics/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/radios/panoramique.png";

    private static async Task<MemoryStream> WriteAsync(FakeArchiveStore store, FakeBlobStore blobs)
    {
        var buffer = new MemoryStream();

        await ClinicArchivePackager.WriteAsync(
            buffer, ClinicA, ClinicName, store, blobs, NullLogger.Instance, cancellationToken: CancellationToken.None);

        buffer.Position = 0;
        return buffer;
    }

    private static string TextOf(ZipArchive zip, string entryName)
    {
        using var stream = zip.GetEntry(entryName)!.Open();
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------ what the file contains

    // [AC-1] The layout the restore reads back: a manifest, one file per table, and the blobs — each at its OWN
    // storage key, verbatim, including a flat pre-US-5 one. Re-prefixing on either side would put the bytes where
    // the restored row does not look, and the file would read as « introuvable » on a perfectly healthy row.
    [Fact]
    public async Task The_Archive_Holds_A_Manifest_The_Tables_And_The_Blobs_At_Their_Own_Keys()
    {
        var store = new FakeArchiveStore()
            .Table("Patient", """[{"Id":"11111111-1111-1111-1111-111111111111"}]""", rows: 1)
            .Table("Invoice", "[]");
        store.StorageKeys.AddRange(new[] { FlatLegacyKey, PrefixedKey });

        var blobs = new FakeBlobStore();
        blobs.Put(FlatLegacyKey, "ordonnance");
        blobs.Put(PrefixedKey, "panoramique");

        using var zip = new ZipArchive(await WriteAsync(store, blobs), ZipArchiveMode.Read);

        Assert.NotNull(zip.GetEntry(ClinicArchiveFormat.ManifestEntry));
        Assert.NotNull(zip.GetEntry("data/Patient.json"));
        Assert.NotNull(zip.GetEntry("data/Invoice.json"));
        Assert.NotNull(zip.GetEntry($"blobs/{FlatLegacyKey}"));
        Assert.NotNull(zip.GetEntry($"blobs/{PrefixedKey}"));
        Assert.Equal("ordonnance", TextOf(zip, $"blobs/{FlatLegacyKey}"));
    }

    // [AC-7] The manifest describes what actually landed and is read back by this build's own reader — the two
    // halves of the file meeting, rather than each meeting a literal in a test.
    [Fact]
    public async Task The_Manifest_Describes_What_Landed_And_This_Build_Accepts_It()
    {
        var store = new FakeArchiveStore()
            .Table("Patient", "[]", rows: 42)
            .Table("Invoice", "[]", rows: 7);
        store.StorageKeys.Add(PrefixedKey);

        var blobs = new FakeBlobStore();
        blobs.Put(PrefixedKey, "panoramique");

        using var zip = new ZipArchive(await WriteAsync(store, blobs), ZipArchiveMode.Read);
        var read = ClinicArchivePackager.ReadManifest(zip);

        Assert.False(read.IsRefused);
        var manifest = read.Manifest!;

        Assert.Equal(ClinicArchiveFormat.SchemaVersion, manifest.SchemaVersion);
        Assert.Equal(ClinicA, manifest.ClinicId);
        Assert.Equal(ClinicName, manifest.ClinicName);
        Assert.Equal(1, manifest.BlobCount);
        Assert.Equal(new[] { "Patient", "Invoice" }, manifest.Tables.Select(t => t.Entity));
        Assert.Equal(42, manifest.Tables[0].Rows);
    }

    // [AC-3] The manifest's table order is the export's — parents before children — because it is the order the
    // restore applies. Shuffling it would put an invoice line in front of its invoice.
    [Fact]
    public async Task The_Manifest_Keeps_The_Exports_Parent_Before_Child_Order()
    {
        var store = new FakeArchiveStore()
            .Table("Clinic").Table("Patient").Table("Invoice").Table("InvoiceLine");

        using var zip = new ZipArchive(await WriteAsync(store, new FakeBlobStore()), ZipArchiveMode.Read);

        Assert.Equal(
            new[] { "Clinic", "Patient", "Invoice", "InvoiceLine" },
            ClinicArchivePackager.ReadManifest(zip).Manifest!.Tables.Select(t => t.Entity));
    }

    // A blob the object store has lost costs the practice that file and nothing else: the twenty thousand rows
    // beside it still travel, and the manifest says in French what is missing. A refusal here would be the wrong
    // trade — « voici ce qui manque » is something an owner can act on.
    [Fact]
    public async Task An_Unreadable_Blob_Is_A_Warning_And_The_Rest_Of_The_Archive_Still_Travels()
    {
        var store = new FakeArchiveStore().Table("PatientFile", "[]", rows: 2);
        store.StorageKeys.AddRange(new[] { FlatLegacyKey, PrefixedKey });

        var blobs = new FakeBlobStore();
        blobs.Put(FlatLegacyKey, "ordonnance");
        blobs.Put(PrefixedKey, "panoramique");
        blobs.Unreadable.Add(PrefixedKey);

        using var zip = new ZipArchive(await WriteAsync(store, blobs), ZipArchiveMode.Read);
        var manifest = ClinicArchivePackager.ReadManifest(zip).Manifest!;

        Assert.Equal(1, manifest.BlobCount);
        Assert.Contains(manifest.Warnings, w => w.Contains(PrefixedKey, StringComparison.Ordinal));
        Assert.NotNull(zip.GetEntry($"blobs/{FlatLegacyKey}"));
        Assert.NotNull(zip.GetEntry("data/PatientFile.json"));
    }

    // What the export could not scope is carried into the file, so the archive explains its own gaps rather than
    // being quietly smaller than the practice.
    [Fact]
    public async Task The_Exports_Own_Warnings_Travel_In_The_Manifest()
    {
        var store = new FakeArchiveStore().Table("Patient");
        store.Warnings.Add("« Machin » n'a pas pu être rattachée au cabinet et n'est pas incluse dans l'archive.");

        using var zip = new ZipArchive(await WriteAsync(store, new FakeBlobStore()), ZipArchiveMode.Read);

        Assert.Contains(
            ClinicArchivePackager.ReadManifest(zip).Manifest!.Warnings,
            w => w.Contains("Machin", StringComparison.Ordinal));
    }

    // French accents survive as characters, not as é: an archive is a file the practice may hand to somebody
    // else, and one nobody can open is a promise nobody can check.
    [Fact]
    public async Task French_Text_Is_Written_Readably()
    {
        var store = new FakeArchiveStore().Table("Patient", """[{"FirstName":"Béchir"}]""", rows: 1);

        using var zip = new ZipArchive(await WriteAsync(store, new FakeBlobStore()), ZipArchiveMode.Read);

        Assert.Contains("Béchir", TextOf(zip, "data/Patient.json"), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ AC-7, the refusals

    // [AC-7] A file this build cannot read is refused BEFORE anything is written, and both versions are named:
    // « incompatible » alone leaves the reader unable to tell whether the file is too old for the application or
    // the application too old for the file — opposite actions.
    [Fact]
    public void An_Archive_From_Another_Schema_Version_Is_Refused_Naming_Both()
    {
        using var zip = ArchiveCarrying(new
        {
            SchemaVersion = ClinicArchiveFormat.SchemaVersion + 7,
            ClinicId = ClinicA,
            ClinicName,
        });

        var read = ClinicArchivePackager.ReadManifest(zip);

        Assert.True(read.IsRefused);
        Assert.Equal(ClinicArchiveFormat.SchemaUnsupportedCode, read.Code);
        Assert.Contains((ClinicArchiveFormat.SchemaVersion + 7).ToString(), read.Error!, StringComparison.Ordinal);
        Assert.Contains(ClinicArchiveFormat.SchemaVersion.ToString(), read.Error!, StringComparison.Ordinal);
        Assert.Null(read.Manifest);
    }

    // [AC-7] A zip that is not an archive of ours at all.
    [Fact]
    public void A_Zip_With_No_Manifest_Is_Refused_As_Invalid()
    {
        using var zip = ZipOf(("data/Patient.json", "[]"));

        var read = ClinicArchivePackager.ReadManifest(zip);

        Assert.True(read.IsRefused);
        Assert.Equal(ClinicArchiveFormat.InvalidCode, read.Code);
    }

    // [AC-7] A truncated or corrupted manifest. ⚠️ An empty clinic id counts: it would restore rows nothing points
    // at, which is worse than a refusal because it looks like it worked.
    [Theory]
    [InlineData("{ not json at all")]
    [InlineData("""{"SchemaVersion":1,"ClinicId":"00000000-0000-0000-0000-000000000000"}""")]
    public void An_Unreadable_Manifest_Is_Refused_As_Invalid(string manifestJson)
    {
        using var zip = ZipOf((ClinicArchiveFormat.ManifestEntry, manifestJson));

        var read = ClinicArchivePackager.ReadManifest(zip);

        Assert.True(read.IsRefused);
        Assert.Equal(ClinicArchiveFormat.InvalidCode, read.Code);
    }

    // ------------------------------------------------------------------ the file's own name and keys

    // The download is filed under the cabinet and the CLINIC-LOCAL day. An owner archives repeatedly, and one
    // taken at 00:30 Tunis filed under the previous day is how the wrong file gets restored.
    [Theory]
    [InlineData("Cabinet Ben Ali", "archive-cabinet-ben-ali-2026-08-11.zip")]
    [InlineData("Dr. Ben Ali & Fils", "archive-dr-ben-ali-fils-2026-08-11.zip")]
    [InlineData("///", "archive-cabinet-2026-08-11.zip")]
    public void The_Download_Is_Named_After_The_Cabinet_And_The_Clinic_Day(string clinicName, string expected)
    {
        Assert.Equal(expected, ClinicArchiveFormat.FileName(clinicName, new DateTime(2026, 8, 11)));
    }

    // [EC-4] A storage key survives the trip into an entry name and back out, slashes and all — the two halves of
    // the same rule, so a key cannot be readable on the way in and unrecognisable on the way back.
    [Theory]
    [InlineData(FlatLegacyKey)]
    [InlineData(PrefixedKey)]
    public void A_Storage_Key_Round_Trips_Through_Its_Entry_Name(string storageKey)
    {
        Assert.Equal(storageKey, ClinicArchiveFormat.StorageKeyOf(ClinicArchiveFormat.BlobEntry(storageKey)));
    }

    // An entry that is not a blob is not mistaken for one — otherwise the restore would try to write the manifest
    // back into the object store under a key nothing holds.
    [Theory]
    [InlineData("manifest.json")]
    [InlineData("data/Patient.json")]
    [InlineData("blobs/")]
    public void A_Non_Blob_Entry_Names_No_Storage_Key(string entryName)
    {
        Assert.Null(ClinicArchiveFormat.StorageKeyOf(entryName));
    }

    // ------------------------------------------------------------------ helpers

    private static ZipArchive ArchiveCarrying(object manifest) =>
        ZipOf((ClinicArchiveFormat.ManifestEntry, JsonSerializer.Serialize(manifest, ClinicArchiveFormat.Json)));

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
