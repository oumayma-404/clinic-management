using ClinicManagement.API.Controllers;
using ClinicManagement.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The Android half of the client-update surface (<c>android-in-app-update</c>): which APK this server offers,
/// what <c>client-requirements</c> says about it, and the route that serves it.
///
/// <para>⚠️ The load-bearing cases are the two the phone cannot recover from: a hash that is not of the served
/// bytes (the shell refuses the file, so nobody can update) and a <c>.part</c> upload being offered (Android is
/// handed half an APK).</para>
/// </summary>
public class AndroidUpdatePackageTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "apexa-apk-tests-" + Guid.NewGuid().ToString("N"));

    public AndroidUpdatePackageTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    private IConfiguration Config(Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?> { ["Clients:UpdateDirectory"] = _folder };
        foreach (var (key, value) in extra ?? new()) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private MetaController Controller(Dictionary<string, string?>? extra = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("app.apexa.tn");

        return new MetaController(Config(extra), mediator: null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private void Write(string name, string content = "apk") =>
        File.WriteAllText(Path.Combine(_folder, name), content);

    private static ClientRequirements Requirements(IActionResult result) =>
        Assert.IsType<ClientRequirements>(Assert.IsType<OkObjectResult>(result).Value);

    // ── which APK is offered ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void No_apk_is_offered_when_the_folder_is_empty()
    {
        Assert.Null(AndroidUpdatePackage.Resolve(Config(), _folder));
    }

    [Fact]
    public void The_newest_apk_wins_by_version_not_by_write_time()
    {
        Write("APEXA-1.10.0.apk", "newer");
        Write("APEXA-1.9.3.apk", "older");
        File.SetLastWriteTimeUtc(Path.Combine(_folder, "APEXA-1.9.3.apk"), DateTime.UtcNow.AddHours(1));

        var package = AndroidUpdatePackage.Resolve(Config(), _folder);

        // 1.10.0 > 1.9.3 as versions; as strings it would lose.
        Assert.Equal("1.10.0", package!.Version);
        Assert.Equal("APEXA-1.10.0.apk", package.FileName);
    }

    [Theory]
    [InlineData("APEXA-1.3.0.apk.part")]   // an upload still in flight
    [InlineData("APEXA-latest.apk")]
    [InlineData("app-release.apk")]
    [InlineData("APEXA-win-Setup.exe")]
    [InlineData("APEXA-1.3.0-full.nupkg")]
    public void A_file_that_is_not_a_versioned_apk_is_never_offered(string name)
    {
        Write(name);

        Assert.Null(AndroidUpdatePackage.Resolve(Config(), _folder));
    }

    [Fact]
    public void The_published_hash_is_of_the_bytes_that_will_be_served()
    {
        Write("APEXA-1.2.0.apk", "the actual apk bytes");

        var package = AndroidUpdatePackage.Resolve(Config(), _folder)!;
        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(_folder, "APEXA-1.2.0.apk"))));

        Assert.Equal(expected, package.Sha256);
    }

    [Fact]
    public void A_replaced_apk_is_re_hashed_rather_than_served_from_the_cache()
    {
        Write("APEXA-1.2.0.apk", "first");
        var first = AndroidUpdatePackage.Resolve(Config(), _folder)!.Sha256;

        var path = Path.Combine(_folder, "APEXA-1.2.0.apk");
        File.WriteAllText(path, "second, longer content");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

        Assert.NotEqual(first, AndroidUpdatePackage.Resolve(Config(), _folder)!.Sha256);
    }

    // ── what client-requirements says ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Requirements_advertise_the_apk_with_this_servers_own_url_and_hash()
    {
        Write("APEXA-1.2.0.apk", "bytes");

        var requirements = Requirements(Controller().ClientRequirements());
        var package = AndroidUpdatePackage.Resolve(Config(), _folder)!;

        Assert.Equal("1.2.0", requirements.Android.CurrentVersion);
        Assert.Equal("https://app.apexa.tn/api/meta/client-download/android", requirements.Android.DownloadUrl);
        Assert.Equal(package.Sha256, requirements.Android.Sha256);
        // The 1.1.1 phones' wall opens this field in the browser: their one way onto the updater build.
        Assert.Equal(requirements.Android.DownloadUrl, requirements.StoreUrls.Android);
    }

    [Fact]
    public void The_android_block_never_takes_the_windows_version()
    {
        // The defect the split exists for: the shared `currentShellVersion` is the desktop's.
        Write("APEXA-win-Setup.exe", "setup");
        Write("releases.win.json", """{"Assets":[{"Version":"1.3.0","Type":"Full"}]}""");
        Write("APEXA-1.2.0.apk", "bytes");

        var requirements = Requirements(Controller().ClientRequirements());

        Assert.Equal("1.3.0", requirements.CurrentShellVersion);
        Assert.Equal("1.2.0", requirements.Android.CurrentVersion);
    }

    [Fact]
    public void With_no_apk_the_android_block_is_what_the_operator_configured()
    {
        var requirements = Requirements(Controller().ClientRequirements());

        Assert.Equal(string.Empty, requirements.Android.CurrentVersion);
        Assert.Equal(string.Empty, requirements.Android.DownloadUrl);
        Assert.Equal(string.Empty, requirements.Android.Sha256);
        Assert.Equal(string.Empty, requirements.StoreUrls.Android);
    }

    [Fact]
    public void An_operator_configured_url_wins_and_our_hash_does_not_travel_with_it()
    {
        Write("APEXA-1.2.0.apk", "bytes");

        var requirements = Requirements(Controller(new()
        {
            ["Clients:Android:DownloadUrl"] = "https://mirror.example/apexa.apk",
            ["Clients:Android:Sha256"] = "ABC123",
            ["Clients:Android:CurrentVersion"] = "1.1.9",
        }).ClientRequirements());

        Assert.Equal("https://mirror.example/apexa.apk", requirements.Android.DownloadUrl);
        Assert.Equal("ABC123", requirements.Android.Sha256);
        Assert.Equal("1.1.9", requirements.Android.CurrentVersion);
        Assert.Equal("https://mirror.example/apexa.apk", requirements.StoreUrls.Android);
    }

    [Fact]
    public void A_configured_store_url_is_never_overwritten()
    {
        Write("APEXA-1.2.0.apk", "bytes");

        var requirements = Requirements(Controller(new()
        {
            ["Clients:StoreUrls:Android"] = "https://play.google.com/store/apps/details?id=com.clinicmanagement.shell",
        }).ClientRequirements());

        Assert.StartsWith("https://play.google.com/", requirements.StoreUrls.Android);
        Assert.Equal("https://app.apexa.tn/api/meta/client-download/android", requirements.Android.DownloadUrl);
    }

    // ── the download route ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_download_serves_the_newest_apk_as_an_android_package()
    {
        Write("APEXA-1.2.0.apk", "old");
        Write("APEXA-1.2.1.apk", "new");

        var file = Assert.IsType<PhysicalFileResult>(Controller().ClientDownloadAndroid());

        Assert.EndsWith("APEXA-1.2.1.apk", file.FileName);
        Assert.Equal("APEXA-1.2.1.apk", file.FileDownloadName);
        Assert.Equal("application/vnd.android.package-archive", file.ContentType);
        Assert.True(file.EnableRangeProcessing);
    }

    [Fact]
    public void The_download_404s_with_the_canonical_body_when_there_is_no_apk()
    {
        var result = Assert.IsType<NotFoundObjectResult>(Controller().ClientDownloadAndroid());

        Assert.Contains("error", result.Value!.GetType().GetProperties().Select(p => p.Name));
    }
}
