using System.Net;
using ClinicManagement.Application.Common.SignIn;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Auth;

/// <summary>
/// sign-in-alerts: what reaches the owner's inbox. One e-mail per case — not one per refusal — counted per address and
/// per e-mail, and a ceiling on the hour, so an attack is a handful of messages rather than a flood. The log line is
/// written for every refusal and names the address typed and where it came from.
/// </summary>
public sealed class SignInRefusalRecorderTests : IDisposable
{
    private const string Typed = "karim.benali@gmail.com";

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly HttpContextAccessor _accessor = new();
    private readonly CapturingLogger _logger = new();
    private readonly List<SignInAlert> _sent = [];
    private readonly SignInRefusalRecorder _recorder;

    public SignInRefusalRecorderTests()
    {
        _recorder = new SignInRefusalRecorder(_cache, _accessor, TrustedProxies.LoopbackOnly, _logger, alert =>
        {
            _sent.Add(alert);
            return Task.CompletedTask;
        });
    }

    public void Dispose() => _cache.Dispose();

    // The 31/10 case: one person, three tries with an address nobody owns.
    [Fact]
    public void Three_Tries_With_An_Address_Nobody_Owns_Send_One_Email_On_The_Third()
    {
        RequestFrom("41.226.10.20");

        Refuse(Unknown(Typed));
        Refuse(Unknown(Typed));
        Assert.Empty(_sent);

        Refuse(Unknown(Typed));

        var alert = Assert.Single(_sent);
        Assert.Equal(SignInAlertKind.UnknownEmailRepeated, alert.Kind);
        Assert.Equal("41.226.10.20", alert.ClientAddress);
        Assert.Equal(3, alert.Attempts);
    }

    [Fact]
    public void Tries_That_Go_On_After_The_Email_Do_Not_Send_Another()
    {
        RequestFrom("41.226.10.20");

        for (var i = 0; i < 8; i++)
        {
            Refuse(Unknown(Typed));
        }

        Assert.Single(_sent);
    }

    // Changing address does not reset the count on one account.
    [Fact]
    public void Five_Wrong_Passwords_On_One_Account_From_Five_Addresses_Send_One_Email()
    {
        for (var i = 1; i <= SignInRefusalRules.AccountThreshold; i++)
        {
            RequestFrom($"10.0.0.{i}");
            Refuse(WrongPassword("doc@clinic.com"));
        }

        Assert.Equal(SignInAlertKind.AccountUnderAttack, Assert.Single(_sent).Kind);
    }

    [Fact]
    public void Ten_Different_Addresses_Tried_From_One_Place_Send_One_Email()
    {
        RequestFrom("196.203.1.1");

        for (var i = 0; i < SignInRefusalRules.AddressThreshold; i++)
        {
            Refuse(Unknown($"guess{i}@gmail.com"));
        }

        var alert = Assert.Single(_sent);
        Assert.Equal(SignInAlertKind.AddressGuessing, alert.Kind);
        Assert.Equal(SignInRefusalRules.AddressThreshold, alert.Attempts);
    }

    [Fact]
    public void Every_Console_Refusal_Is_Told_Once_Per_Address()
    {
        RequestFrom("41.226.10.20");
        Refuse(ConsoleRefusal("vendor@apexa.tn"));
        Refuse(ConsoleRefusal("vendor@apexa.tn"));
        RequestFrom("102.31.0.7");
        Refuse(ConsoleRefusal("vendor@apexa.tn"));

        Assert.Equal(new[] { "41.226.10.20", "102.31.0.7" }, _sent.Select(a => a.ClientAddress));
        Assert.All(_sent, a => Assert.Equal(SignInAlertKind.ConsoleRefused, a.Kind));
    }

    [Fact]
    public void No_More_Than_The_Hourly_Ceiling_Is_Sent_Whatever_Happens()
    {
        for (var i = 1; i <= SignInRefusalRules.MaxAlertsPerHour + 5; i++)
        {
            RequestFrom($"10.1.0.{i}");
            Refuse(ConsoleRefusal("vendor@apexa.tn"));
        }

        Assert.Equal(SignInRefusalRules.MaxAlertsPerHour, _sent.Count);
    }

    // « unknown » is every request with no address at all; counting it would make strangers look like one source.
    [Fact]
    public void Requests_With_No_Address_Are_Never_Counted_As_One_Source()
    {
        _accessor.HttpContext = null;

        for (var i = 0; i < SignInRefusalRules.AddressThreshold + 2; i++)
        {
            Refuse(Unknown($"guess{i}@gmail.com"));
        }

        Assert.DoesNotContain(_sent, a => a.Kind == SignInAlertKind.AddressGuessing);
    }

    // One address can be a clinic account and a console account at once (a partner with both).
    [Fact]
    public void Clinic_And_Console_Refusals_Are_Counted_Apart()
    {
        RequestFrom("41.226.10.20");

        for (var i = 0; i < SignInRefusalRules.AccountThreshold - 1; i++)
        {
            Refuse(ConsoleRefusal("partner@gmail.com"));
        }

        // Shared counts would make this the fifth refusal on the clinic account, and tell the vendor of an attack.
        Refuse(WrongPassword("partner@gmail.com"));

        Assert.DoesNotContain(_sent, a => a.Kind == SignInAlertKind.AccountUnderAttack);
    }

    [Fact]
    public void A_Dispatch_That_Throws_Never_Reaches_The_Caller()
    {
        var recorder = new SignInRefusalRecorder(
            _cache, _accessor, TrustedProxies.LoopbackOnly, _logger, _ => throw new InvalidOperationException("smtp"));
        RequestFrom("41.226.10.20");

        var record = () => recorder.Record(ConsoleRefusal("vendor@apexa.tn"));

        var thrown = Record.Exception(record);
        Assert.Null(thrown);
    }

    [Fact]
    public void Every_Refusal_Is_Logged_With_The_Address_Typed_And_Where_It_Came_From()
    {
        RequestFrom("41.226.10.20");

        Refuse(Unknown(Typed));

        var line = Assert.Single(_logger.Lines, l => l.Contains("Sign-in refused"));
        Assert.Contains(Typed, line);
        Assert.Contains("41.226.10.20", line);
        Assert.Contains("UnknownEmail", line);
    }

    // The line prints what the rule shows, never the raw key: that may be a password typed into the wrong box.
    [Fact]
    public void Text_That_Is_Not_An_Address_Never_Reaches_The_Log()
    {
        RequestFrom("41.226.10.20");

        Refuse(Unknown("karim@dentiste2024"));

        var line = Assert.Single(_logger.Lines, l => l.Contains("Sign-in refused"));
        Assert.DoesNotContain("dentiste2024", line);
        Assert.Contains(SignInRefusalRules.NotAnAddress, line);
    }

    private void Refuse(SignInRefusal refusal) => _recorder.Record(refusal);

    private void RequestFrom(string peer)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        _accessor.HttpContext = context;
    }

    private static SignInRefusal Unknown(string email) =>
        Refusal(SignInDoor.ClinicSignIn, SignInRefusalReason.UnknownEmail, email);

    private static SignInRefusal WrongPassword(string email) =>
        Refusal(SignInDoor.ClinicSignIn, SignInRefusalReason.WrongSecret, email);

    private static SignInRefusal ConsoleRefusal(string email) =>
        Refusal(SignInDoor.ConsoleSignIn, SignInRefusalReason.WrongSecret, email);

    private static SignInRefusal Refusal(SignInDoor door, SignInRefusalReason reason, string email) =>
        new(door, reason, SignInRefusalRules.ShownEmail(email), email, null,
            new DateTime(2026, 10, 31, 13, 56, 58, DateTimeKind.Utc));

    private sealed class CapturingLogger : ILogger<SignInRefusalRecorder>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}
