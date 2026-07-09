using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure;

/// <summary>
/// Assembles the CORS allowed-origins list for the API's credentialed policy (FR-E1). Because the
/// policy uses <c>AllowCredentials()</c> it cannot use <c>AllowAnyOrigin()</c>, so the exact origins
/// must be enumerated. In Cloud this collapses to today's single <c>FrontendUrl</c>; in Local an
/// installer/operator can add the LAN client origin(s) via <c>Cors:AllowedOrigins</c> without a code change.
/// </summary>
public static class CorsOrigins
{
    /// <summary>
    /// Builds a deduped, order-preserving origin list from the primary <paramref name="frontendUrl"/>
    /// plus any <paramref name="additional"/> entries. Null/empty/whitespace entries are dropped and
    /// each entry is trimmed; duplicates are removed case-insensitively (origins are case-insensitive).
    /// </summary>
    public static string[] Assemble(string? frontendUrl, IEnumerable<string?>? additional)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }
            var trimmed = candidate.Trim();
            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        Add(frontendUrl);
        if (additional is not null)
        {
            foreach (var entry in additional)
            {
                Add(entry);
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Reads the assembled origin list from configuration: <c>FrontendUrl</c> (default
    /// <c>http://localhost:3000</c>) unioned with the optional <c>Cors:AllowedOrigins</c> array.
    /// </summary>
    public static string[] FromConfiguration(IConfiguration configuration)
    {
        var frontendUrl = configuration["FrontendUrl"] ?? "http://localhost:3000";
        var additional = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        return Assemble(frontendUrl, additional);
    }
}
