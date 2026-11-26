using System.Text.Json;
using ClinicManagement.API.Middleware;
using ClinicManagement.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// A request Kestrel could not read keeps its own status (408 too slow · 413 too large · 400 malformed) and a French
/// sentence, instead of reaching <c>ExceptionMiddleware</c> as a 500 « erreur » — measured on a cabinet's import,
/// 26/11/2026, where a five-second stall was logged as a server error. Everything else still passes through.
/// </summary>
public class ClientRequestFaultMiddlewareTests
{
    private static async Task<(int Status, string Body)> InvokeWith(Exception thrown)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ClientRequestFaultMiddleware(_ => throw thrown, NullLogger<ClientRequestFaultMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData(StatusCodes.Status408RequestTimeout, "trop lente")]
    [InlineData(StatusCodes.Status413PayloadTooLarge, "trop volumineuse")]
    [InlineData(StatusCodes.Status400BadRequest, "pas pu être lue")]
    public async Task An_Unreadable_Request_Keeps_Its_Own_Status_And_A_French_Sentence(int statusCode, string fragment)
    {
        var (status, body) = await InvokeWith(
            new BadHttpRequestException("Reading the request body timed out due to data arriving too slowly.", statusCode));

        Assert.Equal(statusCode, status);
        using var doc = JsonDocument.Parse(body);
        Assert.Contains(fragment, doc.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("Reading the request body", body);
    }

    [Fact]
    public async Task Any_Other_Exception_Is_Left_To_The_Exception_Middleware()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => InvokeWith(new NotFoundException("Missing.")));
    }

    // The Kestrel type a slow body actually raises derives from the one caught here — pinned, because catching the
    // obsolete Kestrel type instead would compile and catch nothing the day the two are told apart.
    [Fact]
    public void Kestrels_Own_Exception_Is_Caught_By_The_Shared_Type()
    {
#pragma warning disable CS0618 // the obsolete Kestrel type is exactly what Kestrel still throws
        Assert.True(typeof(BadHttpRequestException).IsAssignableFrom(
            typeof(Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException)));
#pragma warning restore CS0618
    }
}
