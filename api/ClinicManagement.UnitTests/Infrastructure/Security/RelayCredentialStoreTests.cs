using ClinicManagement.Infrastructure.Relay;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Security;

/// <summary>
/// The PC de secours's own credentials file (<c>clinic-pc-copy</c> D8): readable by this install's key ring only, and
/// never half-written.
/// </summary>
public sealed class RelayCredentialStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-store-" + Guid.NewGuid().ToString("N"));

    private static RelayCredentials Sample() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "Cabinet Ben Salah", "https://app.example.tn", "the-secret", Convert.ToBase64String(new byte[] { 1, 2, 3 }),
        new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Saved_Credentials_Load_Back_With_The_Same_Ring()
    {
        var ring = new EphemeralDataProtectionProvider();
        var store = new RelayCredentialStore(ring, _dir);

        store.Save(Sample());

        Assert.True(store.Exists);
        Assert.Equal(Sample(), new RelayCredentialStore(ring, _dir).TryLoad());
        Assert.Equal(new Uri("https://app.example.tn/api/"), store.TryLoad()!.ApiBase);
    }

    // The file alone opens nothing: neither the secret nor the private key is readable in it.
    [Fact]
    public void The_File_Holds_No_Secret_In_Clear()
    {
        var store = new RelayCredentialStore(new EphemeralDataProtectionProvider(), _dir);
        store.Save(Sample());

        var raw = File.ReadAllText(store.FilePath);

        Assert.DoesNotContain("the-secret", raw);
        Assert.DoesNotContain("Ben Salah", raw);
    }

    [Fact]
    public void Another_Ring_Or_A_Damaged_File_Reads_As_Not_Paired()
    {
        var store = new RelayCredentialStore(new EphemeralDataProtectionProvider(), _dir);
        store.Save(Sample());

        Assert.Null(new RelayCredentialStore(new EphemeralDataProtectionProvider(), _dir).TryLoad());

        File.WriteAllText(store.FilePath, "garbage");
        Assert.Null(store.TryLoad());
    }

    [Fact]
    public void A_Missing_File_Reads_As_Not_Paired_And_Delete_Is_Idempotent()
    {
        var store = new RelayCredentialStore(new EphemeralDataProtectionProvider(), _dir);

        Assert.False(store.Exists);
        Assert.Null(store.TryLoad());
        store.Delete();
        store.Save(Sample());
        store.Delete();
        Assert.False(store.Exists);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
