using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Queries;

/// <summary>
/// « Journal » — the console's own access ledger, read back (<c>platform-console</c> FR-5, AC-7.3).
///
/// <para>⚠️ <b>A write-only ledger is a promise nobody can check.</b> The point of recording what a cross-cabinet
/// surface does is that somebody can read it afterwards, so this read is part of the guarantee rather than a
/// convenience on top of it.</para>
///
/// <para>⚠️ <b>It stays a console read.</b> Showing a cabinet who at the vendor looked at it is named out of scope
/// by the spec, so there is no clinic-facing endpoint here — and, like <c>AuditController</c>, the surface is
/// read-only by construction: no command exists that edits or deletes a row.</para>
///
/// <para>⚠️ <b>Omitting the paging parameters gets the FIRST PAGE, not everything</b> — the audit ledger's rule,
/// and for its reason: this table grows with every cabinet anyone opens, and there is no caller for all of it.</para>
/// </summary>
public class GetPlatformAccessLogQuery : IRequest<Result<PlatformAccessLogPageDto>>
{
    /// <summary>Narrow to one console account (AC-7.3's « who »).</summary>
    public Guid? PlatformAccountId { get; set; }

    /// <summary>Narrow to one cabinet — « qui a ouvert la fiche de ce cabinet, et quand ? ».</summary>
    public Guid? ClinicId { get; set; }

    public int? Page { get; set; }

    public int? PageSize { get; set; }
}

public class GetPlatformAccessLogQueryHandler
    : IRequestHandler<GetPlatformAccessLogQuery, Result<PlatformAccessLogPageDto>>
{
    private readonly IPlatformAccessEntryRepository _repository;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<GetPlatformAccessLogQueryHandler> _logger;

    public GetPlatformAccessLogQueryHandler(
        IPlatformAccessEntryRepository repository,
        ITenantScope tenantScope,
        ILogger<GetPlatformAccessLogQueryHandler> logger)
    {
        _repository = repository;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformAccessLogPageDto>> Handle(
        GetPlatformAccessLogQuery request, CancellationToken cancellationToken)
    {
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            var paging = PageRequest.From(request.Page, request.PageSize)
                         ?? PageRequest.Of(1, PageRequest.DefaultPageSize);

            var page = await _repository.GetPageAsync(
                request.PlatformAccountId, request.ClinicId, paging, cancellationToken);

            var actors = await _repository.GetRecordedActorsAsync(cancellationToken);

            return Result<PlatformAccessLogPageDto>.Success(new PlatformAccessLogPageDto(
                Items: page.Items.Select(ToDto).ToList(),
                Actors: actors.Select(a => new PlatformAccessActorDto(a.PlatformAccountId, a.AccountEmail)).ToList(),
                Page: page.Page,
                PageSize: page.PageSize,
                TotalCount: page.TotalCount,
                TotalPages: page.TotalPages,
                HasPreviousPage: page.HasPreviousPage,
                HasNextPage: page.HasNextPage));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading the platform access log");
            return Result<PlatformAccessLogPageDto>.Failure("Erreur lors de la lecture du journal des accès.");
        }
    }

    private static PlatformAccessEntryDto ToDto(PlatformAccessEntry entry) => new(
        EntryId: entry.Id,
        PlatformAccountId: entry.PlatformAccountId,
        AccountEmail: entry.AccountEmail,
        ClinicId: entry.ClinicId,
        ClinicName: entry.ClinicName,
        // Both the machine name and the French wording: the screen renders the label, and the raw member is what a
        // later export or a support question can be matched on without parsing prose.
        Action: entry.Action.ToString(),
        ActionLabel: PlatformAccessLabels.Action(entry.Action),
        OccurredAt: entry.OccurredAt,
        // Null on every row but a second-factor reset, where they are the only record that exists — see the DTO.
        TargetEmail: entry.TargetEmail,
        Reason: entry.Reason);
}
