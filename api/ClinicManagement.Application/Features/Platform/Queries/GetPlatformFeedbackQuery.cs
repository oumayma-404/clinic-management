using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Feedback;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Queries;

/// <summary>The console's « Retours »: every cabinet's feedback, newest first, optionally one kind (guided-first-days E).</summary>
public class GetPlatformFeedbackQuery : IRequest<Result<PlatformFeedbackPageDto>>
{
    /// <summary><c>idee</c> · <c>probleme</c> · <c>question</c> · <c>avis</c>; an unknown value is ignored, not refused.</summary>
    public string? Kind { get; set; }

    public int? Page { get; set; }

    public int? PageSize { get; set; }
}

public class GetPlatformFeedbackQueryHandler : IRequestHandler<GetPlatformFeedbackQuery, Result<PlatformFeedbackPageDto>>
{
    private readonly IFeedbackRepository _repository;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<GetPlatformFeedbackQueryHandler> _logger;

    public GetPlatformFeedbackQueryHandler(
        IFeedbackRepository repository,
        ITenantScope tenantScope,
        ILogger<GetPlatformFeedbackQueryHandler> logger)
    {
        _repository = repository;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformFeedbackPageDto>> Handle(
        GetPlatformFeedbackQuery request, CancellationToken cancellationToken)
    {
        // A filtered table read with no cross-clinic scope returns nothing — « aucun retour » would then be a lie.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            var paging = PageRequest.From(request.Page, request.PageSize)
                         ?? PageRequest.Of(1, PageRequest.DefaultPageSize);
            var page = await _repository.GetPageAsync(FeedbackRules.ParseKind(request.Kind), paging, cancellationToken);

            var items = page.Items.Select(row =>
            {
                var f = row.Feedback;
                return new PlatformFeedbackDto(
                    FeedbackId: f.Id,
                    ClinicId: f.ClinicId,
                    ClinicName: row.ClinicName,
                    AuthorName: row.AuthorName ?? "Compte supprimé",
                    AuthorRoleLabel: FeedbackRules.RoleLabel(f.AuthorRole),
                    Kind: f.Kind,
                    KindLabel: FeedbackRules.KindLabels.GetValueOrDefault(f.Kind, f.Kind),
                    Source: f.Source,
                    HelpRating: f.HelpRating,
                    EaseRating: f.EaseRating,
                    LikedAreas: f.LikedAreas().Select(Label).ToList(),
                    HardAreas: f.HardAreas().Select(Label).ToList(),
                    Message: f.Message,
                    FromScreen: f.FromScreen,
                    // The address only when the person agreed to be contacted.
                    ContactEmail: f.MayContact ? row.AuthorEmail : null,
                    CreatedAt: f.CreatedAt);
            }).ToList();

            return Result<PlatformFeedbackPageDto>.Success(new PlatformFeedbackPageDto(
                items, page.Page, page.PageSize, page.TotalCount, page.TotalPages, page.HasPreviousPage, page.HasNextPage));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading the platform feedback list");
            return Result<PlatformFeedbackPageDto>.Failure("Erreur lors de la lecture des retours.");
        }
    }

    private static string Label(string area) => FeedbackRules.AreaLabels.GetValueOrDefault(area, area);
}
