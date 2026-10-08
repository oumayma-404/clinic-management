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
/// AC-9.4, step 1: a restored cloud hands its PC de secours each row's hash for one table, and the PC sends back only
/// the rows the cloud lacks or holds otherwise.
/// </summary>
public sealed record GetRelayGapHashesQuery(string Table, string? RelayBuild) : IRequest<Result<IReadOnlyList<RelayRowHashDto>>>;

public sealed class GetRelayGapHashesQueryHandler : IRequestHandler<GetRelayGapHashesQuery, Result<IReadOnlyList<RelayRowHashDto>>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _tenantScope;
    private readonly IRelayBuildInfo _build;
    private readonly IClinicRelayRowStore _rows;

    public GetRelayGapHashesQueryHandler(
        IClinicContext clinicContext, IClinicRelayRepository relays, ITenantScope tenantScope, IRelayBuildInfo build,
        IClinicRelayRowStore rows)
    {
        _clinicContext = clinicContext;
        _relays = relays;
        _tenantScope = tenantScope;
        _build = build;
        _rows = rows;
    }

    public async Task<Result<IReadOnlyList<RelayRowHashDto>>> Handle(GetRelayGapHashesQuery request, CancellationToken cancellationToken)
    {
        var opened = await RelayFeedGate.OpenAsync(_clinicContext, _relays, _tenantScope, _build, request.RelayBuild, cancellationToken);
        if (opened.IsFailure)
        {
            return Result<IReadOnlyList<RelayRowHashDto>>.FailureFrom(opened);
        }

        if (string.IsNullOrWhiteSpace(request.Table) || RelayHandbackRules.NeverReturned.Contains(request.Table))
        {
            return Result<IReadOnlyList<RelayRowHashDto>>.Failure(RelayRefusals.UnknownGapTable, RelayRefusals.InvalidRequestCode);
        }

        try
        {
            var hashes = await _rows.RowHashesAsync(opened.Value!.ClinicId, request.Table, cancellationToken);
            return Result<IReadOnlyList<RelayRowHashDto>>.Success(
                hashes.Select(h => new RelayRowHashDto(h.Key, h.Value)).OrderBy(h => h.Key, StringComparer.Ordinal).ToList());
        }
        catch (InvalidOperationException)
        {
            return Result<IReadOnlyList<RelayRowHashDto>>.Failure(RelayRefusals.UnknownGapTable, RelayRefusals.InvalidRequestCode);
        }
    }
}

/// <summary>
/// AC-9.4, step 2 (<c>clinic-pc-copy</c> EC-21): what a restore lost — the minutes before the loss, and a cut's work when
/// the PC held the saves — sent back by the PC de secours, once per gap. A row this cloud changed again since its
/// restore point keeps the cloud's version and is listed with the PC's beside it (« Retours du PC de secours »); every
/// other row is the PC's, the cloud's own settings kept (AC-5.7). Accounts and the subscription never travel (FR-11).
/// The cloud is read-only for the cabinet until this lands (<see cref="ClinicRelay.IsRecoveringGap"/>), so no note here
/// can take a number the PC already gave.
/// </summary>
public sealed record ReturnRelayGapCommand(RelayGapRequest Request, string? RelayBuild) : IRequest<Result<RelayHandbackResultDto>>;

public sealed class ReturnRelayGapCommandHandler : IRequestHandler<ReturnRelayGapCommand, Result<RelayHandbackResultDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _tenantScope;
    private readonly IRelayBuildInfo _build;
    private readonly IRelayHandbackStore _store;
    private readonly IRelayReviewItemRepository _reviews;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReturnRelayGapCommandHandler> _logger;
    private readonly IRelayReturnAftermath? _aftermath;

    public ReturnRelayGapCommandHandler(
        IClinicContext clinicContext,
        IClinicRelayRepository relays,
        ITenantScope tenantScope,
        IRelayBuildInfo build,
        IRelayHandbackStore store,
        IRelayReviewItemRepository reviews,
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        ILogger<ReturnRelayGapCommandHandler> logger,
        IRelayReturnAftermath? aftermath = null)
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
        _aftermath = aftermath;
    }

    public async Task<Result<RelayHandbackResultDto>> Handle(ReturnRelayGapCommand command, CancellationToken cancellationToken)
    {
        var opened = await RelayFeedGate.OpenAsync(
            _clinicContext, _relays, _tenantScope, _build, command.RelayBuild, cancellationToken);
        if (opened.IsFailure)
        {
            return Result<RelayHandbackResultDto>.FailureFrom(opened);
        }

        var relay = opened.Value!;
        var request = command.Request;
        if (relay.LastGapId == request.GapId)
        {
            return Result<RelayHandbackResultDto>.Success(new RelayHandbackResultDto(true, 0, 0, 0));
        }

        var rows = Rows(request);
        var now = DateTime.UtcNow;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var keys = rows.Select(r => new RelayRowKey(r.Table, r.Key)).ToList();
            var changed = await _store.ChangedSinceRestoreAsync(relay.ClinicId, keys, cancellationToken);
            var (apply, kept) = Split(rows, changed);
            var listed = kept.Where(r => !RelayHandbackRules.NotReviewed.Contains(r.Table)).ToList();

            await _store.ApplyReturnAsync(relay.ClinicId,
                new RelayHandbackRequest(request.GapId, 0, null, Array.Empty<RelayHandbackChange>(), apply,
                    Array.Empty<RelayHandbackJournalEntry>(), Array.Empty<RelaySignInTrace>(), Array.Empty<RelayRecoveryCodeUse>()),
                new RelayHandbackPlan(apply, Array.Empty<RelayRowKey>(), Array.Empty<RelayPlannedReview>()),
                cancellationToken);

            if (listed.Count > 0)
            {
                var listedKeys = listed.Select(r => new RelayRowKey(r.Table, r.Key)).ToList();
                var cloud = await _store.CurrentRowsAsync(relay.ClinicId, listedKeys, cancellationToken);
                var authors = await _store.AuthorsAsync(relay.ClinicId, listedKeys, cancellationToken);
                await _reviews.AddRangeAsync(listed.Select(r =>
                {
                    var key = new RelayRowKey(r.Table, r.Key);
                    return new RelayReviewItem(relay.ClinicId, relay.Id, now, RelayReviewKind.KeptAfterRestore, r.Table, r.Key,
                        cloud.GetValueOrDefault(key), r.Row?.GetRawText(), null, null, authors.GetValueOrDefault(key), now);
                }).ToList(), cancellationToken);
            }

            relay.RecordGapReturned(request.GapId, apply.Count, now);
            await RelayJournal.StageAsync(_auditEntries, new AuditActor(relay.Subject, null), relay, AuditAction.Update,
                RelayJournal.GapReturned(apply.Count), now, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _logger.LogError(ex, "AC-9.4: the gap {GapId} of clinic {ClinicId} could not be applied.", request.GapId, relay.ClinicId);
            return Result<RelayHandbackResultDto>.Failure(RelayRefusals.HandbackFailed, RelayRefusals.HandbackFailedCode);
        }

        _logger.LogWarning("AC-9.4: the restored cloud took back {Applied} row(s) of clinic {ClinicId} from its PC de secours; "
                           + "{Listed} kept as the cloud changed them since.", relay.LastGapRows, relay.ClinicId,
            rows.Count - relay.LastGapRows);
        if (_aftermath is not null)
        {
            // After the commit, best-effort: the visits it brought back go to Google, open screens refresh.
            await _aftermath.AfterReturnAsync(relay.ClinicId, now, cancellationToken);
        }

        return Result<RelayHandbackResultDto>.Success(
            new RelayHandbackResultDto(false, relay.LastGapRows, 0, rows.Count - relay.LastGapRows));
    }

    /// <summary>One row per key (the last sent), never a deletion — a gap gives back, it never removes — and never an account.</summary>
    public static IReadOnlyList<RelayRow> Rows(RelayGapRequest request) =>
        request.Rows
            .Where(r => r.Row is not null && !RelayHandbackRules.NeverReturned.Contains(r.Table))
            .GroupBy(r => new RelayRowKey(r.Table, r.Key))
            .Select(g => g.Last())
            .ToList();

    /// <summary>The PC's version, unless this cloud changed the row since its restore — then the cloud's stays.</summary>
    public static (IReadOnlyList<RelayRow> Apply, IReadOnlyList<RelayRow> Kept) Split(
        IReadOnlyList<RelayRow> rows, IReadOnlySet<RelayRowKey> changedSinceRestore)
    {
        var apply = rows.Where(r => !changedSinceRestore.Contains(new RelayRowKey(r.Table, r.Key))).ToList();
        var kept = rows.Where(r => changedSinceRestore.Contains(new RelayRowKey(r.Table, r.Key))).ToList();
        return (apply, kept);
    }
}
