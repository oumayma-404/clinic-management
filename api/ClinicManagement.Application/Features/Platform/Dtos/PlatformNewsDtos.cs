namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>One row of the news form as the console sends it. Blank rows are dropped server-side.</summary>
public record PlatformNewsItemRequest(string? Emoji, string? Text);

/// <summary>
/// Who a send would reach right now: the counts the confirm states, and the very rows the send records —
/// one per address, in the audience's order — so the vendor can check the list before pressing « Envoyer ».
/// </summary>
public record PlatformNewsAudienceDto(
    int Clinics,
    int Recipients,
    int ClinicsWithoutAdminEmail,
    IReadOnlyList<PlatformNewsAudienceRecipientDto> RecipientList,
    IReadOnlyList<string> SkippedClinics);

/// <summary>One address a send would reach. `AdminName` is null when the account has none.</summary>
public record PlatformNewsAudienceRecipientDto(string ClinicName, string? AdminName, string AdminEmail);

/// <summary>The e-mail exactly as it will be sent, logo inlined so a browser can paint it.</summary>
public record PlatformNewsPreviewDto(string NewsHtml);

public record PlatformNewsTestSentDto(string TestSentTo);

/// <summary>A send recorded and queued. <c>AlreadyQueued</c> = this press was a repeat of one already recorded.</summary>
public record PlatformNewsQueuedDto(Guid BroadcastId, int Clinics, int Recipients, bool AlreadyQueued, bool InProgress);

/// <summary>One past send, for the history under the form.</summary>
public record PlatformNewsBroadcastDto(
    Guid BroadcastId,
    string Subject,
    DateTime SentAt,
    string SentByName,
    int Clinics,
    int Recipients,
    int Sent,
    int Failed,
    bool InProgress,
    bool VisibleInApp,
    IReadOnlyList<string> FailedClinics);

public record PlatformNewsVisibilityDto(Guid BroadcastId, bool VisibleInApp);
