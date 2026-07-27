namespace ClinicManagement.Application.Common.Exceptions;

/// <summary>
/// Two people edited the same record and the second save would have silently overwritten the first.
///
/// <para>
/// Raised by <c>UnitOfWork.SaveChangesAsync</c> when EF reports <c>DbUpdateConcurrencyException</c> — the
/// row's <c>xmin</c> no longer matches the version the caller was working from. Mapped to <b>HTTP 409</b> by
/// <see cref="ExceptionMiddleware"/>, in the same canonical <c>{ "error": … }</c> shape as every other failure,
/// so the frontend can branch on the status rather than on message text.
/// </para>
/// <para>
/// It is deliberately its own type rather than a <c>Result.Failure</c>. Handlers wrap their bodies in a
/// catch-all that turns exceptions into a generic 500-ish failure message, and a conflict funnelled through
/// that would reach the user as « Une erreur est survenue » — indistinguishable from a real fault, and
/// impossible for the UI to offer a reload on. Every such catch now carries
/// <c>when (ex is not ConflictException)</c> so this one gets through.
/// </para>
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }

    public ConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
