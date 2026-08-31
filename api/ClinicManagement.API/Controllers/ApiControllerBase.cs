using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Csv;
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
    {
        // ⚠️ The diagnostic goes to the LOG and never into the body. It carries whatever the handler's catch-all
        // caught — Npgsql SQLSTATEs and table names, S3 endpoints, server paths — which is exactly the material
        // that used to travel to an authenticated browser inside `error`, and simultaneously never reached a log
        // at all. Logged here, once, rather than at ~160 catch sites: a handler cannot forget it, and none of
        // them had to grow an ILogger for it.
        if (result.Diagnostic is not null)
        {
            HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ClinicManagement.HandlerFailure")
                .LogError(
                    result.Diagnostic,
                    "{Method} {Path} failed: {UserMessage}",
                    Request.Method,
                    Request.Path.Value,
                    result.Error);
        }

        return string.IsNullOrWhiteSpace(result.Code)
            ? Failure(result.Error, statusCode)
            : StatusCode(statusCode, new
            {
                error = string.IsNullOrWhiteSpace(result.Error) ? ErrorMessages.Generic : result.Error,
                code = result.Code,
            });
    }

    /// <summary>
    /// Render an error message as <c>{ error }</c> with the given status code (default 400).
    /// A null/blank message falls back to the generic message (never leaks an empty body).
    /// </summary>
    protected ActionResult Failure(string? message, int statusCode = StatusCodes.Status400BadRequest)
        => StatusCode(statusCode, new { error = string.IsNullOrWhiteSpace(message) ? ErrorMessages.Generic : message });

    /// <summary>
    /// Returns a <see cref="CsvTable"/> as a dated download (L5). Shared by every « Exporter » action so the
    /// media type, the charset and the file-name shape are stated once.
    ///
    /// <para>The name carries the <b>clinic-local</b> day (<c>patients-2026-08-03.csv</c>): an owner exports the
    /// same list repeatedly, and two files called <c>patients.csv</c> in one Downloads folder is how the wrong one
    /// gets sent to the accountant. UTC would put a file exported at 00:30 Tunis under the previous day.</para>
    ///
    /// <para><c>text/csv</c> with an explicit <c>charset=utf-8</c>, alongside the BOM <see cref="CsvTable"/>
    /// writes: the BOM is what Excel reads, the charset is what a browser preview and any HTTP client read.</para>
    /// </summary>
    protected FileContentResult Csv(CsvTable table, string baseName) =>
        File(
            table.ToBytes(),
            "text/csv; charset=utf-8",
            $"{baseName}-{ClinicClock.ClinicToday():yyyy-MM-dd}.csv");
}
