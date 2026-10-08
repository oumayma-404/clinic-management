using System.Security.Cryptography;
using ClinicManagement.API.Controllers;
using ClinicManagement.API.Models;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.UnitTests.Common;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// <c>GET /api/relay/installer</c> (<c>clinic-pc-copy</c> D10): the server installer a PC de secours is set up and
/// updated from — only ever the one of the cloud's own build (lockstep), with the hash the Windows app requires.
/// </summary>
public sealed class RelayInstallerEndpointTests : IDisposable
{
    private const string Build = "20261008082101_AddRelayUninstalledAt+1.0.0+aaaaaaaaaaaa";
    private const string OlderBuild = "20261007223507_AddRelayErasedAt+1.0.0+bbbbbbbbbbbb";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "relay-installer-" + Guid.NewGuid().ToString("N"));

    public RelayInstallerEndpointTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp */ }
    }

    private byte[] Publish(string build, string file, string? manifestName = null, int size = 4096)
    {
        var bytes = RandomNumberGenerator.GetBytes(size);
        File.WriteAllBytes(Path.Combine(_folder, file), bytes);
        File.WriteAllText(Path.Combine(_folder, manifestName ?? $"relay-{Guid.NewGuid():N}.json"),
            $$"""{"build":"{{build}}","file":"{{file}}"}""");
        return bytes;
    }

    // ── Which installer is served ─────────────────────────────────────────────────────────────────

    [Fact]
    public void The_Installer_Of_This_Build_Is_Found_With_The_Hash_Of_Its_Own_Bytes()
    {
        var bytes = Publish(Build, "APEXA-PC-de-secours-aaaaaaaaaaaa.exe");

        var package = RelayInstallerPackage.Find(_folder, Build);

        Assert.NotNull(package);
        Assert.Equal("APEXA-PC-de-secours-aaaaaaaaaaaa.exe", package!.FileName);
        Assert.Equal(bytes.Length, package.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), package.Sha256);
    }

    // Lockstep: a PC set up from an installer one deploy behind could never copy.
    [Fact]
    public void Only_An_Older_Builds_Installer_Is_Not_Served()
    {
        Publish(OlderBuild, "APEXA-PC-de-secours-bbbbbbbbbbbb.exe");

        Assert.Null(RelayInstallerPackage.Find(_folder, Build));
    }

    // The last three builds stay published, so a rollback keeps its installer.
    [Fact]
    public void Among_Several_Builds_The_Clouds_Own_Is_Chosen()
    {
        Publish(OlderBuild, "APEXA-PC-de-secours-bbbbbbbbbbbb.exe");
        Publish(Build, "APEXA-PC-de-secours-aaaaaaaaaaaa.exe");

        Assert.Equal("APEXA-PC-de-secours-aaaaaaaaaaaa.exe", RelayInstallerPackage.Find(_folder, Build)!.FileName);
        Assert.Equal("APEXA-PC-de-secours-bbbbbbbbbbbb.exe", RelayInstallerPackage.Find(_folder, OlderBuild)!.FileName);
    }

    // A manifest names a file in its own folder and nothing else: the name is the whole attack surface.
    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("..\\outside.exe")]
    [InlineData("sub/inner.exe")]
    [InlineData("C:\\Windows\\notepad.exe")]
    [InlineData("installer.dll")]
    [InlineData("")]
    public void A_Manifest_Naming_Anything_But_A_Bare_Exe_Here_Is_Ignored(string file)
    {
        File.WriteAllText(Path.Combine(_folder, "relay-x.json"),
            System.Text.Json.JsonSerializer.Serialize(new { build = Build, file }));

        Assert.Null(RelayInstallerPackage.Find(_folder, Build));
    }

    [Fact]
    public void A_Manifest_Whose_File_Is_Missing_Or_Unreadable_Serves_Nothing()
    {
        File.WriteAllText(Path.Combine(_folder, "relay-gone.json"), $$"""{"build":"{{Build}}","file":"gone.exe"}""");
        File.WriteAllText(Path.Combine(_folder, "relay-broken.json"), "{ not json");

        Assert.Null(RelayInstallerPackage.Find(_folder, Build));
        Assert.Null(RelayInstallerPackage.Find(Path.Combine(_folder, "absent"), Build));
    }

    // ── The endpoint ──────────────────────────────────────────────────────────────────────────────

    private RelayPeerController Controller(DeploymentKind kind = DeploymentKind.HostedMultiTenant)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Relay:InstallerDirectory"] = _folder })
            .Build();
        var build = new Mock<IRelayBuildInfo>();
        build.SetupGet(b => b.Current).Returns(Build);
        return new RelayPeerController(Mock.Of<IMediator>(), DeploymentProfile.For(kind), configuration, build.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public void The_Installer_Is_Served_With_Its_Hash_And_Its_Build()
    {
        var bytes = Publish(Build, "APEXA-PC-de-secours-aaaaaaaaaaaa.exe");
        var controller = Controller();

        var result = Assert.IsType<PhysicalFileResult>(controller.Installer());

        Assert.Equal("APEXA-PC-de-secours-aaaaaaaaaaaa.exe", result.FileDownloadName);
        Assert.True(result.EnableRangeProcessing);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),
            controller.Response.Headers[RelayPeerController.InstallerSha256Header].ToString());
        Assert.Equal(Build, controller.Response.Headers[RelayPeerController.BuildHeader].ToString());
    }

    // The Windows app reads a 404 as « pas encore » — never as a connection problem.
    [Fact]
    public void No_Installer_Of_This_Build_Is_A_404_With_The_Sentence()
    {
        Publish(OlderBuild, "APEXA-PC-de-secours-bbbbbbbbbbbb.exe");

        var result = Assert.IsAssignableFrom<ObjectResult>(Controller().Installer());

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        var body = System.Text.Json.JsonSerializer.Serialize(result.Value, new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        Assert.Contains(RelayRefusals.InstallerUnavailable, body);
    }

    [Fact]
    public void A_Clinics_Own_Server_Has_No_Installer_To_Serve()
    {
        Publish(Build, "APEXA-PC-de-secours-aaaaaaaaaaaa.exe");

        Assert.IsType<NotFoundResult>(Controller(DeploymentKind.SelfHostedLan).Installer());
    }

    // ── The Windows app asks for exactly this ─────────────────────────────────────────────────────

    // ⚠️ The shell references no API assembly, so its URL, header name and « pas encore » sentence are copies; a
    // renamed route or header here would leave every install failing at the download with no error naming why.
    [Fact]
    public void The_Windows_App_Asks_For_This_Route_This_Header_And_Says_This_Sentence()
    {
        var shell = File.ReadAllText(Path.Combine(SolutionSources.Root().Parent!.FullName,
            "desktop", "ClinicManagement.DesktopShell", "RelayInstaller.cs"));
        var route = typeof(RelayPeerController).GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>().Single().Template;
        var action = typeof(RelayPeerController).GetMethod(nameof(RelayPeerController.Installer))!
            .GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>().Single().Template;

        Assert.Contains($"\"/{route}/{action}\"", shell);
        Assert.Contains($"\"{RelayPeerController.InstallerSha256Header}\"", shell);
        Assert.Contains($"\"{RelayRefusals.InstallerUnavailable}\"", shell);
    }
}
