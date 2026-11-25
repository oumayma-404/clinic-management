using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ClinicManagement.API.Startup;
using ClinicManagement.Infrastructure.Auth;
using System.Threading.RateLimiting;
using ClinicManagement.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The import account's higher API ceiling: off unless configured, refused rather than clamped when the end date is
/// unreadable, past or more than seven days away, and granted only to a request carrying that account's VALID token.
/// </summary>
public class ApiRateBoostTests
{
    private const string ImportEmail = "import@cabinet.tn";
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Value;
    }

    private static IConfiguration Config(string? email, string? until, int? permit = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Local:SigningKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('k', 64))),
            ["RateLimiting:Api:Boost:Email"] = email,
            ["RateLimiting:Api:Boost:Until"] = until,
            ["RateLimiting:Api:Boost:PermitLimit"] = permit?.ToString(),
        }).Build();

    private static string Token(IConfiguration config, string email, SecurityKey? key = null, DateTime? expires = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = LocalAuthConfig.Issuer(config),
            Audience = LocalAuthConfig.Audience(config),
            Subject = new ClaimsIdentity(new[] { new Claim("sub", "user-42"), new Claim("email", email) }),
            NotBefore = DateTime.UtcNow.AddMinutes(-30),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(key ?? LocalAuthConfig.SecurityKey(config), SecurityAlgorithms.HmacSha256),
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static HttpContext Request(string? bearer)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/patients";
        if (bearer is not null) context.Request.Headers.Authorization = $"Bearer {bearer}";
        return context;
    }

    [Fact]
    public void Nothing_configured_means_no_boost_and_no_complaint()
    {
        var (boost, refusal) = ApiRateBoost.FromConfiguration(Config(null, null), 600, new Clock(Now));
        Assert.Null(boost);
        Assert.Null(refusal);
    }

    [Theory]
    [InlineData(ImportEmail, null)]
    [InlineData(null, "2026-11-30T00:00:00Z")]
    [InlineData(ImportEmail, "pas une date")]
    [InlineData(ImportEmail, "2026-11-24T00:00:00Z")]  // already past
    [InlineData(ImportEmail, "2026-12-03T00:00:00Z")]  // more than 7 days away
    [InlineData(ImportEmail, "2027-11-30T00:00:00Z")]  // a mistyped year
    public void A_half_written_past_or_too_long_boost_is_refused_never_clamped(string? email, string? until)
    {
        var (boost, refusal) = ApiRateBoost.FromConfiguration(Config(email, until), 600, new Clock(Now));
        Assert.Null(boost);
        Assert.False(string.IsNullOrWhiteSpace(refusal));
    }

    [Theory]
    [InlineData(null, 3000)]
    [InlineData(99999, 6000)]
    [InlineData(100, 600)]
    public void The_ceiling_defaults_to_3000_and_stays_between_the_ordinary_one_and_6000(int? configured, int expected)
    {
        var (boost, _) = ApiRateBoost.FromConfiguration(Config(ImportEmail, "2026-12-01T09:00:00Z", configured), 600, new Clock(Now));
        Assert.NotNull(boost);
        Assert.Equal(expected, boost!.PermitLimit);
    }

    [Fact]
    public void Exactly_seven_days_is_allowed()
    {
        var (boost, refusal) = ApiRateBoost.FromConfiguration(Config(ImportEmail, "2026-12-02T09:00:00Z"), 600, new Clock(Now));
        Assert.NotNull(boost);
        Assert.Null(refusal);
    }

    [Fact]
    public void The_import_account_with_a_valid_token_gets_the_boost()
    {
        var config = Config(ImportEmail, "2026-12-01T09:00:00Z");
        var (boost, _) = ApiRateBoost.FromConfiguration(config, 600, new Clock(Now));

        Assert.Equal("user-42", boost!.SubjectFor(Request(Token(config, "IMPORT@cabinet.tn"))));
    }

    // The token the import page really sends is written by LocalAuthService; a hand-built one proved nothing about it
    // while the boost read tokens with a handler that refuses the app's own (IDX10225).
    [Fact]
    public void A_Token_The_App_Itself_Issued_Gets_The_Boost()
    {
        var config = Config(ImportEmail, "2026-12-01T09:00:00Z");
        var (boost, _) = ApiRateBoost.FromConfiguration(config, 600, new Clock(Now));
        var user = User.CreateLocalUser(Guid.NewGuid(), User.RoleSecretary, ImportEmail, "hash", "Import");
        var token = new LocalAuthService(config).GenerateToken(user, sessionFamilyId: null).AccessToken;

        Assert.Equal(user.Id, boost!.SubjectFor(Request(token)));
    }

    [Fact]
    public void Another_account_an_anonymous_request_or_a_garbage_header_do_not()
    {
        var config = Config(ImportEmail, "2026-12-01T09:00:00Z");
        var (boost, _) = ApiRateBoost.FromConfiguration(config, 600, new Clock(Now));

        Assert.Null(boost!.SubjectFor(Request(Token(config, "secretaire@cabinet.tn"))));
        Assert.Null(boost.SubjectFor(Request(null)));
        Assert.Null(boost.SubjectFor(Request("not-a-jwt")));
    }

    [Fact]
    public void A_forged_token_naming_the_import_account_does_not()
    {
        var config = Config(ImportEmail, "2026-12-01T09:00:00Z");
        var (boost, _) = ApiRateBoost.FromConfiguration(config, 600, new Clock(Now));
        var attackerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 64)));

        Assert.Null(boost!.SubjectFor(Request(Token(config, ImportEmail, attackerKey))));
    }

    [Fact]
    public void An_expired_token_does_not()
    {
        var config = Config(ImportEmail, "2026-12-01T09:00:00Z");
        var (boost, _) = ApiRateBoost.FromConfiguration(config, 600, new Clock(Now));
        var expired = Token(config, ImportEmail, expires: DateTime.UtcNow.AddMinutes(-5));

        Assert.Null(boost!.SubjectFor(Request(expired)));
    }

    [Fact]
    public void The_boost_ends_by_itself_at_its_date_with_no_restart()
    {
        var config = Config(ImportEmail, "2026-12-01T09:00:00Z");
        var clock = new Clock(Now);
        var (boost, _) = ApiRateBoost.FromConfiguration(config, 600, clock);
        var token = Token(config, ImportEmail);
        Assert.NotNull(boost!.SubjectFor(Request(token)));

        clock.Value = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        Assert.Null(boost.SubjectFor(Request(token)));
    }
}

/// <summary>
/// The boost through the REAL limiter wiring (<c>AddConfiguredRateLimiter</c>) and tokens the app itself issues — the
/// whole path a request takes before it reaches a controller, with ceilings shrunk to count by hand.
/// </summary>
public class ApiRateBoostLimiterTests
{
    private const string ImportEmail = "import@cabinet.tn";

    private static (PartitionedRateLimiter<HttpContext> Limiter, LocalAuthService Auth) Wire()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Deployment:Profile"] = "HostedMultiTenant",
            ["Auth:Local:SigningKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('k', 64))),
            ["RateLimiting:Api:PermitLimit"] = "5",
            ["RateLimiting:Api:Boost:Email"] = ImportEmail,
            ["RateLimiting:Api:Boost:Until"] = DateTimeOffset.UtcNow.AddDays(3).ToString("O"),
            ["RateLimiting:Api:Boost:PermitLimit"] = "20",
        }).Build();
        var services = new ServiceCollection();
        services.AddConfiguredRateLimiter(config);
        var limiter = services.BuildServiceProvider().GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter!;
        return (limiter, new LocalAuthService(config));
    }

    private static string TokenFor(LocalAuthService auth, string email) =>
        auth.GenerateToken(User.CreateLocalUser(Guid.NewGuid(), User.RoleSecretary, email, "hash", "X"), sessionFamilyId: null).AccessToken;

    private static int Accepted(PartitionedRateLimiter<HttpContext> limiter, string token, int tries) =>
        Enumerable.Range(0, tries).Count(_ =>
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/patients";
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("41.229.0.1");
            context.Request.Headers.Authorization = $"Bearer {token}";
            return limiter.AttemptAcquire(context).IsAcquired;
        });

    [Fact]
    public void The_Import_Account_Gets_Its_Own_Ceiling_And_Leaves_The_Cabinets_Untouched()
    {
        var (limiter, auth) = Wire();

        Assert.Equal(20, Accepted(limiter, TokenFor(auth, ImportEmail), 30));
        // Same address, after the import spent 20: the colleagues' shared budget is still whole.
        Assert.Equal(5, Accepted(limiter, TokenFor(auth, "secretaire@cabinet.tn"), 10));
    }

    [Fact]
    public void A_Forged_Token_Naming_The_Import_Account_Stays_In_The_Address_Bucket()
    {
        var (limiter, auth) = Wire();
        var real = TokenFor(auth, ImportEmail);
        var forged = real[..real.LastIndexOf('.')] + ".AAAA";

        Assert.Equal(5, Accepted(limiter, forged, 10));
    }
}

/// <summary>
/// <c>GET /api/meta/rate-boost</c> — what the import page reads before going fast. Only the boosted account is told
/// it may; everyone else (and everyone, with no boost configured) reads nulls and keeps the cabinet's shared pace.
/// </summary>
public class MetaRateBoostTests
{
    private static ClinicManagement.API.Controllers.MetaController Controller(string email)
    {
        var controller = new ClinicManagement.API.Controllers.MetaController(
            new ConfigurationBuilder().Build(), new Moq.Mock<MediatR.IMediator>().Object);
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) }, "test")),
            },
        };
        return controller;
    }

    private static ApiRateBoostState Boosted()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Local:SigningKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('k', 64))),
            ["RateLimiting:Api:Boost:Email"] = "import@cabinet.tn",
            ["RateLimiting:Api:Boost:Until"] = DateTimeOffset.UtcNow.AddDays(3).ToString("O"),
        }).Build();
        var (boost, _) = ApiRateBoost.FromConfiguration(config, 600);
        return new ApiRateBoostState(boost, 60);
    }

    private static ClinicManagement.API.Controllers.MetaController.RateBoostAnswer Read(
        Microsoft.AspNetCore.Mvc.IActionResult result) =>
        Assert.IsType<ClinicManagement.API.Controllers.MetaController.RateBoostAnswer>(
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result).Value);

    [Fact]
    public void The_import_account_is_told_its_ceiling()
    {
        var answer = Read(Controller("Import@Cabinet.tn").RateBoost(Boosted()));
        Assert.Equal(3000, answer.PermitPerMinute);
        Assert.NotNull(answer.Until);
    }

    [Fact]
    public void Another_account_and_an_unboosted_deployment_read_nulls()
    {
        Assert.Null(Read(Controller("secretaire@cabinet.tn").RateBoost(Boosted())).PermitPerMinute);
        Assert.Null(Read(Controller("import@cabinet.tn").RateBoost(new ApiRateBoostState(null, 60))).PermitPerMinute);
    }
}
