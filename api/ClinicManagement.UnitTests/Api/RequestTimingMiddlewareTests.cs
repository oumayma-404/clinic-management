using ClinicManagement.API.Middleware;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>The per-request timing line (<c>features/performance-caching</c>) — mostly what it must never write.</summary>
public class RequestTimingMiddlewareTests
{
    private const string Template = "api/patients/{patientId}/files/{fileId}/preview";
    private const string PatientId = "3f2b1c9e-0000-4000-8000-000000000001";
    private const string PreviewPath = $"/api/patients/{PatientId}/files/abc/preview";

    private sealed class CapturingLogger : ILogger<RequestTimingMiddleware>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add((logLevel, formatter(state, exception)));
    }

    private static IConfiguration Configuration(string? slowRequestMs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Diagnostics:SlowRequestMs"] = slowRequestMs })
            .Build();

    private static async Task<(CapturingLogger Logger, bool ReachedNext)> InvokeAsync(
        string path,
        string? slowRequestMs = null,
        int status = StatusCodes.Status200OK,
        int queries = 0,
        string query = "",
        Exception? throws = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask, RoutePatternFactory.Parse(Template), 0, EndpointMetadataCollection.Empty, "preview"));

        var metrics = new RequestQueryMetrics();
        var logger = new CapturingLogger();
        var reachedNext = false;

        var middleware = new RequestTimingMiddleware(ctx =>
        {
            reachedNext = true;
            for (var i = 0; i < queries; i++)
            {
                metrics.Record(TimeSpan.FromMilliseconds(2));
            }

            ctx.Response.StatusCode = status;
            return throws is null ? Task.CompletedTask : Task.FromException(throws);
        }, logger, Configuration(slowRequestMs));

        try
        {
            await middleware.InvokeAsync(context, metrics);
        }
        catch (Exception ex) when (ex == throws)
        {
        }

        return (logger, reachedNext);
    }

    [Fact]
    public async Task It_Logs_The_Route_Template_And_Never_The_Path_Or_The_Query_String()
    {
        var (logger, _) = await InvokeAsync(PreviewPath, slowRequestMs: "0", query: "?access_token=secret-token");

        var line = Assert.Single(logger.Lines).Message;
        Assert.Contains(Template, line);
        Assert.DoesNotContain(PatientId, line);
        Assert.DoesNotContain("secret-token", line);
    }

    [Fact]
    public async Task It_Reports_The_Final_Status_And_How_Many_Queries_Ran()
    {
        var (logger, _) = await InvokeAsync(
            PreviewPath, slowRequestMs: "0", status: StatusCodes.Status304NotModified, queries: 3);

        var line = Assert.Single(logger.Lines).Message;
        Assert.Contains("→ 304", line);
        Assert.Contains("3 queries", line);
    }

    [Fact]
    public async Task A_Request_Under_The_Default_Threshold_Is_Logged_At_Debug()
    {
        var (logger, _) = await InvokeAsync(PreviewPath);

        Assert.Equal(LogLevel.Debug, Assert.Single(logger.Lines).Level);
    }

    [Fact]
    public async Task A_Request_At_The_Threshold_Is_Logged_At_Information()
    {
        var (logger, _) = await InvokeAsync(PreviewPath, slowRequestMs: "0");

        Assert.Equal(LogLevel.Information, Assert.Single(logger.Lines).Level);
    }

    [Fact]
    public async Task A_Request_That_Throws_Is_Still_Logged()
    {
        var (logger, _) = await InvokeAsync(PreviewPath, slowRequestMs: "0", throws: new InvalidOperationException());

        Assert.Single(logger.Lines);
    }

    [Theory]
    [InlineData("/hub/clinic")]
    [InlineData("/health")]
    [InlineData("/patients/3f2b1c9e-0000-4000-8000-000000000001")]
    public async Task A_Path_Outside_The_Api_Passes_Through_Unlogged(string path)
    {
        var (logger, reachedNext) = await InvokeAsync(path, slowRequestMs: "0", query: "?access_token=secret-token");

        Assert.True(reachedNext);
        Assert.Empty(logger.Lines);
    }
}
