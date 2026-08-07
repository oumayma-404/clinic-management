using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Reads <c>FrontendUrl</c> — the key that already decides where the Google OAuth callback sends the browser,
/// and the primary credentialed CORS origin. Reusing it is what keeps « no host is compiled in » true with no
/// new configuration key for an operator to discover.
/// </summary>
public class PublicAppUrlProvider : IPublicAppUrlProvider
{
    private const string DevelopmentFallback = "http://localhost:3000";

    private readonly IConfiguration _configuration;

    public PublicAppUrlProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["FrontendUrl"]);

    public string BaseUrl
    {
        get
        {
            var configured = _configuration["FrontendUrl"];
            return string.IsNullOrWhiteSpace(configured)
                ? DevelopmentFallback
                : configured.Trim().TrimEnd('/');
        }
    }
}
