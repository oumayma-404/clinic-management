using System.Text.Json;
using ClinicManagement.Application.Common.Exceptions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Exceptions;

/// <summary>
/// The canonical error middleware (graceful-error-handling). Every exception becomes the single
/// <c>{ "error": "&lt;message&gt;" }</c> body with the mapped status code; a genuinely unexpected
/// exception yields a GENERIC message and never leaks internals (AC-8); FluentValidation failures now
/// map to 400 instead of falling through to a generic 500.
/// </summary>
public class ExceptionMiddlewareTests
{
    private const string GenericMessage = "An error occurred while processing your request.";

    private static async Task<(int status, string body, string? contentType)> InvokeWith(Exception thrown)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionMiddleware(_ => throw thrown, NullLogger<ExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body, context.Response.ContentType);
    }

    /// <summary>Asserts the JSON body is exactly the canonical <c>{ error }</c> object and returns the message.</summary>
    private static string? ErrorField(string body)
    {
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error), "canonical body must expose an 'error' field");
        return error.GetString();
    }

    [Fact]
    public async Task ForbiddenAccessException_Maps_To_403_Canonical_Error() // [AC-1]
    {
        var (status, body, contentType) = await InvokeWith(new ForbiddenAccessException("Access denied."));

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Equal("Access denied.", ErrorField(body));
        Assert.Equal("application/json", contentType);
    }

    [Fact]
    public async Task NotFoundException_Maps_To_404_Canonical_Error() // [AC-1]
    {
        var (status, body, _) = await InvokeWith(new NotFoundException("Missing."));

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal("Missing.", ErrorField(body));
    }

    [Fact]
    public async Task ValidationException_Maps_To_400_Canonical_Error() // [AC-1] the newly-added case
    {
        var failures = new[] { new ValidationFailure("Name", "Name is required.") };

        var (status, body, _) = await InvokeWith(new ValidationException(failures));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains("Name is required.", ErrorField(body));
    }

    [Fact]
    public async Task Unhandled_Exception_Returns_Generic_500_Without_Leaking_Internals() // [AC-8]
    {
        const string secret = "SENSITIVE internal detail p@ssw0rd";

        var (status, body, _) = await InvokeWith(new InvalidOperationException(secret));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal(GenericMessage, ErrorField(body));
        Assert.DoesNotContain("SENSITIVE", body);
        Assert.DoesNotContain("p@ssw0rd", body);
    }
}
