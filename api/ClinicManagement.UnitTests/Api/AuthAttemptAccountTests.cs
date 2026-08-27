using System.Text;
using ClinicManagement.API.Startup;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// Lifting the submitted email out of an auth request's body so the limiter can partition on the account
/// (multi-tenant-cloud US-6). See <c>AuthAttemptAccount</c>.
///
/// <para><b>What is actually at risk here is not the happy path.</b> This code runs on every login attempt,
/// before authentication, on a body it did not choose — so the cases worth pinning are the ones where it must
/// produce <i>nothing</i> and get out of the way: a body that is not JSON, a truncated one, an oversized one, one
/// with no <c>email</c>. Any of those turning into an exception would take the login endpoint off the air, which
/// is strictly worse than the lockout the re-key exists to prevent.</para>
/// </summary>
public class AuthAttemptAccountTests
{
    private static byte[] Body(string json) => Encoding.UTF8.GetBytes(json);

    private static HttpContext Request(
        string path,
        string method = "POST",
        string? contentType = "application/json",
        long? contentLength = 42)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Request.ContentType = contentType;
        context.Request.ContentLength = contentLength;
        return context;
    }

    // ---- ReadEmail ----

    [Fact]
    public void The_submitted_email_is_read_from_the_body()
    {
        Assert.Equal(
            "amel@cabinet.tn",
            AuthAttemptAccount.ReadEmail(Body("""{"email":"amel@cabinet.tn","password":"s3cret"}""")));
    }

    [Theory]
    [InlineData("""{"Email":"amel@cabinet.tn"}""")]
    [InlineData("""{"EMAIL":"amel@cabinet.tn"}""")]
    public void The_property_name_is_matched_case_insensitively(string json)
    {
        // The wire is camelCase but a client that sends Pascal case must not silently escape the account bucket.
        Assert.Equal("amel@cabinet.tn", AuthAttemptAccount.ReadEmail(Body(json)));
    }

    [Fact]
    public void The_value_is_lower_cased_and_trimmed()
    {
        // Otherwise varying the capitalisation multiplies the allowance for one account — the guessing limit
        // would be per spelling rather than per account.
        Assert.Equal(
            "amel@cabinet.tn",
            AuthAttemptAccount.ReadEmail(Body("""{"email":"  Amel@Cabinet.TN  "}""")));
    }

    [Theory]
    [InlineData("""{"refreshToken":"abc"}""")]          // POST auth/refresh carries no email at all
    [InlineData("""{"email":""}""")]                    // present but empty
    [InlineData("""{"email":"   "}""")]                 // whitespace only
    [InlineData("""{"email":null}""")]                  // present but not a string
    [InlineData("""{"email":42}""")]
    [InlineData("""{"email":{"value":"a@b.tn"}}""")]
    [InlineData("[]")]                                  // valid JSON, not an object
    [InlineData("\"just a string\"")]
    public void A_body_with_no_usable_email_yields_nothing(string json)
    {
        Assert.Null(AuthAttemptAccount.ReadEmail(Body(json)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]                                   // truncated
    [InlineData("not json at all")]
    [InlineData("{\"email\": ")]
    public void An_unparseable_body_yields_nothing_rather_than_throwing(string json)
    {
        // The login endpoint's own validation answers a malformed request; a throw here would 500 the limiter.
        Assert.Null(AuthAttemptAccount.ReadEmail(Body(json)));
    }

    [Fact]
    public void The_first_email_property_wins_and_a_later_one_cannot_reopen_the_decision()
    {
        // A duplicated key is legal JSON. Whichever one is chosen, the choice must be deterministic — otherwise
        // the same body could land in two different buckets on two requests.
        var repeated = Body("""{"email":"first@cabinet.tn","email":"second@cabinet.tn"}""");

        Assert.Equal("first@cabinet.tn", AuthAttemptAccount.ReadEmail(repeated));
        Assert.Equal("first@cabinet.tn", AuthAttemptAccount.ReadEmail(repeated));
    }

    // ---- ShouldCapture ----

    [Fact]
    public void A_json_post_to_the_auth_surface_is_captured()
    {
        Assert.True(AuthAttemptAccount.ShouldCapture(Request("/api/auth/login")));
    }

    [Fact]
    public void A_charset_suffixed_content_type_is_still_json()
    {
        Assert.True(AuthAttemptAccount.ShouldCapture(
            Request("/api/auth/login", contentType: "application/json; charset=utf-8")));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("OPTIONS")]  // the CORS preflight carries no body
    public void Only_a_post_is_captured(string method)
    {
        Assert.False(AuthAttemptAccount.ShouldCapture(Request("/api/auth/login", method)));
    }

    [Theory]
    [InlineData("/api/patients")]
    [InlineData("/api/invoices")]
    public void Nothing_outside_the_auth_surface_is_captured(string path)
    {
        // Buffering every POST body in the product to rate-limit four endpoints would be a real cost for nothing.
        Assert.False(AuthAttemptAccount.ShouldCapture(Request(path)));
    }

    [Theory]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData(null)]
    public void A_non_json_body_is_not_captured(string? contentType)
    {
        Assert.False(AuthAttemptAccount.ShouldCapture(Request("/api/auth/login", contentType: contentType)));
    }

    [Theory]
    [InlineData(null)]  // unknown length: chunked, so there is no size to bound
    [InlineData(0L)]
    [InlineData((long)AuthAttemptAccount.MaxCapturedBodyBytes + 1)]
    public void A_body_of_no_or_unbounded_size_is_not_captured(long? contentLength)
    {
        // Buffering an unbounded body in order to rate-limit it would be its own denial of service.
        Assert.False(AuthAttemptAccount.ShouldCapture(
            Request("/api/auth/login", contentLength: contentLength)));
    }

    [Fact]
    public void A_body_exactly_at_the_cap_is_still_captured()
    {
        Assert.True(AuthAttemptAccount.ShouldCapture(
            Request("/api/auth/login", contentLength: AuthAttemptAccount.MaxCapturedBodyBytes)));
    }
}
