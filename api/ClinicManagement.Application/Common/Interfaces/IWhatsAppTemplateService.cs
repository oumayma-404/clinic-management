using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Submitting the French reminder template on a cabinet's behalf, and reading one template's state back (FR-7,
/// FR-7a, AC-1.3). <b>There is no template call of any kind in the product before this</b> — onboarding makes four
/// Graph calls, none about templates.
///
/// <para>⚠️ <b>Nothing here throws to refuse.</b> Both members return null on failure and log, because both callers
/// are post-something-that-must-not-be-undone: the submission runs after Meta has already accepted the connection
/// (throwing would lose it), and the poll is one cabinet of a daily loop over all of them. A cabinet whose
/// submission failed reads « en attente de validation » and is picked up by the poll — which is the same state as a
/// submission Meta is genuinely still reviewing, and is why the poll is not optional.</para>
///
/// <para>⚠️ There is deliberately <b>no delete and no resubmit</b>: recovering a refused template is the vendor's
/// action (FR-7, EC-10), and an automatic resubmission loop against Meta's review queue is what EC-10 forbids.</para>
/// </summary>
public interface IWhatsAppTemplateService
{
    /// <summary>
    /// Submits <c>WhatsAppReminderTemplate</c> to this cabinet's WABA. Null when the call did not succeed.
    ///
    /// <para>A template already existing under that name is <b>not</b> a failure: the same cabinet reconnecting is
    /// the ordinary case, so the implementation reads the existing one back instead.</para>
    /// </summary>
    Task<WhatsAppTemplateState?> SubmitReminderTemplateAsync(
        string wabaId, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the cabinet's reminder template back — by its Meta id where we have one, else by name. Null when the
    /// call did not succeed <b>or</b> when the WABA holds no such template, which are the same answer to the poll:
    /// leave the stored state alone rather than assert something about a template we could not see.
    /// </summary>
    Task<WhatsAppTemplateState?> ReadReminderTemplateAsync(
        string wabaId, string accessToken, string? templateId, CancellationToken cancellationToken = default);
}

/// <summary>
/// What Meta says about one template right now. <paramref name="Category"/> is carried as the <b>raw</b> word
/// rather than an enum: Meta auto-recategorises and may return a value this product has never heard of, and FR-7b
/// needs such a value to be <i>reportable</i> rather than unparseable.
/// </summary>
public sealed record WhatsAppTemplateState(WhatsAppTemplateStatus Status, string? Category, string? TemplateId);
