using ClinicManagement.Infrastructure.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ClinicManagement.API.Startup;

/// <summary>
/// ONE account allowed a higher API ceiling for at most seven days — a practice moving years of records in from its
/// old software through the « Ancien logiciel » import page. Off unless configured:
/// <c>RateLimiting:Api:Boost:Email</c> (the account), <c>RateLimiting:Api:Boost:Until</c> (an ISO instant, at most
/// seven days ahead of the moment the server starts) and, optionally, <c>RateLimiting:Api:Boost:PermitLimit</c>
/// (default 3 000 a minute, never more than 6 000).
///
/// <para>⚠️ <b>The limiter runs BEFORE authentication</b> (an unauthenticated flood must be refused cheaply), so at
/// that point nobody is signed in and the ordinary partition is the client's ADDRESS. Recognising the account
/// therefore means reading its bearer token here — and a token is believed only once its signature, issuer, audience
/// and lifetime pass the very parameters the JWT bearer handler uses. The claim alone is never trusted: a forged token
/// naming the boosted e-mail would otherwise buy a five-times-larger budget for an unauthenticated flood. The cheap
/// decode comes first so every other request pays a base64 read and nothing more.</para>
///
/// <para>⚠️ <b>Read with <see cref="JsonWebTokenHandler"/>, the bearer handler's own reader — never
/// <c>JwtSecurityTokenHandler</c></b>, which on .NET 8 reports this app's valid tokens as « missing an Expiration
/// Time » (IDX10225). The boost written on it refused every real token and switched on for nobody, with no error.
/// <c>A_Token_The_App_Itself_Issued_Gets_The_Boost</c> holds it with a token from <c>LocalAuthService</c>.</para>
///
/// <para>Everything else keeps its limits: sign-in, exports, archives and every other account and address. Once
/// <c>Until</c> passes the account falls back to the ordinary window with no restart.</para>
/// </summary>
public sealed class ApiRateBoost
{
    public const int DefaultPermitLimit = 3000;
    public const int MaxPermitLimit = 6000;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(7);

    private readonly TokenValidationParameters _parameters;
    private readonly TimeProvider _clock;
    private readonly JsonWebTokenHandler _handler = new();

    private ApiRateBoost(string email, int permitLimit, DateTimeOffset until, TokenValidationParameters parameters, TimeProvider clock)
    {
        Email = email;
        PermitLimit = permitLimit;
        Until = until;
        _parameters = parameters;
        _clock = clock;
    }

    public string Email { get; }
    public int PermitLimit { get; }
    public DateTimeOffset Until { get; }

    /// <summary>
    /// The configured boost, or null with the reason it is off. Refused outright — never clamped — when
    /// <c>Until</c> is unreadable, already past, or more than seven days away: a mistyped year must not hand an
    /// account a permanent exemption.
    /// </summary>
    public static (ApiRateBoost? Boost, string? Refusal) FromConfiguration(
        IConfiguration configuration,
        int ordinaryPermitLimit,
        TimeProvider? clock = null)
    {
        var email = configuration["RateLimiting:Api:Boost:Email"]?.Trim();
        var untilRaw = configuration["RateLimiting:Api:Boost:Until"]?.Trim();
        if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(untilRaw))
        {
            return (null, null);
        }

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(untilRaw))
        {
            return (null, "both RateLimiting:Api:Boost:Email and RateLimiting:Api:Boost:Until are required");
        }

        if (!DateTimeOffset.TryParse(untilRaw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var until))
        {
            return (null, $"RateLimiting:Api:Boost:Until « {untilRaw} » is not a date");
        }

        clock ??= TimeProvider.System;
        var now = clock.GetUtcNow();
        if (until <= now)
        {
            return (null, $"RateLimiting:Api:Boost:Until {until:O} is already past");
        }

        if (until - now > MaxDuration)
        {
            return (null, $"RateLimiting:Api:Boost:Until {until:O} is more than {MaxDuration.TotalDays} days away");
        }

        var permit = configuration.GetValue<int?>("RateLimiting:Api:Boost:PermitLimit") ?? DefaultPermitLimit;
        permit = Math.Clamp(permit, ordinaryPermitLimit, MaxPermitLimit);

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = LocalAuthConfig.Issuer(configuration),
            ValidateAudience = true,
            ValidAudience = LocalAuthConfig.Audience(configuration),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = LocalAuthConfig.SecurityKey(configuration),
            ClockSkew = TimeSpan.Zero,
        };

        return (new ApiRateBoost(email, permit, until, parameters, clock), null);
    }

    /// <summary>Whether an already-authenticated caller with this e-mail is the boosted account, right now.</summary>
    public bool AppliesTo(string? email) =>
        _clock.GetUtcNow() < Until && string.Equals(email, Email, StringComparison.OrdinalIgnoreCase);

    /// <summary>The boosted account's subject when this request carries its VALID token and the boost is live.</summary>
    public string? SubjectFor(HttpContext httpContext)
    {
        if (_clock.GetUtcNow() >= Until)
        {
            return null;
        }

        var header = httpContext.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header["Bearer ".Length..].Trim();

        // Runs inside the limiter's partitioner, where an exception would fail every request this caller sends:
        // anything unreadable simply means « no boost », i.e. the ordinary bucket.
        try
        {
            if (!_handler.CanReadToken(token))
            {
                return null;
            }

            // Cheap first: does it even claim to be the boosted account?
            var unverified = _handler.ReadJsonWebToken(token);
            if (!unverified.TryGetPayloadValue<string>("email", out var claimed)
                || !string.Equals(claimed, Email, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var result = _handler.ValidateTokenAsync(token, _parameters).GetAwaiter().GetResult();
            if (!result.IsValid || result.SecurityToken is not JsonWebToken verified)
            {
                return null;
            }

            return verified.TryGetPayloadValue<string>("email", out var email)
                   && string.Equals(email, Email, StringComparison.OrdinalIgnoreCase)
                   && !string.IsNullOrEmpty(verified.Subject)
                ? verified.Subject
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// The boost as the rest of the app sees it (a singleton): <c>GET /api/meta/rate-boost</c> reads it so the import page
/// goes fast only when the server says its ceiling really was raised — going fast without it would spend the whole
/// cabinet's 600-a-minute budget and refuse the other workstations.
/// </summary>
public sealed record ApiRateBoostState(ApiRateBoost? Boost, double WindowSeconds);
