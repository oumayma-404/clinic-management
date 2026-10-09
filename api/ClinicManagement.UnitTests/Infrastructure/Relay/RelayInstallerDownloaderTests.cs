using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Xml.Linq;
using ClinicManagement.Infrastructure.Relay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>
/// The PC de secours fetching the cloud's installer (<c>clinic-pc-copy</c> D10b) and handing it to Windows. The
/// download cases are about the file left on disk — a resumed part must only ever be a prefix of the right file —
/// and the launcher cases about what the scheduled task and the installer are actually told.
/// </summary>
public sealed class RelayInstallerDownloaderTests : IDisposable
{
    private const string Build = "20261008082101_AddRelayUninstalledAt+1.0.0+aaaaaaaaaaaa";
    private static readonly byte[] Installer = { 10, 20, 30, 40, 50, 60 };
    private static readonly string Sha256 = Convert.ToHexString(SHA256.HashData(Installer));

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-download-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _handler = new();

    public RelayInstallerDownloaderTests() => Directory.CreateDirectory(_dir);

    private string Part => Path.Combine(_dir, "setup.exe.part");

    private Task<RelayInstallerFetch> FetchAsync(string build = Build) =>
        new RelayInstallerDownloader(new HttpClient(_handler), new Uri("https://cloud.example.tn/api/"))
            .FetchAsync(Part, build, CancellationToken.None);

    private static HttpResponseMessage Served(HttpStatusCode status, byte[] body, string build = Build, string? sha = null)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        response.Headers.Add(RelayInstallerDownloader.BuildHeader, build);
        if (sha != "")
        {
            response.Headers.Add(RelayInstallerDownloader.Sha256Header, sha ?? Sha256);
        }

        return response;
    }

    [Fact]
    public async Task A_First_Download_Writes_The_Whole_File_And_Hands_Back_The_Hash()
    {
        _handler.Respond = _ => Served(HttpStatusCode.OK, Installer);

        var fetch = await FetchAsync();

        Assert.Equal(RelayInstallerFetchStatus.Complete, fetch.Status);
        Assert.Equal(Sha256, fetch.Sha256);
        Assert.Equal(Installer, await File.ReadAllBytesAsync(Part));
        Assert.Equal("https://cloud.example.tn/api/relay/installer", _handler.Requests.Single().RequestUri!.ToString());
        Assert.Null(_handler.Requests.Single().Headers.Range);
    }

    [Fact]
    public async Task A_Cut_Download_Asks_For_The_Rest_And_Appends_It()
    {
        await File.WriteAllBytesAsync(Part, Installer[..2]);
        _handler.Respond = _ =>
        {
            var response = Served(HttpStatusCode.PartialContent, Installer[2..]);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(2, Installer.Length - 1, Installer.Length);
            return response;
        };

        var fetch = await FetchAsync();

        Assert.Equal(RelayInstallerFetchStatus.Complete, fetch.Status);
        Assert.Equal(2, _handler.Requests.Single().Headers.Range!.Ranges.Single().From);
        Assert.Equal(Installer, await File.ReadAllBytesAsync(Part));
    }

    // A cloud that ignores the range sends the whole file: what was on disk must not stay in front of it.
    [Fact]
    public async Task A_Whole_Answer_To_A_Resume_Replaces_What_Was_On_Disk()
    {
        await File.WriteAllBytesAsync(Part, new byte[] { 99, 99, 99 });
        _handler.Respond = _ => Served(HttpStatusCode.OK, Installer);

        await FetchAsync();

        Assert.Equal(Installer, await File.ReadAllBytesAsync(Part));
    }

    [Fact]
    public async Task A_Range_The_Cloud_Cannot_Serve_Starts_Again_From_The_First_Byte()
    {
        await File.WriteAllBytesAsync(Part, new byte[50]);
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);

        var fetch = await FetchAsync();

        Assert.Equal(RelayInstallerFetchStatus.Interrupted, fetch.Status);
        Assert.False(File.Exists(Part));
    }

    [Fact]
    public async Task Nothing_Published_Reads_As_Not_Yet()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        Assert.Equal(RelayInstallerFetchStatus.NotPublished, (await FetchAsync()).Status);
    }

    [Fact]
    public async Task Another_Builds_Installer_Writes_Nothing()
    {
        _handler.Respond = _ => Served(HttpStatusCode.OK, Installer, build: "20261009_Other+1.0.0+bbbbbbbbbbbb");

        var fetch = await FetchAsync();

        Assert.Equal(RelayInstallerFetchStatus.OtherBuild, fetch.Status);
        Assert.False(File.Exists(Part));
    }

    // No hash, no run: an installer without one is not worth downloading.
    [Fact]
    public async Task An_Installer_Without_Its_Hash_Writes_Nothing()
    {
        _handler.Respond = _ => Served(HttpStatusCode.OK, Installer, sha: "");

        var fetch = await FetchAsync();

        Assert.Equal(RelayInstallerFetchStatus.Interrupted, fetch.Status);
        Assert.False(File.Exists(Part));
    }

    [Fact]
    public async Task An_Unreachable_Cloud_Is_An_Interruption_Not_An_Exception()
    {
        _handler.Respond = _ => throw new HttpRequestException("Aucun hôte.");

        var fetch = await FetchAsync();

        Assert.Equal(RelayInstallerFetchStatus.Interrupted, fetch.Status);
    }

    // ---- what Windows is told -----------------------------------------------------------------------------------

    // A path with spaces, quotes in the arguments and an ampersand all survive the XML exactly.
    [Fact]
    public void The_Task_Runs_The_Installer_As_System_With_Exactly_Its_Arguments()
    {
        const string command = @"C:\Program Files\APEXA\api\.local\updates\APEXA-PC-de-secours-mise-a-jour.exe";
        var arguments = RelayUpdater.Arguments(@"C:\Program Files\APEXA\r&d.txt", @"C:\Program Files\APEXA\logs\u.log");

        var xml = XDocument.Parse(ScheduledTaskUpdateLauncher.TaskXml(command, arguments, @"C:\Program Files\APEXA\api\.local\updates"));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Equal(ScheduledTaskUpdateLauncher.SystemSid, xml.Descendants(ns + "UserId").Single().Value);
        Assert.Equal("HighestAvailable", xml.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal(command, xml.Descendants(ns + "Command").Single().Value);
        Assert.Equal(arguments, xml.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("false", xml.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Empty(xml.Descendants(ns + "Triggers"));
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-16\"?>",
            ScheduledTaskUpdateLauncher.TaskXml(command, arguments, "C:\\"));
    }

    [Fact]
    public void The_Installed_Pc_Uses_The_Scheduled_Task_And_Only_A_Rig_Says_Otherwise()
    {
        Assert.IsType<ScheduledTaskUpdateLauncher>(RelayUpdateLaunchers.For(null, _dir, NullLogger.Instance));
        Assert.IsType<ScheduledTaskUpdateLauncher>(RelayUpdateLaunchers.For("schtasks", _dir, NullLogger.Instance));
        Assert.IsType<DirectProcessUpdateLauncher>(RelayUpdateLaunchers.For(" Direct ", _dir, NullLogger.Instance));
    }

    // Every /NAME= the update passes is one the installer reads, and none of them re-pairs the PC.
    [Fact]
    public void The_Installer_Reads_Every_Argument_The_Update_Passes()
    {
        var iss = File.ReadAllText(InstallerScript());
        var arguments = RelayUpdater.Arguments(@"C:\r.txt", @"C:\l.log");

        Assert.Contains("'/RELAY'", iss);
        Assert.Contains("{param:RESULTFILE|", iss);
        Assert.Contains("\"/LOG=", arguments); // Inno's own switch — the log of a run nobody watched
        Assert.DoesNotContain("PAIRFILE", arguments);
        Assert.DoesNotContain("CLOUD", arguments);
    }

    private static string InstallerScript([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!,
            "..", "..", "..", "..", "packaging", "setup", "clinic-setup.iss"));
        Assert.True(File.Exists(path), $"clinic-setup.iss not found at {path}");
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    // ---- in pieces: one connection from the cloud to a cabinet ran at ~100 KB/s, several fill the line -------------

    private static readonly byte[] Big = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();

    private HttpResponseMessage ServeBig(HttpRequestMessage request, Func<int, bool>? fail = null)
    {
        if (request.Headers.Range?.Ranges.Single() is not { } range)
        {
            var whole = Served(HttpStatusCode.OK, Big);
            whole.Headers.AcceptRanges.Add("bytes");
            return whole;
        }

        var from = (int)range.From!.Value;
        var to = (int)range.To!.Value;
        if (fail?.Invoke(from) == true)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        var piece = Served(HttpStatusCode.PartialContent, Big[from..(to + 1)]);
        piece.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, Big.Length);
        return piece;
    }

    private Task<RelayInstallerFetch> FetchInPiecesAsync() =>
        new RelayInstallerDownloader(new HttpClient(_handler), new Uri("https://cloud.example.tn/api/"), parallelAbove: 50, pieceBytes: 16)
            .FetchAsync(Part, Build, CancellationToken.None);

    [Fact]
    public async Task A_Big_Installer_Comes_In_Pieces_And_Is_Whole()
    {
        _handler.Respond = r => ServeBig(r);

        var fetch = await FetchInPiecesAsync();

        Assert.Equal(RelayInstallerFetchStatus.Complete, fetch.Status);
        Assert.Equal(Big, await File.ReadAllBytesAsync(Part));
        Assert.Equal(1 + 7, _handler.Requests.Count); // the first answer, then 7 pieces of 16 bytes
        Assert.False(File.Exists(Part + ".pieces"));
    }

    // A cut keeps the pieces already on disk: the next attempt asks only for the missing ones.
    [Fact]
    public async Task A_Cut_Download_In_Pieces_Resumes_From_The_Pieces_It_Has()
    {
        _handler.Respond = r => ServeBig(r, fail: from => from >= 64);
        Assert.Equal(RelayInstallerFetchStatus.Interrupted, (await FetchInPiecesAsync()).Status);
        Assert.True(File.Exists(Part + ".pieces"));

        _handler.Requests.Clear();
        _handler.Respond = r => ServeBig(r);
        var fetch = await FetchInPiecesAsync();

        Assert.Equal(RelayInstallerFetchStatus.Complete, fetch.Status);
        Assert.Equal(Big, await File.ReadAllBytesAsync(Part));
        Assert.All(_handler.Requests, r => Assert.True(r.Headers.Range!.Ranges.Single().From >= 64));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }
}
