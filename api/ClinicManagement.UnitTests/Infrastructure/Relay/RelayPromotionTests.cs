using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ClinicManagement.Infrastructure.Relay;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>
/// Promoting a PC de secours into the cabinet's local server (<c>clinic-pc-copy</c> D11, AC-9.3). The code cases are
/// about who can issue one and for which PC; the promoter cases are about what a refusal leaves behind — nothing — and
/// what a promotion writes, in which order.
/// </summary>
public sealed class RelayPromotionTests : IDisposable
{
    private static readonly Guid RelayId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    private const string RelayLayer = """
        {
          "ConnectionStrings": { "DefaultConnection": "Host=localhost;Database=clinic" },
          "Deployment": { "Profile": "ClinicRelay" },
          "FileStorage": { "BasePath": "C:\\Program Files\\APEXA\\api\\Files" }
        }
        """;

    private readonly (string PrivateKeyPem, string PublicKey) _vendor = RelayPromotionCode.NewVendorKey();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-promote-" + Guid.NewGuid().ToString("N"));
    private readonly List<RelayPromotionClaim> _journal = new();
    private bool _cloudServes;

    public RelayPromotionTests() => Directory.CreateDirectory(_dir);

    private string Layer => Path.Combine(_dir, "appsettings.Install.json");

    private RelayFollowerStateStore States => new(_dir);

    private static RelayCredentials Credentials => new(RelayId, ClinicId, "Cabinet Dr. Ben Salah", "https://cloud.example.tn",
        "secret", Convert.ToBase64String(new byte[32]), T0.AddDays(-30));

    private string Code(Guid? relay = null, Guid? clinic = null, DateTime? issued = null, string? key = null) =>
        RelayPromotionCode.Sign(key ?? _vendor.PrivateKeyPem, relay ?? RelayId, clinic ?? ClinicId, issued ?? T0,
            RelayPromotionCode.DefaultValidity);

    private RelayPromoter Promoter() => new(
        _vendor.PublicKey,
        _ => Task.FromResult(_cloudServes),
        (claim, _, _) => { _journal.Add(claim); return Task.CompletedTask; },
        Layer,
        States,
        _dir);

    private Task<RelayPromotionResult> PromoteAsync(string code, DateTime? now = null) =>
        Promoter().PromoteAsync(Credentials, code, now ?? T0.AddMinutes(5), CancellationToken.None);

    private void AssertNothingChanged()
    {
        Assert.Empty(_journal);
        Assert.Equal(RelayLayer, File.ReadAllText(Layer));
        Assert.Null(RelayPromoter.LoadRecord(_dir));
        Assert.False(States.Load().Released);
        Assert.Empty(Directory.GetFiles(_dir, "*.bak-*"));
    }

    // ---- the code -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_Code_The_Vendor_Signed_For_This_Pc_Is_Valid()
    {
        var (verdict, claim) = RelayPromotionCode.Verify(Code(), _vendor.PublicKey, RelayId, ClinicId, T0.AddDays(1));

        Assert.Equal(RelayPromotionVerdict.Valid, verdict);
        Assert.Equal(RelayId, claim!.RelayId);
        Assert.Equal(T0 + RelayPromotionCode.DefaultValidity, claim.ExpiresAtUtc);
    }

    [Fact]
    public void A_Code_Signed_With_Any_Other_Key_Is_Refused()
    {
        var other = RelayPromotionCode.NewVendorKey();

        Assert.Equal(RelayPromotionVerdict.BadSignature,
            RelayPromotionCode.Verify(Code(key: other.PrivateKeyPem), _vendor.PublicKey, RelayId, ClinicId, T0).Verdict);
    }

    // The payload is signed byte for byte: re-pointing a real code at another PC breaks it.
    [Fact]
    public void A_Code_Whose_Payload_Was_Edited_Is_Refused()
    {
        var parts = Code(relay: Guid.NewGuid()).Split('.');
        var edited = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Pad(parts[1]))))!;
        edited["relayId"] = RelayId.ToString();
        var tampered = parts[0] + "." + Url(Encoding.UTF8.GetBytes(edited.ToJsonString())) + "." + parts[2];

        Assert.Equal(RelayPromotionVerdict.BadSignature,
            RelayPromotionCode.Verify(tampered, _vendor.PublicKey, RelayId, ClinicId, T0).Verdict);
    }

    [Fact]
    public void A_Code_For_Another_Pc_Or_Another_Cabinet_Is_Refused()
    {
        Assert.Equal(RelayPromotionVerdict.OtherPc,
            RelayPromotionCode.Verify(Code(relay: Guid.NewGuid()), _vendor.PublicKey, RelayId, ClinicId, T0).Verdict);
        Assert.Equal(RelayPromotionVerdict.OtherPc,
            RelayPromotionCode.Verify(Code(clinic: Guid.NewGuid()), _vendor.PublicKey, RelayId, ClinicId, T0).Verdict);
    }

    [Fact]
    public void A_Code_Is_Short_Lived_And_Not_From_The_Future()
    {
        var code = Code();

        Assert.Equal(RelayPromotionVerdict.Valid,
            RelayPromotionCode.Verify(code, _vendor.PublicKey, RelayId, ClinicId, T0 + RelayPromotionCode.DefaultValidity).Verdict);
        Assert.Equal(RelayPromotionVerdict.Expired,
            RelayPromotionCode.Verify(code, _vendor.PublicKey, RelayId, ClinicId, T0 + RelayPromotionCode.DefaultValidity + TimeSpan.FromSeconds(1)).Verdict);
        Assert.Equal(RelayPromotionVerdict.Expired,
            RelayPromotionCode.Verify(code, _vendor.PublicKey, RelayId, ClinicId, T0 - RelayPromotionCode.ClockSkew - TimeSpan.FromSeconds(1)).Verdict);
        Assert.Throws<ArgumentOutOfRangeException>(() => RelayPromotionCode.Sign(_vendor.PrivateKeyPem, RelayId, ClinicId, T0,
            RelayPromotionCode.MaxValidity + TimeSpan.FromDays(1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("n'importe quoi")]
    [InlineData("APEXA-PROMO-1.abc")]
    [InlineData("AUTRE-1.abc.def")]
    [InlineData("APEXA-PROMO-1.!!!.???")]
    public void Anything_Else_Is_Unreadable(string code)
    {
        Assert.Equal(RelayPromotionVerdict.Malformed,
            RelayPromotionCode.Verify(code, _vendor.PublicKey, RelayId, ClinicId, T0).Verdict);
    }

    // The key compiled into every PC is a real P-256 public key — a placeholder would make every promotion impossible.
    [Fact]
    public void The_Built_In_Vendor_Key_Is_A_P256_Public_Key()
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(RelayPromotionCode.VendorPublicKey), out var read);

        Assert.Equal(Convert.FromBase64String(RelayPromotionCode.VendorPublicKey).Length, read);
        Assert.Equal(256, key.KeySize);
    }

    [Fact]
    public void A_Private_Key_Names_Its_Own_Public_Half()
    {
        Assert.Equal(_vendor.PublicKey, RelayPromotionCode.PublicKeyOf(_vendor.PrivateKeyPem));
    }

    // ---- the promotion ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Promotion_Records_Then_Switches_The_Kind_And_Releases_The_Copy()
    {
        File.WriteAllText(Layer, RelayLayer);

        var result = await PromoteAsync(Code());

        Assert.Equal(RelayPromotionResult.Promoted, result.ExitCode);
        Assert.Contains("Cabinet Dr. Ben Salah", result.Sentence);
        Assert.Equal(RelayId, Assert.Single(_journal).RelayId);
        var layer = JsonNode.Parse(File.ReadAllText(Layer))!;
        Assert.Equal("SelfHostedLan", layer["Deployment"]!["Profile"]!.GetValue<string>());
        Assert.Equal("Host=localhost;Database=clinic", layer["ConnectionStrings"]!["DefaultConnection"]!.GetValue<string>());
        Assert.Equal(@"C:\Program Files\APEXA\api\Files", layer["FileStorage"]!["BasePath"]!.GetValue<string>());
        Assert.Equal(RelayLayer, File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "*.bak-*"))));
        Assert.True(States.Load().Released);
        Assert.Equal(T0.AddMinutes(5), RelayPromoter.LoadRecord(_dir)!.PromotedAtUtc);
    }

    // Found live: a cabinet with no name read « du cabinet «  » ».
    [Theory]
    [InlineData("Cabinet Dr. Ben Salah", "du cabinet « Cabinet Dr. Ben Salah »")]
    [InlineData("", "du cabinet")]
    [InlineData("   ", "du cabinet")]
    [InlineData(null, "du cabinet")]
    public void A_Cabinet_Without_A_Name_Is_Not_Quoted_Empty(string? name, string expected)
    {
        Assert.Equal(expected, RelayPromoter.OfTheCabinet(name));
    }

    // The installer reads a PC de secours off the substring 'ClinicRelay' anywhere in the file: a promoted file must
    // carry none, or the next installer run would treat the cabinet's new server as a copy.
    [Fact]
    public async Task A_Promoted_Install_Layer_Reads_As_A_Cabinet_Server_To_The_Installer()
    {
        File.WriteAllText(Layer, RelayLayer);
        var iss = File.ReadAllText(InstallerScript());

        await PromoteAsync(Code());

        Assert.Contains("Pos('ClinicRelay', String(Cfg)) > 0", iss);
        Assert.DoesNotContain("ClinicRelay", File.ReadAllText(Layer));
    }

    [Fact]
    public async Task A_Refused_Code_Changes_Nothing()
    {
        File.WriteAllText(Layer, RelayLayer);

        var wrongPc = await PromoteAsync(Code(relay: Guid.NewGuid()));
        var expired = await PromoteAsync(Code(), now: T0.AddDays(30));
        var unreadable = await PromoteAsync("APEXA-PROMO-1.x.y");

        Assert.All(new[] { wrongPc, expired, unreadable }, r => Assert.Equal(RelayPromotionResult.CodeRefused, r.ExitCode));
        Assert.Equal(RelayPromotionCode.Sentence(RelayPromotionVerdict.OtherPc), wrongPc.Sentence);
        AssertNothingChanged();
    }

    // Two writable copies of one cabinet is what the whole feature prevents: a cloud that serves wins over any code.
    [Fact]
    public async Task A_Cloud_That_Still_Serves_Refuses_The_Promotion_And_Changes_Nothing()
    {
        File.WriteAllText(Layer, RelayLayer);
        _cloudServes = true;

        var result = await PromoteAsync(Code());

        Assert.Equal(RelayPromotionResult.CloudAnswers, result.ExitCode);
        AssertNothingChanged();
    }

    [Fact]
    public async Task A_Pc_Whose_Layer_Is_Not_A_Relay_Is_Left_Alone()
    {
        const string server = """{ "ConnectionStrings": { "DefaultConnection": "x" } }""";
        File.WriteAllText(Layer, server);

        var result = await PromoteAsync(Code());

        Assert.Equal(RelayPromotionResult.CannotRun, result.ExitCode);
        Assert.Equal(server, File.ReadAllText(Layer));
        Assert.Empty(_journal);
    }

    [Fact]
    public async Task A_Journal_That_Cannot_Be_Written_Stops_Before_The_Kind_Changes()
    {
        File.WriteAllText(Layer, RelayLayer);
        var promoter = new RelayPromoter(_vendor.PublicKey, _ => Task.FromResult(false),
            (_, _, _) => throw new InvalidOperationException("base injoignable"), Layer, States, _dir);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            promoter.PromoteAsync(Credentials, Code(), T0.AddMinutes(5), CancellationToken.None));

        AssertNothingChanged();
    }

    // ---- is the cloud gone? -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"status":"Healthy","checks":{"database":"Healthy","storage":"Healthy"}}""", true)]
    [InlineData(HttpStatusCode.OK, """{"status":"Degraded","checks":{"database":"Healthy","storage":"Unhealthy"}}""", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"status":"Unhealthy","checks":{"database":"Unhealthy"}}""", false)]
    [InlineData(HttpStatusCode.OK, "<html>Ce domaine est à vendre</html>", false)]
    [InlineData(HttpStatusCode.OK, """{"status":"ok"}""", false)]
    [InlineData(HttpStatusCode.NotFound, "", false)]
    public async Task Only_The_Clouds_Own_Healthy_Answer_Counts_As_Serving(HttpStatusCode status, string body, bool serves)
    {
        var handler = new Handler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

        Assert.Equal(serves, await RelayPromoter.CloudServesAsync(new HttpClient(handler), "https://cloud.example.tn/", CancellationToken.None));
        Assert.Equal("https://cloud.example.tn/health", handler.Asked!.ToString());
    }

    [Fact]
    public async Task No_Answer_Is_A_Cloud_That_Is_Gone()
    {
        var handler = new Handler(_ => throw new HttpRequestException("Aucun hôte."));

        Assert.False(await RelayPromoter.CloudServesAsync(new HttpClient(handler), "https://cloud.example.tn", CancellationToken.None));
    }

    private static string Pad(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
    }

    private static string Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            return Task.FromResult(_respond(request));
        }
    }
}
