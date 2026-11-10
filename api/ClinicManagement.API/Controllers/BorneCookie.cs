namespace ClinicManagement.API.Controllers;

/// <summary>
/// The borne's credential: an <c>HttpOnly</c> cookie scoped to <c>/api/pointage</c>, so no other request carries it.
/// Chrome caps a cookie at 400 days; every punch re-issues it, so a borne in use never expires.
/// </summary>
public static class BorneCookie
{
    public const string Name = "apexa_borne";

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(400);

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Write(HttpResponse response, bool isHttps, string secret) =>
        response.Cookies.Append(Name, secret, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/pointage",
            MaxAge = Lifetime,
            IsEssential = true,
        });
}
