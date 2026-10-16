namespace ClinicManagement.Application.Common.Exceptions;

/// <summary>
/// A save reached a cabinet's rows on the side that may not write them now (<c>clinic-pc-copy</c> D15): the cloud while
/// its PC de secours may hold the cabinet's saves, or a PC de secours that does not hold them. Raised by the change
/// capture's net, below the gate — so it is what stops a job, a backfill or any path no middleware sees.
///
/// <para>⚠️ <b>It derives from <see cref="ConflictException"/> on purpose</b>: every handler catch-all carries
/// <c>when (ex is not ConflictException)</c>, so a subclass reaches <see cref="ExceptionMiddleware"/> instead of being
/// flattened into « Une erreur est survenue ». The middleware answers it <b>423</b> with its own sentence and code,
/// never the 409 « modifié par quelqu'un d'autre » — nothing here is a concurrent edit.</para>
/// </summary>
public sealed class ClinicFencedException : ConflictException
{
    public ClinicFencedException(string message, string code) : base(message)
    {
        Code = code;
    }

    /// <summary>The refusal's code (<c>relay_silent</c>, <c>clinic_on_relay</c>, <c>relay_standby</c>, …), what clients branch on.</summary>
    public string Code { get; }
}
