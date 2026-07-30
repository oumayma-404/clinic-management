using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Base controller that renders every failure as the single canonical error body
/// <c>{ "error": "&lt;message&gt;" }</c>, keeping the status code each action chooses.
/// All controllers extend this and route their <see cref="Result"/> failures through
/// <see cref="HandleFailure"/> (or <see cref="Failure"/> for ad-hoc messages) so the
/// frontend can parse one shape everywhere.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Render a failed <see cref="Result"/> as <c>{ error }</c> with the given status code
    /// (default 400). The action keeps ownership of the status code (400/401/403/404/…).
    /// </summary>
    /// <remarks>
    /// When the result carries a <see cref="Result.Code"/>, it is added as <c>code</c> beside <c>error</c> so a
    /// client can branch on the refusal instead of pattern-matching the French message. The body stays
    /// <c>{ error }</c> for every failure without one, so no existing consumer changes.
    /// </remarks>
    protected ActionResult HandleFailure(Result result, int statusCode = StatusCodes.Status400BadRequest)
        => string.IsNullOrWhiteSpace(result.Code)
            ? Failure(result.Error, statusCode)
            : StatusCode(statusCode, new
            {
                error = string.IsNullOrWhiteSpace(result.Error) ? ErrorMessages.Generic : result.Error,
                code = result.Code,
            });

    /// <summary>
    /// Render an error message as <c>{ error }</c> with the given status code (default 400).
    /// A null/blank message falls back to the generic message (never leaks an empty body).
    /// </summary>
    protected ActionResult Failure(string? message, int statusCode = StatusCodes.Status400BadRequest)
        => StatusCode(statusCode, new { error = string.IsNullOrWhiteSpace(message) ? ErrorMessages.Generic : message });
}
