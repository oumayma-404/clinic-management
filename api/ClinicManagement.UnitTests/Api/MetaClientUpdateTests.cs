using ClinicManagement.API.Controllers;
using ClinicManagement.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The client-update surface: which installer this server offers, and the Velopack feed the desktop shell reads
/// to update itself.
///
/// <para>
/// ⚠️ <b>The feed route's filename handling is the reason these exist.</b> It serves files out of a folder by a
/// name taken from the URL, and the file it serves is <b>executed on a clinic PC</b>. Everything else here could
/// be re-derived by reading the code; a traversal that slipped through could not be seen at all until it had
/// been used, so it is asserted rather than reasoned about.
/// </para>
///
/// <para>
/// ⚠️ Deliberately not testing what Velopack asks for: that contract belongs to Velopack, and pinning its
/// filenames here would turn its next version into a red test on a route that is *supposed* to be indifferent to
/// them. What is pinned is the shape the route accepts — a bare filename with one of the feed's extensions.
/// </para>
/// </summary>
public class MetaClientUpdateTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "apexa-feed-tests-" + Guid.NewGuid().ToString("N"));

    public MetaClientUpdateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    private MetaController Controller()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Clients:UpdateDirectory"] = _folder })
            .Build();

        return new MetaController(configuration, mediator: null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Clients:UpdateDirectory"] = _folder })
        .Build();

    private void Write(string name, string content = "x") =>
        File.WriteAllText(Path.Combine(_folder, name), content);

    // ── the feed route ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("RELEASES")]
    [InlineData("releases.win.json")]
    [InlineData("APEXA-1.2.1-delta.nupkg")]
    [InlineData("APEXA-win-Setup.exe")]
    public void ClientFeed_serves_the_files_a_velopack_feed_is_made_of(string name)
    {
        Write(name);
        Assert.IsType<PhysicalFileResult>(Controller().ClientFeed(name));
    }

    [Fact]
    public void ClientFeed_never_serves_a_file_from_outside_the_updates_folder()
    {
        // ⚠️ **This is the test that actually holds the guarantee, and the theory below does not.** The route has
        // THREE overlapping defences, and they were removed one at a time to find out which the tests can see:
        // dropping the `..`/`GetFileName` pair left everything green, and so did dropping the containment
        // re-check. Only with all three gone does this assertion fail — which also identified the one that
        // actually does the work, and it was not the obvious one: `Path.GetInvalidFileNameChars()` rejects `/`
        // and `\` on Windows, so it catches traversal before the `..` check is ever consulted.
        //
        // Defence in depth is the right shape here; the point of writing it down is that no single removal is
        // visible to the suite, so a future edit that "simplifies" two of the three is not caught by a green run.
        //
        // ⚠️ It is also why the theory's inputs are not evidence of much on their own: they 404 in a *correct*
        // route and in a `Path.GetFileName`-sanitising one alike, because the file they name is simply not there.
        // Behaviour worth asserting, but not a discriminator.
        var outside = Path.Combine(Directory.GetParent(_folder)!.FullName, "apexa-secret-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(outside, "{\"secret\":true}");

        try
        {
            var name = Path.GetFileName(outside);
            foreach (var spelling in new[] { $"../{name}", $"..\\{name}", $"./../{name}" })
            {
                Assert.IsType<NotFoundResult>(Controller().ClientFeed(spelling));
            }
        }
        finally
        {
            try { File.Delete(outside); } catch { /* best effort */ }
        }
    }

    [Theory]
    // Traversal and junk, in the spellings a caller really sends. These assert BEHAVIOUR; the discriminating
    // test is the one above.
    [InlineData("../appsettings.json")]
    [InlineData("..\\appsettings.json")]
    [InlineData("../../etc/passwd")]
    [InlineData("sub/RELEASES")]
    [InlineData("sub\\RELEASES")]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    [InlineData("/etc/passwd")]
    // Right shape, wrong extension: the folder may sit beside things that are not ours to hand out.
    [InlineData("appsettings.json.bak")]
    [InlineData("notes.txt")]
    [InlineData("db-credentials")]
    // Degenerate input.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    public void ClientFeed_refuses_anything_that_is_not_a_bare_feed_filename(string name)
    {
        Assert.IsType<NotFoundResult>(Controller().ClientFeed(name));
    }

    [Fact]
    public void ClientFeed_404s_a_name_that_is_allowed_but_absent()
    {
        Assert.IsType<NotFoundResult>(Controller().ClientFeed("APEXA-9.9.9-full.nupkg"));
    }

    [Fact]
    public void ClientFeed_404s_when_the_deployment_ships_no_updates_folder()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Clients:UpdateDirectory"] = Path.Combine(_folder, "does-not-exist"),
            })
            .Build();

        var controller = new MetaController(configuration, mediator: null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        Assert.IsType<NotFoundResult>(controller.ClientFeed("RELEASES"));
    }

    // ── which package the requirements payload advertises ────────────────────────────────────────────────

    [Fact]
    public void No_package_is_offered_when_the_folder_is_empty()
    {
        Assert.Null(ClientUpdatePackage.Resolve(Config(), _folder));
    }

    [Fact]
    public void A_velopack_feed_is_offered_at_the_version_its_manifest_names()
    {
        Write("APEXA-win-Setup.exe", "setup");
        Write("releases.win.json",
            """{"Assets":[{"Version":"1.2.0","Type":"Full"},{"Version":"1.2.1","Type":"Delta"},{"Version":"1.2.1","Type":"Full"}]}""");

        var package = ClientUpdatePackage.Resolve(Config(), _folder);

        Assert.NotNull(package);
        // ⚠️ The HIGHEST asset version, not the first: the manifest lists full and delta packages per release and
        // retains older ones, while the setup on disk is always the newest. A wrong version here is published as
        // `currentShellVersion` and either hides a real update or advertises one that does not exist.
        Assert.Equal("1.2.1", package!.Version);
        Assert.Equal("APEXA-win-Setup.exe", package.FileName);
    }

    [Fact]
    public void A_velopack_setup_with_no_manifest_is_not_offered()
    {
        // A half-published feed: say nothing rather than guess a version.
        Write("APEXA-win-Setup.exe", "setup");

        Assert.Null(ClientUpdatePackage.Resolve(Config(), _folder));
    }

    [Fact]
    public void The_velopack_feed_wins_over_a_legacy_inno_setup()
    {
        // A clinic mid-migration holds both. The answer it should be given is the self-updating one.
        Write("ClinicManagementClientSetup-1.1.3.exe", "inno");
        Write("APEXA-win-Setup.exe", "velopack");
        Write("releases.win.json", """{"Assets":[{"Version":"1.2.1","Type":"Full"}]}""");

        var package = ClientUpdatePackage.Resolve(Config(), _folder);

        Assert.NotNull(package);
        Assert.Equal("APEXA-win-Setup.exe", package!.FileName);
        Assert.Equal("1.2.1", package.Version);
    }

    [Fact]
    public void A_legacy_inno_setup_alone_is_still_offered_for_a_first_lan_install()
    {
        Write("ClinicManagementClientSetup-1.1.3.exe", "inno");

        var package = ClientUpdatePackage.Resolve(Config(), _folder);

        Assert.NotNull(package);
        Assert.Equal("1.1.3", package!.Version);
    }

    [Fact]
    public void The_newest_legacy_setup_wins_by_version_not_by_write_time()
    {
        Write("ClinicManagementClientSetup-1.1.3.exe", "newer version");
        Write("ClinicManagementClientSetup-1.0.9.exe", "older version");
        // Touched later, so a write-time choice would pick the wrong one.
        File.SetLastWriteTimeUtc(
            Path.Combine(_folder, "ClinicManagementClientSetup-1.0.9.exe"),
            DateTime.UtcNow.AddHours(1));

        Assert.Equal("1.1.3", ClientUpdatePackage.Resolve(Config(), _folder)!.Version);
    }

    [Fact]
    public void The_published_hash_is_of_the_bytes_that_will_be_served()
    {
        Write("ClinicManagementClientSetup-2.0.0.exe", "the actual bytes");

        var package = ClientUpdatePackage.Resolve(Config(), _folder)!;
        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(Path.Combine(_folder, "ClinicManagementClientSetup-2.0.0.exe"))));

        // The shell refuses a download whose digest does not match this, so a hash of anything else is an update
        // nobody can install.
        Assert.Equal(expected, package.Sha256);
    }

    [Fact]
    public void A_replaced_file_is_re_hashed_rather_than_served_from_the_cache()
    {
        Write("ClinicManagementClientSetup-2.0.0.exe", "first");
        var first = ClientUpdatePackage.Resolve(Config(), _folder)!.Sha256;

        var path = Path.Combine(_folder, "ClinicManagementClientSetup-2.0.0.exe");
        File.WriteAllText(path, "second, longer content");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

        Assert.NotEqual(first, ClientUpdatePackage.Resolve(Config(), _folder)!.Sha256);
    }
}
