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
    protected ActionResult HandleFailure(Result result, int statusCode = StatusCodes.Status400BadRequest)
        => Failure(result.Error, statusCode);

    /// <summary>
    /// Render an error message as <c>{ error }</c> with the given status code (default 400).
    /// A null/blank message falls back to the generic message (never leaks an empty body).
    /// </summary>
    protected ActionResult Failure(string? message, int statusCode = StatusCodes.Status400BadRequest)
        => StatusCode(statusCode, new { error = string.IsNullOrWhiteSpace(message) ? ErrorMessages.Generic : message });
}
