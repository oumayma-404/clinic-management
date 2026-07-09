using ClinicManagement.Infrastructure;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure;

/// <summary>
/// CORS origin assembly (FR-E1 / R-4). The credentialed policy cannot use AllowAnyOrigin, so the origins
/// must be enumerated: FrontendUrl unioned with the optional list, deduped case-insensitively, with
/// empty/whitespace entries dropped. Cloud collapses to the single FrontendUrl (unchanged).
/// </summary>
public class CorsOriginsTests
{
    [Fact]
    public void Single_frontend_url_yields_one_origin()
    {
        Assert.Equal(new[] { "http://localhost:3000" },
            CorsOrigins.Assemble("http://localhost:3000", additional: null));
    }

    [Fact]
    public void Frontend_url_and_list_are_unioned_in_order()
    {
        var origins = CorsOrigins.Assemble("http://localhost:3000",
            new[] { "https://clinic-server:5001", "https://client-pc:5001" });

        Assert.Equal(
            new[] { "http://localhost:3000", "https://clinic-server:5001", "https://client-pc:5001" },
            origins);
    }

    [Fact]
    public void Duplicates_are_removed_case_insensitively()
    {
        var origins = CorsOrigins.Assemble("http://localhost:3000",
            new[] { "http://localhost:3000", "HTTP://LOCALHOST:3000" });

        Assert.Single(origins);
    }

    [Fact]
    public void Empty_null_and_whitespace_entries_are_dropped()
    {
        var origins = CorsOrigins.Assemble("http://localhost:3000",
            new[] { "", "   ", null, "https://client-pc:5001" });

        Assert.Equal(new[] { "http://localhost:3000", "https://client-pc:5001" }, origins);
    }

    [Fact]
    public void Null_frontend_url_is_dropped()
    {
        Assert.Equal(new[] { "https://client-pc:5001" },
            CorsOrigins.Assemble(frontendUrl: null, new[] { "https://client-pc:5001" }));
    }
}
