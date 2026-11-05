namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>
/// One message in the console's « Retours » (guided-first-days E). Written by a cabinet's person TO the vendor —
/// never copied from a record, and the form asks for no patient name. Labels are French, resolved server-side.
/// </summary>
public sealed record PlatformFeedbackDto(
    Guid FeedbackId,
    Guid ClinicId,
    string ClinicName,
    string AuthorName,
    string AuthorRoleLabel,
    string Kind,
    string KindLabel,
    string Source,
    int? HelpRating,
    int? EaseRating,
    IReadOnlyList<string> LikedAreas,
    IReadOnlyList<string> HardAreas,
    string? LikedOther,
    string? HardOther,
    string? Message,
    string? FromScreen,
    string? ContactEmail,
    DateTime CreatedAt);

public sealed record PlatformFeedbackPageDto(
    IReadOnlyList<PlatformFeedbackDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);
