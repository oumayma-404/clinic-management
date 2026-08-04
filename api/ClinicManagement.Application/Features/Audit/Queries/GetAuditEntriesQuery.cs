using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Audit.Queries;

/// <summary>
/// « Journal d'activité » — the clinic's audit ledger, newest first, filterable and paged.
///
/// <para>Admin-only, gated by the controller's <c>AdminOnly</c> policy. That is the point of the whole feature:
/// the owner is the person who needs to answer « qui a supprimé ce patient ? », and a ledger a secretary can read
/// tells them who noticed what — which is a different, and unasked-for, product.</para>
///
/// <para><b>Paged, never unbounded.</b> Unlike the pickers and the money totals, there is no legitimate caller for
/// « every audit row of this clinic »: the table grows with every save the practice has ever made, so the read
/// always goes through <see cref="PageRequest"/>. Omitting the paging parameters gets the default page, not
/// everything — deliberately different from the list reads, where unpaged is a first-class case.</para>
/// </summary>
public class GetAuditEntriesQuery : IRequest<Result<AuditPageDto>>
{
    /// <summary>A CLR aggregate name (`Patient`, `Invoice`) as offered by <c>AuditPageDto.EntityTypes</c>.</summary>
    public string? EntityType { get; set; }

    /// <summary>One record's whole history — « tout ce qui est arrivé à ce dossier ».</summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// Inclusive calendar-day bounds in the <b>clinic's</b> zone (Tunisia, UTC+1), not UTC. They arrive as dates
    /// and are widened through <see cref="ClinicClock"/> to the first and last tick of those local days — the
    /// alternative being that « le 3 août » silently means 01:00 on the 3rd to 01:00 on the 4th, so an action taken
    /// at 00:30 files under the previous day. The same defect § 4.1 fixed across the money reads.
    /// </summary>
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>`Insert` | `Update` | `Delete`. An unparseable value is ignored, not refused — same tolerance the
    /// lab-order stage filter and the procedure-type category filter have, so a stale bookmark shows the full
    /// ledger rather than a French error about a query parameter.</summary>
    public string? Action { get; set; }

    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public class GetAuditEntriesQueryHandler : IRequestHandler<GetAuditEntriesQuery, Result<AuditPageDto>>
{
    private readonly IAuditEntryRepository _auditRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetAuditEntriesQueryHandler> _logger;

    public GetAuditEntriesQueryHandler(
        IAuditEntryRepository auditRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetAuditEntriesQueryHandler> logger)
    {
        _auditRepository = auditRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<AuditPageDto>> Handle(GetAuditEntriesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<AuditPageDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var clinicId = clinicResult.Value;

            // The clinic-local day, both ends inclusive. `LastTickOfLocalDayUtc` and not `EndOfLocalDayUtc`: the
            // latter is the next midnight (exclusive), which would put an action logged exactly at midnight into
            // both adjacent days — finding #20, re-armed every time someone reaches for the obvious helper.
            var from = request.From.HasValue ? ClinicClock.StartOfLocalDayUtc(request.From.Value) : (DateTime?)null;
            var to = request.To.HasValue ? ClinicClock.LastTickOfLocalDayUtc(request.To.Value) : (DateTime?)null;

            var page = await _auditRepository.GetFilteredAsync(
                clinicId,
                request.EntityType,
                request.EntityId,
                from,
                to,
                ParseAction(request.Action),
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            var types = await _auditRepository.GetRecordedEntityTypesAsync(clinicId, cancellationToken);

            return Result<AuditPageDto>.Success(new AuditPageDto
            {
                Items = page.Items
                    .Select(a => new AuditEntryDto
                    {
                        Id = a.Id,
                        UserId = a.UserId,
                        UserEmail = a.UserEmail,
                        ActorLabel = AuditLabels.Actor(a.UserId, a.UserEmail),
                        IsSystemActor = a.UserId.StartsWith(AuditActor.ProcessPrefix, StringComparison.Ordinal),
                        EntityType = a.EntityType,
                        EntityLabel = AuditLabels.Entity(a.EntityType),
                        EntityId = a.EntityId,
                        Action = a.Action.ToString(),
                        ActionLabel = AuditLabels.Action(a.Action),
                        ChangedFields = a.ChangedFields,
                        OccurredAt = a.OccurredAt,
                    })
                    .ToList(),
                EntityTypes = types
                    .Select(t => new AuditEntityTypeOptionDto { Value = t, Label = AuditLabels.Entity(t) })
                    // Ordered by the *French* label, because that is what the reader sees. The repository orders by
                    // the CLR name, which puts « Note d'honoraires » under I and « Dépense » under E.
                    .OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                Page = page.Page,
                PageSize = page.PageSize,
                TotalCount = page.TotalCount,
                TotalPages = page.TotalPages,
                HasPreviousPage = page.HasPreviousPage,
                HasNextPage = page.HasNextPage,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error retrieving the audit ledger.");
            return Result<AuditPageDto>.Failure("Erreur lors de la lecture du journal d'activité.");
        }
    }

    /// <summary>Tolerant on purpose — see <see cref="GetAuditEntriesQuery.Action"/>.</summary>
    private static AuditAction? ParseAction(string? value) =>
        Enum.TryParse<AuditAction>(value, ignoreCase: true, out var parsed) ? parsed : null;
}
