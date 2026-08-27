using System.Linq;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;

namespace ClinicManagement.Application.Common.Exceptions;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var code = HttpStatusCode.InternalServerError;
        var result = string.Empty;

        switch (exception)
        {
            case ForbiddenAccessException:
                code = HttpStatusCode.Forbidden;
                result = JsonSerializer.Serialize(new { error = exception.Message });
                break;
            case NotFoundException:
                code = HttpStatusCode.NotFound;
                result = JsonSerializer.Serialize(new { error = exception.Message });
                break;
            // A concurrent edit is not a fault — it is a 409 the client can recover from by reloading. It
            // must not fall through to the generic 500 branch, or the user is told « une erreur est survenue »
            // for something that has a specific, actionable cause.
            case ConflictException:
                code = HttpStatusCode.Conflict;
                result = JsonSerializer.Serialize(new { error = exception.Message });
                break;
            case FluentValidation.ValidationException validationException:
                code = HttpStatusCode.BadRequest;
                var validationMessage = string.Join(" ", validationException.Errors.Select(e => e.ErrorMessage));
                result = JsonSerializer.Serialize(new { error = string.IsNullOrWhiteSpace(validationMessage) ? exception.Message : validationMessage });
                break;
            default:
                // Shared constant, not a copy: this is the other half of the `{ error }` contract with
                // ApiControllerBase, and two literals would drift.
                result = JsonSerializer.Serialize(new { error = ErrorMessages.Generic });
                break;
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)code;

        return context.Response.WriteAsync(result);
    }
}


