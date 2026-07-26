namespace ClinicManagement.Application.Common;

/// <summary>
/// Canonical, client-facing error strings shared by the two halves of the <c>{ "error": "&lt;message&gt;" }</c>
/// contract: the API-layer <c>ApiControllerBase</c> failure helper and the Application-layer
/// <c>ExceptionMiddleware</c>. Kept in one place so the generic fallback can't drift between them.
/// </summary>
public static class ErrorMessages
{
    /// <summary>
    /// Generic, internals-free message for an unhandled failure (never leaks details to the client).
    /// French, like the rest of the user-facing product — this string is shown verbatim in the UI.
    /// </summary>
    public const string Generic = "Une erreur est survenue lors du traitement de votre demande.";
}
