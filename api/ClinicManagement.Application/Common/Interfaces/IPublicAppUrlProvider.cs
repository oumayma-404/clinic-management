namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Where this deployment's web app answers, as a browser would reach it — the base a link put in an email has to
/// be built from.
///
/// <para><b>An interface rather than a configuration read</b> because this project references no configuration
/// package at all (Domain, MediatR and EF Core are its whole dependency set), and adding one so a single handler
/// can read one string would widen the layer's surface for a value that is already an outbound fact about the
/// environment. Its implementation reads <c>FrontendUrl</c> — the same key
/// <c>GoogleCalendarController</c>'s OAuth success redirect uses, so the two cannot point at different hosts.</para>
/// </summary>
public interface IPublicAppUrlProvider
{
    /// <summary>
    /// Whether this deployment has actually been told where its web app answers.
    ///
    /// <para>Asked beside <c>ITransactionalEmailSender.IsConfigured</c> and for the identical reason: with the key
    /// unset every verification link points at the <i>recipient's own machine</i>, so the visitor gets a 202, the
    /// operator gets a clean log, and the clinic is never created. A missing link host is exactly as fatal as a
    /// missing mail host, and has to refuse as loudly.</para>
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// The origin, without a trailing slash. Never empty — it falls back to the development origin rather than
    /// producing a link with no host, which would arrive in an inbox as text. Only meaningful when
    /// <see cref="IsConfigured"/>.
    /// </summary>
    string BaseUrl { get; }
}
