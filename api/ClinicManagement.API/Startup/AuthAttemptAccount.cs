using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Lifts the submitted email out of an anonymous auth request's body so the rate limiter can partition on the
/// <b>account</b> rather than only on the address (multi-tenant-cloud US-6). See <see cref="RateLimiting"/> for why
/// that re-keying matters.
///
/// <para><b>Why a middleware and not the partitioner itself.</b> The limiter runs long before model binding, so
/// the email is still an unread request body at that point — and the partitioner delegate is synchronous, which
/// makes reading it there sync-over-async on the request stream. This reads it once, asynchronously, rewinds the
/// stream so the action's own binding still sees an intact body, and leaves the value on
/// <c>HttpContext.Items</c>.</para>
///
/// <para><b>Nothing about it may be able to refuse a request.</b> A body that is not JSON, is truncated, is
/// oversized, or simply has no <c>email</c> property leaves no value behind and the limiter falls back to the
/// address — i.e. to exactly the behaviour that shipped before. A parse failure here must never surface: the login
/// endpoint's own validation is what answers a malformed request, and turning a JSON slip into a 500 at the
/// limiter would make the login page unreachable rather than merely refuse the attempt.</para>
/// </summary>
public static class AuthAttemptAccount
{
    /// <summary>
    /// The most body this will buffer. A login payload is a few hundred bytes; anything larger is not one, and
    /// buffering an unbounded body in order to *rate-limit* it would be its own denial of service.
    /// </summary>
    public const int MaxCapturedBodyBytes = 8 * 1024;

    private const string EmailProperty = "email";

    /// <summary>
    /// The property naming the SESSION on <c>POST auth/refresh</c>, which carries no email at all.
    ///
    /// <para>⚠️ Without it that endpoint fell back to the address, so a whole practice behind one NAT address
    /// shared <b>one</b> bucket of 30 per 300 s for silent token renewal — and when it tripped the client received
    /// a 429 it did not surface, then looped on the 401s that followed. It is the one auth route where every
    /// legitimate caller is already authenticated, so keying on the session is both available and correct.</para>
    /// </summary>
    private const string RefreshTokenProperty = "refreshToken";

    /// <summary>
    /// Registers the capture. Must sit immediately before <c>UseRateLimiter()</c>: after it, the partition has
    /// already been chosen.
    /// </summary>
    public static void UseAuthAttemptAccountCapture(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AuthAttemptAccount));

        app.Use(async (context, next) =>
        {
            if (ShouldCapture(context))
            {
                await CaptureAsync(context, logger);
            }

            await next(context);
        });
    }

    /// <summary>
    /// A JSON body small enough to buffer, on an auth attempt. Asks <see cref="RateLimiting.IsAnonymousAuthAttempt"/>
    /// rather than repeating its terms, so the capture and the limiter cannot disagree about what one is.
    /// </summary>
    public static bool ShouldCapture(HttpContext context) =>
        RateLimiting.IsAnonymousAuthAttempt(context)
        && (context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false)
        && context.Request.ContentLength is > 0 and <= MaxCapturedBodyBytes;

    private static async Task CaptureAsync(HttpContext context, ILogger logger)
    {
        try
        {
            context.Request.EnableBuffering();

            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer);
            // Unconditionally, and before anything can throw on the bytes: the action's model binding reads this
            // same stream, so leaving it at the end would bind an empty body — the request would fail validation
            // for a reason that has nothing to do with the request.
            Rewind(context);

            var body = buffer.ToArray();

            var email = ReadEmail(body);
            if (email is not null)
            {
                context.Items[RateLimiting.SubmittedAccountItemKey] = email;
            }

            // Only when there is no account: a body carrying both would be a shape no endpoint has, and the
            // account is the tighter, more meaningful partition of the two.
            if (email is null && ReadSessionKey(body) is { } session)
            {
                context.Items[RateLimiting.SubmittedSessionItemKey] = session;
            }
        }
        catch (Exception ex)
        {
            // Silent to the caller but not to the log: a *systematic* capture failure silently reverts the limiter
            // to per-address partitioning, i.e. reinstates the lockout US-6 exists to remove, and nothing else
            // would connect the two.
            logger.LogWarning(ex, "Could not read the submitted account for rate limiting; falling back to the address.");
            Rewind(context);
        }
    }

    /// <summary>
    /// Rewinds the body for model binding. ⚠️ <c>CanSeek</c> is not defensive tidiness: the reachable failure above
    /// is <c>EnableBuffering</c> itself, and assigning <c>Position</c> to a still-unbuffered stream throws
    /// <c>NotSupportedException</c> — out of a handler that runs *before* <c>ExceptionMiddleware</c>, so it would
    /// surface as a raw 500 on <c>POST auth/login</c> and break the one contract this class claims: that nothing
    /// about it can refuse a request.
    /// </summary>
    private static void Rewind(HttpContext context)
    {
        if (context.Request.Body.CanSeek)
        {
            context.Request.Body.Position = 0;
        }
    }

    /// <summary>
    /// The normalised <c>email</c> value of a JSON object, or null. Case-insensitive on the property name, and
    /// lower-cased on the value, so <c>Nom@Cabinet.tn</c> and <c>nom@cabinet.tn</c> spend one budget rather than
    /// two — otherwise varying the capitalisation would multiply the allowance for one account.
    /// </summary>
    public static string? ReadEmail(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, EmailProperty, StringComparison.OrdinalIgnoreCase)
                    || property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = property.Value.GetString()?.Trim();
                return string.IsNullOrEmpty(value) ? null : value.ToLowerInvariant();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A stable, non-secret key for the session a refresh request names, or null.
    ///
    /// <para>⚠️ <b>Hashed, never the token itself.</b> A rate-limiter partition key is held in memory for the
    /// window's duration and is exactly the kind of value that ends up in a diagnostic dump — putting a live
    /// refresh credential there would trade a lockout for a credential leak. SHA-256 truncated to 128 bits is
    /// far past collision-resistance for a per-window bucket, and the same token always yields the same key,
    /// which is all the partition needs.</para>
    /// </summary>
    public static string? ReadSessionKey(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, RefreshTokenProperty, StringComparison.OrdinalIgnoreCase)
                    || property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var token = property.Value.GetString()?.Trim();
                if (string.IsNullOrEmpty(token))
                {
                    return null;
                }

                var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
                return Convert.ToHexString(digest, 0, 16);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
