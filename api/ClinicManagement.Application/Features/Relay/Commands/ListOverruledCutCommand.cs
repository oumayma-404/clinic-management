using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>
/// « À reprendre » (<c>clinic-pc-copy</c> US-7, AC-7.3 → AC-7.5): a PC de secours whose cut « Reprendre la main »
/// overruled sends what it recorded during that cut. Nothing is applied — the cloud went on without it — every record
/// becomes a line a person enters again on the cloud and marks « Repris », and the PC may then copy the cloud afresh.
/// Once per cut: a repeat lists nothing twice.
/// </summary>
public sealed record ListOverruledCutCommand(RelayHandbackRequest Request, string? RelayBuild)
    : IRequest<Result<RelayHandbackResultDto>>;

public sealed class ListOverruledCutCommandHandler : IRequestHandler<ListOverruledCutCommand, Result<RelayHandbackResultDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _tenantScope;
    private readonly IRelayBuildInfo _build;
    private readonly IRelayHandbackStore _store;
    private readonly IRelayReviewItemRepository _reviews;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ListOverruledCutCommandHandler> _logger;

    public ListOverruledCutCommandHandler(
        IClinicContext clinicContext,
        IClinicRelayRepository relays,
        ITenantScope tenantScope,
        IRelayBuildInfo build,
        IRelayHandbackStore store,
        IRelayReviewItemRepository reviews,
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        ILogger<ListOverruledCutCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _relays = relays;
        _tenantScope = tenantScope;
        _build = build;
        _store = store;
        _reviews = reviews;
        _auditEntries = auditEntries;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<RelayHandbackResultDto>> Handle(ListOverruledCutCommand command, CancellationToken cancellationToken)
    {
        var opened = await Queries.RelayFeedGate.OpenAsync(
            _clinicContext, _relays, _tenantScope, _build, command.RelayBuild, cancellationToken);
        if (opened.IsFailure)
        {
            return Result<RelayHandbackResultDto>.FailureFrom(opened);
        }

        var relay = opened.Value!;
        var request = command.Request;
        if (relay.ReclaimedAtUtc is null || request.CutSinceUtc is not { } cutSince)
        {
            return Result<RelayHandbackResultDto>.Failure(RelayRefusals.NotOverruled, RelayRefusals.NotOverruledCode);
        }

        cutSince = DateTime.SpecifyKind(cutSince, DateTimeKind.Utc);
        var already = await _reviews.GetForCutAsync(relay.Id, cutSince, cancellationToken);
        if (already.Any(i => i.Kind == RelayReviewKind.ToReEnter))
        {
            return Result<RelayHandbackResultDto>.Success(new RelayHandbackResultDto(true, 0, 0, 0));
        }

        var lines = Lines(request, _store.References);
        var now = DateTime.UtcNow;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var cloud = await _store.CurrentRowsAsync(relay.ClinicId, lines.Select(l => l.Key).ToList(), cancellationToken);
            var authors = HandBackRelayCommandHandler.CabinetAuthors(request.Journal);
            var items = lines.Select(l =>
            {
                var by = authors.GetValueOrDefault(l.Key);
                return new RelayReviewItem(relay.ClinicId, relay.Id, cutSince, RelayReviewKind.ToReEnter, l.Key.Table, l.Key.Key,
                    cloud.GetValueOrDefault(l.Key), l.Json, null, null, null, now, by.By, by.At);
            }).ToList();

            if (items.Count > 0)
            {
                await _reviews.AddRangeAsync(items, cancellationToken);
            }

            await RelayJournal.StageAsync(_auditEntries, new AuditActor(relay.Subject, null), relay, AuditAction.Update,
                RelayJournal.ListedToReEnter(items.Count), now, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("US-7: the overruled cut of clinic {ClinicId} is listed « À reprendre » — {Count} record(s).",
                relay.ClinicId, items.Count);
            return Result<RelayHandbackResultDto>.Success(new RelayHandbackResultDto(false, 0, 0, items.Count));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _logger.LogError(ex, "US-7: the overruled cut of clinic {ClinicId} could not be listed.", relay.ClinicId);
            return Result<RelayHandbackResultDto>.Failure(RelayRefusals.HandbackFailed, RelayRefusals.HandbackFailedCode);
        }
    }

    /// <summary>
    /// One line per record, not per row of it: the cut's rows that point to nothing else of the cut (a note, not its
    /// lines) — accounts, the subscription and delivery state left out, as at the return.
    /// </summary>
    public static IReadOnlyList<(RelayRowKey Key, string? Json)> Lines(
        RelayHandbackRequest request, Func<string, System.Text.Json.JsonElement, IEnumerable<RelayRowKey>> references)
    {
        var rows = request.Rows
            .Where(r => !RelayHandbackRules.NeverReturned.Contains(r.Table) && !RelayHandbackRules.NotReviewed.Contains(r.Table))
            .GroupBy(r => new RelayRowKey(r.Table, r.Key))
            .ToDictionary(g => g.Key, g => g.Last());
        return rows
            .Where(r => r.Value.Row is not { } json || !references(r.Key.Table, json).Any(rows.ContainsKey))
            .OrderBy(r => r.Key.Table, StringComparer.Ordinal).ThenBy(r => r.Key.Key, StringComparer.Ordinal)
            .Select(r => (r.Key, r.Value.Row?.GetRawText()))
            .ToList();
    }
}
