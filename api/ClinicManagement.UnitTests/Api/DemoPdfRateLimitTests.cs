using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using ClinicManagement.API.Controllers;
using ClinicManagement.API.Startup;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.UnitTests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// demo-cabinet AC-5.6: the demo's PDFs are limited per visit. Every visitor signs in as the same dentist, so the
/// key is the visit's session family, never the user.
/// </summary>
public class DemoPdfRateLimitTests
{
    private static HttpContext Visit(string? family, string address = "41.229.0.1")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(address);
        var claims = new List<Claim> { new("sub", "demo-dentist") };
        if (family != null)
            claims.Add(new Claim(LocalAuthClaims.SessionFamily, family));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return context;
    }

    private static string Key(HttpContext context) =>
        RateLimiting.DemoSessionPartitionKey(context, TrustedProxies.LoopbackOnly);

    [Fact]
    public void Two_Visitors_Of_The_Same_Dentist_Get_Two_Budgets()
    {
        Assert.NotEqual(Key(Visit("family-a")), Key(Visit("family-b")));
    }

    [Fact]
    public void One_Visit_Keeps_One_Budget_Whatever_The_Address()
    {
        Assert.Equal(Key(Visit("family-a", "41.229.0.1")), Key(Visit("family-a", "197.0.0.9")));
    }

    [Fact]
    public void A_Request_Naming_No_Visit_Falls_Back_To_The_Address()
    {
        Assert.Equal("ip:41.229.0.1", Key(Visit(family: null)));
    }

    // The limiter runs before authentication: the visit must be found on the raw bearer, or every visitor shares the address.
    [Fact]
    public void Before_Authentication_The_Visit_Is_Read_Off_The_Bearer()
    {
        // Minted by the real issuer: a hand-built unsigned token passed here while real demo tokens did not.
        var issuer = new LocalAuthService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Local:SigningKey"] = "demo-test-signing-key-0123456789abcdef0123456789",
                ["Auth:Mode"] = "Local",
            })
            .Build());
        var dentist = ClinicManagement.Domain.Entities.User.CreateLocalUser(
            Guid.NewGuid(), ClinicManagement.Domain.Entities.User.RoleAdmin, "monica.geller+20261103@demo.apexa.tn", "hash", "Monica Geller");
        var families = new Dictionary<string, Guid> { ["family-a"] = Guid.NewGuid(), ["family-b"] = Guid.NewGuid() };
        string Bearer(string family) => "Bearer " + issuer.GenerateToken(dentist, families[family]).AccessToken;
        HttpContext Unauthenticated(string? authorization)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("41.229.0.1");
            if (authorization != null)
                context.Request.Headers.Authorization = authorization;
            return context;
        }

        Assert.Equal($"visit:{families["family-a"]}", Key(Unauthenticated(Bearer("family-a"))));
        Assert.NotEqual(Key(Unauthenticated(Bearer("family-a"))), Key(Unauthenticated(Bearer("family-b"))));
        Assert.Equal("ip:41.229.0.1", Key(Unauthenticated("Bearer not-a-jwt")));
        Assert.Equal("ip:41.229.0.1", Key(Unauthenticated(null)));
    }

    // The visit key is read unvalidated, so forged tokens are fresh buckets; the demo's per-address ceiling still stops them.
    [Theory]
    [InlineData("PublicDemo", 5)]
    [InlineData("HostedMultiTenant", 10)]
    public void Forged_Visits_From_One_Address_Meet_A_Ceiling(string profile, int expectedAccepted)
    {
        var services = new ServiceCollection();
        services.AddConfiguredRateLimiter(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Deployment:Profile"] = profile,
                ["RateLimiting:Api:PermitLimit"] = "1000",
                ["RateLimiting:Api:DemoAddressPermitLimit"] = "5",
            })
            .Build());
        var limiter = services.BuildServiceProvider().GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter!;

        var accepted = Enumerable.Range(0, 10).Count(i =>
        {
            var forged = "e30." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                $"{{\"{LocalAuthClaims.SessionFamily}\":\"{Guid.NewGuid()}\"}}")).TrimEnd('=') + ".sig";
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/patients";
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("41.229.0.1");
            context.Request.Headers.Authorization = $"Bearer {forged}";
            return limiter.AttemptAcquire(context).IsAcquired;
        });

        Assert.Equal(expectedAccepted, accepted);
    }

    [Fact]
    public void A_Visit_Key_Can_Never_Collide_With_An_Address_Key()
    {
        Assert.StartsWith("visit:", Key(Visit("41.229.0.1")));
    }

    [Theory]
    [InlineData(45, "45 secondes")]
    [InlineData(61, "2 minutes")]
    [InlineData(60, "1 minute")]
    public void The_Refusal_Is_French_And_Says_How_Long(int seconds, string expected)
    {
        var message = RateLimiting.DemoPdfLimitedMessage(seconds);

        Assert.StartsWith("La démo limite", message);
        Assert.Contains(expected, message);
    }

    [Fact]
    public void The_Ordinary_Refusal_Is_Unchanged()
    {
        Assert.Equal("Trop de tentatives. Veuillez réessayer dans 45 secondes.", RateLimiting.TooManyRequestsMessage(45));
        Assert.Equal("Trop de tentatives. Veuillez réessayer dans 1 minute.", RateLimiting.TooManyRequestsMessage(60));
        Assert.Equal("Trop de tentatives. Veuillez réessayer dans 3 minutes.", RateLimiting.TooManyRequestsMessage(150));
    }

    // Derived from the controllers' own source: an action that answers a PDF must carry the demo's limit.
    [Fact]
    public void Every_Pdf_Action_Carries_The_Demo_Limit()
    {
        var unlimited = PdfActions().Where(a => !a.Limited).Select(a => a.Name).ToList();

        Assert.True(PdfActions().Count >= 9, $"expected the nine PDF actions, found {PdfActions().Count}");
        Assert.Empty(unlimited);
    }

    [Fact]
    public void Only_Pdf_Actions_Carry_The_Demo_Limit()
    {
        var pdf = PdfActions().Select(a => a.Name).ToHashSet();
        var limited = typeof(ApiControllerBase).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName == RateLimiting.DemoPdfPolicy)
                .Select(m => $"{t.Name}.{m.Name}"))
            .ToHashSet();

        Assert.Equal(pdf.OrderBy(x => x), limited.OrderBy(x => x));
    }

    [Fact]
    public void The_Guard_Catches_A_Pdf_Action_Without_The_Limit()
    {
        const string probe = """
            [HttpGet("{id}/pdf")]
            public async Task<ActionResult> Probe(Guid id) => File(new byte[0], "application/pdf", "x.pdf");
            """;

        Assert.Contains(Scan("ProbeController", probe), a => a is { Name: "ProbeController.Probe", Limited: false });
    }

    private static List<(string Name, bool Limited)> PdfActions()
    {
        var dir = Path.Combine(SolutionSources.Root().FullName, "ClinicManagement.API", "Controllers");
        return Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => Scan(Path.GetFileNameWithoutExtension(f), SolutionSources.WithoutComments(File.ReadAllText(f))))
            .ToList();
    }

    // Each "application/pdf" is read back to the action declaring it, and that action's attribute block.
    private static IEnumerable<(string Name, bool Limited)> Scan(string controller, string source)
    {
        var actions = Regex.Matches(source, @"((?:\s*\[[^\]]*\])+)\s*public\s+(?:async\s+)?[\w<>\[\],.?\s]+?\s(\w+)\s*\(");
        foreach (Match pdf in Regex.Matches(source, "\"application/pdf\""))
        {
            var owner = actions.LastOrDefault(a => a.Index < pdf.Index);
            if (owner == null)
                continue;
            yield return ($"{controller}.{owner.Groups[2].Value}", owner.Groups[1].Value.Contains("RateLimiting.DemoPdfPolicy"));
        }
    }
}
