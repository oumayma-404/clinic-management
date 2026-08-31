namespace ClinicManagement.Application.Common.Models;

public class Result
{
    public bool IsSuccess { get; private set; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; private set; }

    /// <summary>
    /// Optional machine-readable tag for a failure, rendered as <c>code</c> beside <c>error</c> in the response
    /// body (see <c>ApiControllerBase.HandleFailure</c>).
    ///
    /// <para><b>Why a code and not just the message.</b> A handful of refusals are ones the client is meant to
    /// <i>react</i> to rather than merely display — the caller can retry with an explicit override, or route the
    /// user somewhere. Deciding that from the French message means pattern-matching prose: it works until someone
    /// rewords the sentence, at which point the behaviour silently reverts and no test notices. The precedent is
    /// <c>LocalAuthEnforcementMiddleware</c>, which already returns <c>{ error, code: "must_change_password" }</c>
    /// for exactly this reason.</para>
    ///
    /// <para>Null for the overwhelming majority of failures, which the client only ever shows to the user. Do not
    /// add a code unless a caller genuinely branches on it — an unused code is a contract nobody is honouring.</para>
    /// </summary>
    public string? Code { get; private set; }

    /// <summary>
    /// The exception behind a failure, for the <b>log</b> — never for the client.
    ///
    /// <para><b>Why this exists.</b> ~160 handlers ended their catch-all with
    /// <c>Result.Failure(ex.Message)</c>, and that message went into the response body verbatim: Npgsql
    /// SQLSTATEs and table names, S3 endpoints, server file paths, English framework text — all of it reaching
    /// an authenticated browser, and none of it reaching a log. So the detail was simultaneously <i>exposed</i>
    /// to the one place it must not go and <i>lost</i> to the one place it was needed.</para>
    ///
    /// <para>⚠️ <b>This property must never be serialised.</b> <c>ApiControllerBase.HandleFailure</c> logs it and
    /// builds the body from <see cref="Error"/> and <see cref="Code"/> alone;
    /// <c>ErrorContractCoverageTests</c> is the derived guard that fails if a response shape ever names it.</para>
    /// </summary>
    public Exception? Diagnostic { get; private set; }

    protected Result(bool isSuccess, string? error, string? code = null, Exception? diagnostic = null)
    {
        IsSuccess = isSuccess;
        Error = error;
        Code = code;
        Diagnostic = diagnostic;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string error, string? code = null) => new(false, error, code);

    /// <summary>
    /// A failure the user sees as <paramref name="error"/> while the operator sees
    /// <paramref name="diagnostic"/> in the log. The overload to reach for in a catch-all.
    /// </summary>
    public static Result Failure(string error, Exception diagnostic) => new(false, error, null, diagnostic);
}

public class Result<T> : Result
{
    public T? Value { get; private set; }

    private Result(T? value, bool isSuccess, string? error, string? code = null, Exception? diagnostic = null)
        : base(isSuccess, error, code, diagnostic)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(value, true, null);
    public static new Result<T> Failure(string error, string? code = null) => new(default, false, error, code);

    /// <inheritdoc cref="Result.Failure(string, Exception)"/>
    public static new Result<T> Failure(string error, Exception diagnostic) =>
        new(default, false, error, null, diagnostic);

    /// <summary>
    /// Re-wrap another result's failure, preserving its <see cref="Result.Code"/>.
    ///
    /// <para>Handlers routinely short-circuit on a nested result of a different generic type
    /// (<c>Result&lt;bool&gt;</c> from a scheduling check into <c>Result&lt;AppointmentDto&gt;</c>). Written by hand
    /// as <c>Failure(inner.Error!)</c> that silently drops the code, which is the one thing the caller needed.
    /// </para>
    /// </summary>
    public static Result<T> FailureFrom(Result failure) =>
        new(default, false, failure.Error, failure.Code, failure.Diagnostic);
}
