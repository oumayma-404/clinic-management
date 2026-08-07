using System.Text;
using ClinicManagement.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Storage;

/// <summary>
/// Local-disk <c>IFileStorage</c> backend used in Local (offline) mode. Exercises the real
/// filesystem under a throwaway temp folder — no MinIO, no Docker (FR-C1/C2 parity).
/// </summary>
public class LocalDiskFileStorageTests : IDisposable
{
    private static readonly Guid Clinic = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly string _basePath;
    private readonly LocalDiskFileStorage _storage;

    public LocalDiskFileStorageTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), "clinic-localdisk-tests", Guid.NewGuid().ToString("N"));
        _storage = new LocalDiskFileStorage(_basePath, NullLogger<LocalDiskFileStorage>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static MemoryStream Bytes(string content) => new(Encoding.UTF8.GetBytes(content));

    private async Task<string> ReadAll(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    // [AC-1] Uploading persists to the configured folder and the blob is downloadable, no MinIO.
    [Fact]
    public async Task Upload_Then_Download_Roundtrips_Content()
    {
        var key = await _storage.UploadAsync(Bytes("hello clinic"), "text/plain", Clinic, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(key));
        await using var downloaded = await _storage.DownloadAsync(key, CancellationToken.None);
        Assert.Equal("hello clinic", await ReadAll(downloaded));
    }

    // [US-5] Every new key is clinic-prefixed, in this backend too — the two must not disagree about what a key
    // means, or a Local install's blobs and a hosted one's are laid out differently for no reason.
    [Fact]
    public async Task Upload_Prefixes_The_Key_With_Its_Clinic()
    {
        var generated = await _storage.UploadAsync(Bytes("x"), "text/plain", Clinic, CancellationToken.None);
        var deterministic = await _storage.UploadAsync(Bytes("y"), "image/png", Clinic, "logo", CancellationToken.None);

        Assert.StartsWith($"clinics/{Clinic}/", generated);
        Assert.Equal($"clinics/{Clinic}/logo", deterministic);
    }

    // [US-5 / M2] A key written before US-5 is flat, and there is no backfill — so reading must NOT prefix.
    [Fact]
    public async Task Download_And_Delete_Resolve_A_Legacy_Flat_Key()
    {
        Directory.CreateDirectory(_basePath);
        const string legacyKey = "8f14e45f-ceea-467a-9c1e-000000000000-20250104120000";
        await File.WriteAllTextAsync(Path.Combine(_basePath, legacyKey), "written before US-5");

        await using (var downloaded = await _storage.DownloadAsync(legacyKey, CancellationToken.None))
        {
            Assert.Equal("written before US-5", await ReadAll(downloaded));
        }

        await _storage.DeleteAsync(legacyKey, CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(_basePath, legacyKey)));
    }

    // [AC-1] The base folder is created on first use when missing.
    [Fact]
    public async Task Upload_Creates_Base_Folder_When_Missing()
    {
        Assert.False(Directory.Exists(_basePath));

        await _storage.UploadAsync(Bytes("x"), "text/plain", Clinic, CancellationToken.None);

        Assert.True(Directory.Exists(_basePath));
    }

    // [AC-3 / edge] Concurrent uploads never collide — each gets a unique guid-based key.
    [Fact]
    public async Task Upload_Generates_Unique_Keys()
    {
        var key1 = await _storage.UploadAsync(Bytes("a"), "text/plain", Clinic, CancellationToken.None);
        var key2 = await _storage.UploadAsync(Bytes("b"), "text/plain", Clinic, CancellationToken.None);

        Assert.NotEqual(key1, key2);
    }

    // [AC-5] The customPath overload writes to a deterministic key and overwrites in place.
    [Fact]
    public async Task Upload_With_CustomPath_Is_Deterministic_And_Overwrites()
    {
        const string relativePath = "logo";
        var expectedKey = $"clinics/{Clinic}/logo";

        var firstKey = await _storage.UploadAsync(Bytes("v1"), "image/png", Clinic, relativePath, CancellationToken.None);
        var secondKey = await _storage.UploadAsync(Bytes("v2"), "image/png", Clinic, relativePath, CancellationToken.None);

        Assert.Equal(expectedKey, firstKey);
        Assert.Equal(expectedKey, secondKey); // same key each time
        await using var downloaded = await _storage.DownloadAsync(expectedKey, CancellationToken.None);
        Assert.Equal("v2", await ReadAll(downloaded)); // overwritten in place, not duplicated
    }

    // [AC-4] Downloading a key that doesn't exist reports a clean failure (no unhandled crash).
    [Fact]
    public async Task Download_Missing_Key_Throws_Clean_FileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _storage.DownloadAsync("does-not-exist", CancellationToken.None));
    }

    // [AC-4] Deleting a key that doesn't exist is idempotent (mirrors MinIO/S3) — no crash.
    [Fact]
    public async Task Delete_Missing_Key_Does_Not_Throw()
    {
        var exception = await Record.ExceptionAsync(
            () => _storage.DeleteAsync("does-not-exist", CancellationToken.None));

        Assert.Null(exception);
    }

    // Delete removes a stored blob so a subsequent download fails.
    [Fact]
    public async Task Delete_Removes_Stored_Blob()
    {
        var key = await _storage.UploadAsync(Bytes("bye"), "text/plain", Clinic, CancellationToken.None);

        await _storage.DeleteAsync(key, CancellationToken.None);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _storage.DownloadAsync(key, CancellationToken.None));
    }

    // [Edge] A crafted path with ".." can never resolve outside the base folder. Since US-5 it cannot climb out
    // of its own clinic either — the refusal now comes from ClinicStorageKey, so MinIO (which has no traversal
    // semantics and would happily have stored the literal name) refuses the identical path.
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("../../escape.txt")]
    [InlineData("sub/../../escape.txt")]
    public async Task Upload_Rejects_Path_Traversal_Keys(string maliciousPath)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _storage.UploadAsync(Bytes("evil"), "text/plain", Clinic, maliciousPath, CancellationToken.None));
    }

    // ---- ProbeAsync: the /health storage check (multi-tenant-cloud US-6) ----

    // [US-6] A writable base folder is healthy, and the folder is created if it does not exist yet — first boot.
    [Fact]
    public async Task Probe_Succeeds_And_Creates_The_Base_Folder()
    {
        Assert.False(Directory.Exists(_basePath));

        await _storage.ProbeAsync(CancellationToken.None);

        Assert.True(Directory.Exists(_basePath));
    }

    // [US-6] It leaves nothing behind. A probe that littered would fill the clinic's own file store, one file per
    // health poll, for the life of the install.
    [Fact]
    public async Task Probe_Leaves_No_File_Behind()
    {
        await _storage.ProbeAsync(CancellationToken.None);
        await _storage.ProbeAsync(CancellationToken.None);

        Assert.Empty(Directory.GetFileSystemEntries(_basePath));
    }

    // [US-6] It actually WRITES. An unmounted volume, a full disk and a folder the service account cannot write to
    // all look like an existing directory — checking only for existence would report healthy and then fail the
    // first upload.
    [Fact]
    public async Task Probe_Fails_When_The_Base_Path_Is_Not_A_Directory()
    {
        var parent = Path.Combine(Path.GetTempPath(), "clinic-localdisk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var occupied = Path.Combine(parent, "not-a-folder");
        await File.WriteAllTextAsync(occupied, "in the way");

        try
        {
            var storage = new LocalDiskFileStorage(occupied, NullLogger<LocalDiskFileStorage>.Instance);

            await Assert.ThrowsAnyAsync<IOException>(() => storage.ProbeAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
