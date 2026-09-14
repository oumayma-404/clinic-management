namespace ClinicManagement.Application.Common.Email;

/// <summary>One labelled row of the panel an e-mail puts its facts in (« Adresse du compte », « Lien »…).</summary>
/// <param name="Label">The small caps line above the value.</param>
/// <param name="Value">The value itself. Escaped on the way into the HTML — it carries user input.</param>
/// <param name="IsLink">
/// Whether <paramref name="Value"/> is a URL to render as an anchor. ⚠️ A link is <b>also</b> printed as text
/// beside the button, deliberately: a recipient whose client refuses to follow a button — or who is reading the
/// message on a phone and needs it on the desk PC — has nothing otherwise.
/// </param>
public sealed record EmailDetail(string Label, string Value, bool IsLink = false);

/// <summary>One numbered step of a walkthrough.</summary>
/// <param name="Title">The action, in the imperative — « Ouvrez Google Authenticator ».</param>
/// <param name="Detail">What to expect, or the thing that trips people up. Optional.</param>
public sealed record EmailStep(string Title, string? Detail = null);

/// <summary>The call to action. One per e-mail, never two — see <see cref="EmailContent.Action"/>.</summary>
public sealed record EmailAction(string Label, string Url);

/// <summary>
/// What one transactional e-mail says, as structure rather than as prose.
///
/// <para><b>Why it is a record and not two strings.</b> Every one of these messages has to exist twice — as the
/// HTML a mail client paints and as the <c>text/plain</c> alternate a client that refuses HTML reads — and
/// writing the sentences twice is this repository's dominant defect shape applied to copy a patient-facing
/// business depends on. <see cref="EmailLayout"/> renders <i>both</i> from this one object, so a reworded
/// paragraph cannot reach one recipient and not the other.</para>
///
/// <para>⚠️ <b>Never compare two of these with <c>==</c>.</b> It is a record, so the operator looks equatable —
/// but four of its members are <c>IReadOnlyList</c>, which records compare by <b>reference</b>, so two
/// separately-built instances are never equal even when every sentence matches. A Moq matcher written that way
/// is permanently false and presents as « the e-mail was never sent », which is what it cost the first time.
/// Compare the parts you actually care about.</para>
///
/// <para>⚠️ <b>Every string here is escaped on the way into the HTML, not on the way in here.</b>
/// <c>Greeting</c> carries <c>User.FullName</c> and <c>Details</c> carries e-mail addresses — both typed by a
/// stranger at a public signup form, so an un-escaped composer would let a chosen name inject markup into a mail
/// the account holder trusts. <see cref="EmailLayout"/> is the only place that encoding lives.</para>
/// </summary>
public sealed record EmailContent
{
    /// <summary>The big heading inside the message. Not the subject — see <see cref="Preheader"/>.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// The one line an inbox list shows next to the subject.
    ///
    /// <para>Set it, always. Left empty, Gmail and Apple Mail scrape the first visible text instead — which here
    /// is « Bonjour Docteur … », so a list of these messages is a column of identical greetings and the reader
    /// cannot tell the verification from the password reset without opening both.</para>
    /// </summary>
    public required string Preheader { get; init; }

    /// <summary>« Bonjour Docteur Sassi, ». Built by <c>EmailGreeting</c>, which handles a null name.</summary>
    public string? Greeting { get; init; }

    /// <summary>The paragraphs before the panel and the button.</summary>
    public IReadOnlyList<string> Intro { get; init; } = [];

    /// <summary>The facts panel. Empty renders no panel at all rather than an empty box.</summary>
    public IReadOnlyList<EmailDetail> Details { get; init; } = [];

    /// <summary>
    /// The single button.
    ///
    /// <para>⚠️ <b>One, deliberately.</b> A second competing button is how a verification e-mail ends with the
    /// account unverified: the reader picks the more inviting one. Anything else that needs reaching goes in
    /// <see cref="Outro"/> as a sentence with a link in it.</para>
    /// </summary>
    public EmailAction? Action { get; init; }

    /// <summary>A heading above <see cref="Steps"/>. Only read when there are steps.</summary>
    public string? StepsTitle { get; init; }

    /// <summary>The numbered walkthrough, after the button.</summary>
    public IReadOnlyList<EmailStep> Steps { get; init; } = [];

    /// <summary>The paragraphs after everything else — « si vous n'êtes pas à l'origine de cette demande… ».</summary>
    public IReadOnlyList<string> Outro { get; init; } = [];

    /// <summary>
    /// The small print under the rule: validity, single use, what happens if it is ignored. Rendered muted and
    /// smaller, so it reads as a condition rather than as an instruction.
    /// </summary>
    public string? Note { get; init; }
}
