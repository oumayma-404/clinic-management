using System.Text.Json;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// A request Kestrel could not read — its body arrived too slowly (408, Kestrel's <c>MinRequestBodyDataRate</c>), too
/// large (413) or malformed (400) — answered with ITS OWN status and a French sentence, and logged as a warning.
///
/// <para><b>Why it exists.</b> Measured on a cabinet's import, 26/11/2026: a connection that stalled for five seconds
/// mid-request reached <c>ExceptionMiddleware</c> as an ordinary exception, so the client was told 500 « une erreur est
/// survenue » and the log carried an <c>ERR</c> — a fault of ours, by every sign, for a fact about the client's line.
/// The client can only retry; telling it so is the whole fix.</para>
///
/// <para>⚠️ <b>Here and not in <c>ExceptionMiddleware</c></b>: that one lives in Application, which does not see the
/// shared framework's <see cref="BadHttpRequestException"/>. Registered directly inside it, so it is the first to see the
/// exception on its way out, and never writes once the response has started (nothing can be said then).</para>
/// </summary>
public sealed class ClientRequestFaultMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ClientRequestFaultMiddleware> _logger;

    public ClientRequestFaultMiddleware(RequestDelegate next, ILogger<ClientRequestFaultMiddleware> logger)
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
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Request refused before reaching a handler ({StatusCode}): {Message}", ex.StatusCode, ex.Message);
            context.Response.Clear();
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = SentenceFor(ex.StatusCode) }));
        }
    }

    /// <summary>What the client is told — never Kestrel's own English message.</summary>
    public static string SentenceFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status408RequestTimeout => "La connexion est trop lente : la demande n'est pas arrivée en entier. Réessayez.",
        StatusCodes.Status413PayloadTooLarge => "La demande est trop volumineuse.",
        _ => "La demande n'a pas pu être lue. Réessayez.",
    };
}
