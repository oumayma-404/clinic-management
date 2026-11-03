using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>One « Retours » row: the message, and who wrote it from which cabinet (names read at query time).</summary>
public sealed record ProductFeedbackRow(
    ProductFeedback Feedback,
    string ClinicName,
    string? AuthorName,
    string? AuthorEmail);

/// <summary>The messages a cabinet's people send the vendor, and each person's week-one question state (guided-first-days E).</summary>
public interface IFeedbackRepository
{
    Task AddAsync(ProductFeedback feedback, CancellationToken cancellationToken = default);

    /// <summary>
    /// The console's « Retours », newest first (then by id, so a page boundary never shows a row twice). Only
    /// meaningful under a system-wide scope — the table is tenant-filtered.
    /// </summary>
    Task<PagedResult<ProductFeedbackRow>> GetPageAsync(
        string? kind, PageRequest paging, CancellationToken cancellationToken = default);

    Task<UserFeedbackPrompt?> GetPromptAsync(string userId, CancellationToken cancellationToken = default);

    Task AddPromptAsync(UserFeedbackPrompt prompt, CancellationToken cancellationToken = default);
}
