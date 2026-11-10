using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Queries;

/// <summary>
/// The news e-mail rendered by the sender's own composer and layout, greeting the signed-in console account —
/// what « M'envoyer un test » would deliver. A query: it writes nothing and never refuses an unfinished form.
/// </summary>
public class PreviewPlatformNewsQuery : IRequest<Result<PlatformNewsPreviewDto>>
{
    public string? Subject { get; set; }
    public IReadOnlyList<PlatformNewsItemRequest>? Items { get; set; }
}

public class PreviewPlatformNewsQueryHandler
    : IRequestHandler<PreviewPlatformNewsQuery, Result<PlatformNewsPreviewDto>>
{
    private readonly IPlatformAccountRepository _accounts;
    private readonly IPlatformSessionContext _session;
    private readonly IPublicAppUrlProvider _appUrl;
    private readonly ISubscriptionPricing _pricing;
    private readonly ILogger<PreviewPlatformNewsQueryHandler> _logger;

    public PreviewPlatformNewsQueryHandler(
        IPlatformAccountRepository accounts,
        IPlatformSessionContext session,
        IPublicAppUrlProvider appUrl,
        ISubscriptionPricing pricing,
        ILogger<PreviewPlatformNewsQueryHandler> logger)
    {
        _accounts = accounts;
        _session = session;
        _appUrl = appUrl;
        _pricing = pricing;
        _logger = logger;
    }

    public async Task<Result<PlatformNewsPreviewDto>> Handle(
        PreviewPlatformNewsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await _accounts.GetByIdAsync(PlatformAccessLedger.RequireAccountId(_session), cancellationToken);
            var draft = PlatformNewsRules.ForPreview(request.Subject, request.Items);
            var content = PlatformNewsEmail.Compose(
                draft,
                account?.FullName,
                PlatformNewsEmail.SampleClinicName,
                PlatformNewsEmail.AppLink(_appUrl),
                PlatformNewsContact.From(_pricing));

            return Result<PlatformNewsPreviewDto>.Success(
                new PlatformNewsPreviewDto(PlatformNewsEmail.PreviewHtml(content)));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error rendering the news preview");
            return Result<PlatformNewsPreviewDto>.Failure("Impossible d'afficher l'aperçu.", ex);
        }
    }
}
