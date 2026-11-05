using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.SignIn;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// <see cref="ISignInRefusalRecorder"/> over the shared <see cref="IMemoryCache"/>: one log line per refusal, counts per
/// e-mail and per address over <see cref="SignInRefusalRules.Window"/>, and the vendor's e-mail when
/// <see cref="SignInRefusalRules.Due"/> says so — once per case per quiet period, at most
/// <see cref="SignInRefusalRules.MaxAlertsPerHour"/> an hour.
///
/// <para><b>In memory, like <see cref="LoginAttemptTracker"/>, and for its reason:</b> a table would add a write to an
/// anonymous endpoint on every refusal. A restart forgets the counts, which costs at most one repeated e-mail.</para>
///
/// <para>⚠️ The e-mail leaves from its own scope on the thread pool, with the request's execution context suppressed:
/// the refusal is answered before any mail server is reached, and the request's <c>HttpContext</c> is over by the time
/// one answers.</para>
/// </summary>
public sealed class SignInRefusalRecorder : ISignInRefusalRecorder
{
    private const string KeyPrefix = "signin-refusals";

    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TrustedProxies _trustedProxies;
    private readonly ILogger<SignInRefusalRecorder> _logger;
    private readonly Func<SignInAlert, Task> _dispatch;
    private readonly object _gate = new();
    private DateTime _hourEndsUtc = DateTime.MinValue;
    private int _sentThisHour;

    public SignInRefusalRecorder(
        IMemoryCache cache,
        IHttpContextAccessor httpContextAccessor,
        TrustedProxies trustedProxies,
        IServiceScopeFactory scopes,
        IConfiguration configuration,
        ILogger<SignInRefusalRecorder> logger)
        : this(cache, httpContextAccessor, trustedProxies, logger,
            alert => SendFromOwnScope(scopes, configuration, alert, logger))
    {
    }

    /// <summary>The tests' seam: <paramref name="dispatch"/> is what « tell the vendor » does.</summary>
    public SignInRefusalRecorder(
        IMemoryCache cache,
        IHttpContextAccessor httpContextAccessor,
        TrustedProxies trustedProxies,
        ILogger<SignInRefusalRecorder> logger,
        Func<SignInAlert, Task> dispatch)
    {
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _trustedProxies = trustedProxies;
        _logger = logger;
        _dispatch = dispatch;
    }

    public void Record(SignInRefusal refusal)
    {
        var context = _httpContextAccessor.HttpContext;
        var address = context is null ? ClientIp.Unknown : ClientIp.Resolve(context, _trustedProxies);

        _logger.LogWarning(
            "Sign-in refused at {Door}: {Reason} for {Email} from {ClientAddress}",
            refusal.Door, refusal.Reason, refusal.ShownEmail, address);

        List<SignInAlert> alerts;
        lock (_gate)
        {
            // Per population: one address can be a clinic account and a console account at once.
            var population = refusal.Door.IsConsole() ? "console" : "clinic";
            var sameEmail = refusal.EmailKey.Length == 0 ? 0 : Count($"{KeyPrefix}:email:{population}:{refusal.EmailKey}");
            // « unknown » is every request with no address at all, not one source.
            var sameAddress = address == ClientIp.Unknown ? 0 : Count($"{KeyPrefix}:address:{address}");

            alerts = SignInRefusalRules.Due(refusal, address, sameEmail, sameAddress)
                .Where(TryClaim)
                .Select(due => new SignInAlert(due.Kind, refusal, address, due.Attempts))
                .ToList();
        }

        foreach (var alert in alerts)
        {
            _ = DispatchAsync(alert);
        }
    }

    private int Count(string key)
    {
        var count = _cache.TryGetValue(key, out int existing) ? existing + 1 : 1;
        _cache.Set(key, count, new MemoryCacheEntryOptions { SlidingExpiration = SignInRefusalRules.Window });
        return count;
    }

    private bool TryClaim(DueSignInAlert due)
    {
        var quietKey = $"{KeyPrefix}:quiet:{due.QuietKey}";
        if (_cache.TryGetValue(quietKey, out _))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (now >= _hourEndsUtc)
        {
            _hourEndsUtc = now.AddHours(1);
            _sentThisHour = 0;
        }

        if (_sentThisHour >= SignInRefusalRules.MaxAlertsPerHour)
        {
            _logger.LogWarning("Sign-in alert {Kind} not e-mailed: {Max} already sent this hour.",
                due.Kind, SignInRefusalRules.MaxAlertsPerHour);
            return false;
        }

        _sentThisHour++;
        _cache.Set(quietKey, true, due.Quiet);
        return true;
    }

    private async Task DispatchAsync(SignInAlert alert)
    {
        try
        {
            await _dispatch(alert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sign-in alert {Kind} could not be sent.", alert.Kind);
        }
    }

    private static Task SendFromOwnScope(
        IServiceScopeFactory scopes, IConfiguration configuration, SignInAlert alert, ILogger logger)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(async () =>
            {
                using var scope = scopes.CreateScope();
                await SignInAlertNotice.SendAsync(
                    scope.ServiceProvider.GetRequiredService<IPlatformAccountRepository>(),
                    scope.ServiceProvider.GetRequiredService<ITransactionalEmailSender>(),
                    configuration[VendorAlertRecipients.AlertEmailKey],
                    alert,
                    logger,
                    CancellationToken.None);
            });
        }
    }
}
