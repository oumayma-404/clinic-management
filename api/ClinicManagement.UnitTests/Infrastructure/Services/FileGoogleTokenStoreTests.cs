using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The file-backed Google refresh-token store (US-3 / FR-E3). Verifies it persists to the per-install
/// <c>.local/</c> file (never appsettings), round-trips awkward token characters, falls back to config
/// when no file exists (R-5), and — as a Singleton — reflects a save in a subsequent read even after an
/// earlier read cached a null (the cache-staleness guard).
/// </summary>
public sealed class FileGoogleTokenStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _tokenPath;

    public FileGoogleTokenStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cm-token-tests", Guid.NewGuid().ToString("N"));
        _tokenPath = Path.Combine(_dir, ".local", "google-refresh-token");
    }

    private FileGoogleTokenStore Store(string? configToken = null)
    {
        var settings = new Dictionary<string, string?> { ["GoogleCalendar:RefreshTokenPath"] = _tokenPath };
        if (configToken is not null)
        {
            settings["GoogleCalendar:RefreshToken"] = configToken;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new FileGoogleTokenStore(configuration, NullLogger<FileGoogleTokenStore>.Instance);
    }

    [Fact]
    public async Task Save_then_read_round_trips()
    {
        var store = Store();
        await store.SaveRefreshTokenAsync("1//abc-refresh");

        Assert.Equal("1//abc-refresh", store.GetRefreshToken());
    }

    [Fact]
    public void Missing_file_falls_back_to_configuration() // R-5 (Cloud / upgrade back-compat)
    {
        Assert.Equal("config-token", Store(configToken: "config-token").GetRefreshToken());
    }

    [Fact]
    public void Missing_file_and_no_config_returns_null()
    {
        Assert.Null(Store().GetRefreshToken());
    }

    [Fact]
    public async Task Save_writes_to_local_path_not_appsettings()
    {
        await Store().SaveRefreshTokenAsync("tok");

        Assert.True(File.Exists(_tokenPath));
        Assert.Contains(".local", _tokenPath);
        Assert.Equal("tok", (await File.ReadAllTextAsync(_tokenPath)).Trim());
    }

    [Fact]
    public async Task Token_with_quotes_and_backslashes_round_trips()
    {
        const string tricky = "1//ab\"c\\d/ef";
        var store = Store();
        await store.SaveRefreshTokenAsync(tricky);

        Assert.Equal(tricky, store.GetRefreshToken());   // from cache
        Assert.Equal(tricky, Store().GetRefreshToken()); // fresh instance reads from disk
    }

    [Fact]
    public async Task Read_after_write_on_same_instance_returns_new_token() // cache-staleness guard
    {
        var store = Store(configToken: "old-config");
        Assert.Equal("old-config", store.GetRefreshToken()); // primes cache with the config fallback (no file yet)

        await store.SaveRefreshTokenAsync("new-token");

        Assert.Equal("new-token", store.GetRefreshToken());
    }

    [Fact]
    public async Task Empty_token_save_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store().SaveRefreshTokenAsync("   "));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup — a leftover temp dir must never fail the test run
        }
    }
}
