using ClinicManagement.Application.Common.Exceptions;
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
/// D18 phase 1 (US-5, FR-6): the PC de secours hands the cut's work back. Applied once, in one transaction — the rows,
/// the cloud's log of them, the merged sign-in traces, the journal and the review list — while the cloud stays fenced:
/// the PC may still be taking saves until it hears this answer. It releases the cloud on its next heartbeat (phase 2).
/// </summary>
public sealed record HandBackRelayCommand(RelayHandbackRequest Request, string? RelayBuild)
    : IRequest<Result<RelayHandbackResultDto>>;

public sealed class HandBackRelayCommandHandler : IRequestHandler<HandBackRelayCommand, Result<RelayHandbackResultDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _tenantScope;
    private readonly IRelayBuildInfo _build;
    private readonly IRelayHandbackStore _store;
    private readonly IRelayReviewItemRepository _reviews;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<HandBackRelayCommandHandler> _logger;

    public HandBackRelayCommandHandler(
        IClinicContext clinicContext,
        IClinicRelayRepository relays,
        ITenantScope tenantScope,
        IRelayBuildInfo build,
        IRelayHandbackStore store,
        IRelayReviewItemRepository reviews,
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        ILogger<HandBackRelayCommandHandler> logger)
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

    public async Task<Result<RelayHandbackResultDto>> Handle(HandBackRelayCommand command, CancellationToken cancellationToken)
    {
        var opened = await RelayFeedGate.OpenAsync(
            _clinicContext, _relays, _tenantScope, _build, command.RelayBuild, cancellationToken);
        if (opened.IsFailure)
        {
            return Result<RelayHandbackResultDto>.FailureFrom(opened);
        }

        var relay = opened.Value!;
        var request = command.Request;

        // A repeat of a handback already applied — its answer was lost — is answered as it was, applying nothing.
        if (relay.HandbackAppliedId == request.HandbackId)
        {
            return Result<RelayHandbackResultDto>.Success(new RelayHandbackResultDto(true, 0, 0, 0));
        }

        if (relay.PcHoldingSinceUtc is null)
        {
            return Result<RelayHandbackResultDto>.Failure(RelayRefusals.NotHolding, RelayRefusals.NotHoldingCode);
        }

        var clinicId = relay.ClinicId;
        var now = DateTime.UtcNow;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var cloudChanges = await _store.CloudChangesAfterAsync(clinicId, Math.Max(0, request.BaseAppliedSeq), cancellationToken);
            var plan = RelayHandbackPlanner.Plan(request, cloudChanges, _store.References);

            // The cloud's versions are read before anything is applied: the list must show what the cabinet's overwrote.
            var reviewKeys = plan.Review.Select(r => r.CloudKey ?? r.Key).Distinct().ToList();
            var cloudVersions = await _store.CurrentRowsAsync(clinicId, reviewKeys, cancellationToken);
            var authors = await _store.AuthorsAsync(clinicId, reviewKeys, cancellationToken);

            await _store.ApplyReturnAsync(clinicId, request, plan, cancellationToken);

            var cutSince = request.CutSinceUtc ?? relay.PcHoldingSinceUtc.Value;
            var listed = await StageReviewAsync(relay, cutSince, request, plan, cloudVersions, authors, now, cancellationToken);
            await StageJournalAsync(clinicId, request, cancellationToken);

            relay.RecordHandbackApplied(request.HandbackId, request.CutSinceUtc, now);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation(
                "D18: handback {HandbackId} applied for clinic {ClinicId} — {Applied} row(s), {Dropped} duplicate(s) dropped, "
                + "{Listed} listed; the cloud waits for the PC to confirm.",
                request.HandbackId, clinicId, plan.Apply.Count, plan.Dropped.Count, listed);
            return Result<RelayHandbackResultDto>.Success(
                new RelayHandbackResultDto(false, plan.Apply.Count, plan.Dropped.Count, listed));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _logger.LogError(ex, "D18: handback {HandbackId} for clinic {ClinicId} could not be applied.", request.HandbackId, clinicId);
            return Result<RelayHandbackResultDto>.Failure(RelayRefusals.HandbackFailed, RelayRefusals.HandbackFailedCode);
        }
    }

    /// <summary>A record already listed for this cut is not listed again — the first versions are the ones worth reading.</summary>
    private async Task<int> StageReviewAsync(
        ClinicRelay relay, DateTime cutSince, RelayHandbackRequest request, RelayHandbackPlan plan,
        IReadOnlyDictionary<RelayRowKey, string> cloudVersions, IReadOnlyDictionary<RelayRowKey, string> authors,
        DateTime now, CancellationToken cancellationToken)
    {
        var already = (await _reviews.GetForCutAsync(relay.Id, cutSince, cancellationToken))
            .Select(i => (i.Kind, i.Table, i.EntityKey))
            .ToHashSet();
        var cabinet = request.Rows.GroupBy(r => new RelayRowKey(r.Table, r.Key))
            .ToDictionary(g => g.Key, g => g.Last().Row?.GetRawText());

        var items = new List<RelayReviewItem>();
        foreach (var line in plan.Review)
        {
            if (!already.Add((line.Kind, line.Key.Table, line.Key.Key)))
            {
                continue;
            }

            var cloudKey = line.CloudKey ?? line.Key;
            items.Add(new RelayReviewItem(
                relay.ClinicId, relay.Id, cutSince, line.Kind, line.Key.Table, line.Key.Key,
                cloudVersions.GetValueOrDefault(cloudKey),
                line.Kind == RelayReviewKind.CloudOnly ? null : cabinet.GetValueOrDefault(line.Key),
                line.CloudKey?.Key,
                line.CloudChangedAtUtc,
                authors.GetValueOrDefault(cloudKey),
                now));
        }

        if (items.Count > 0)
        {
            await _reviews.AddRangeAsync(items, cancellationToken);
        }

        return items.Count;
    }

    /// <summary>AC-5.5: each journal row the cabinet wrote on its PC, under its author, marked « via PC de secours ».</summary>
    private Task StageJournalAsync(Guid clinicId, RelayHandbackRequest request, CancellationToken cancellationToken)
    {
        var entries = request.Journal
            .Where(e => !string.IsNullOrWhiteSpace(e.UserId) && !string.IsNullOrWhiteSpace(e.EntityType)
                        && !string.IsNullOrWhiteSpace(e.EntityId) && Enum.IsDefined(typeof(AuditAction), e.Action))
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new AuditEntry(clinicId, e.UserId, e.UserEmail, e.EntityType, e.EntityId, (AuditAction)e.Action,
                RelayJournal.MarkViaRelay(e.ChangedFields), DateTime.SpecifyKind(e.OccurredAtUtc, DateTimeKind.Utc),
                e.IsDeclaredGap))
            .ToList();
        return entries.Count == 0 ? Task.CompletedTask : _auditEntries.AddRangeAsync(entries, cancellationToken);
    }
}

/// <summary>D18: which of the files the cut's rows name the cloud does not hold yet — sent before the rows.</summary>
public sealed record GetMissingHandbackFilesQuery(IReadOnlyList<string> Keys, string? RelayBuild)
    : IRequest<Result<IReadOnlyList<string>>>;

public sealed class GetMissingHandbackFilesQueryHandler
    : IRequestHandler<GetMissingHandbackFilesQuery, Result<IReadOnlyList<string>>>
{
    public const int MaxKeys = 5000;

    private readonly IClinicContext _clinicContext;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _tenantScope;
    private readonly IRelayBuildInfo _build;
    private readonly IFileStorage _files;

    public GetMissingHandbackFilesQueryHandler(
        IClinicContext clinicContext, IClinicRelayRepository relays, ITenantScope tenantScope, IRelayBuildInfo build,
        IFileStorage files)
    {
        _clinicContext = clinicContext;
        _relays = relays;
        _tenantScope = tenantScope;
        _build = build;
        _files = files;
    }

    public async Task<Result<IReadOnlyList<string>>> Handle(GetMissingHandbackFilesQuery request, CancellationToken cancellationToken)
    {
        var opened = await RelayFeedGate.OpenAsync(_clinicContext, _relays, _tenantScope, _build, request.RelayBuild, cancellationToken);
        if (opened.IsFailure)
        {
            return Result<IReadOnlyList<string>>.FailureFrom(opened);
        }

        var missing = new List<string>();
        foreach (var key in request.Keys.Distinct(StringComparer.Ordinal).Take(MaxKeys))
        {
            if (RelayHandbackFiles.IsClinicKey(opened.Value!.ClinicId, key) && !await _files.ExistsAsync(key, cancellationToken))
            {
                missing.Add(key);
            }
        }

        return Result<IReadOnlyList<string>>.Success(missing);
    }
}

/// <summary>
/// D18: one file made on the PC during the cut, stored under the key its row names — only a key under this cabinet's own
/// prefix, and only where nothing is stored yet: a return adds files, it never replaces one.
/// </summary>
public sealed record StoreHandbackFileCommand(string Key, Stream Content, string? RelayBuild) : IRequest<Result>;

public sealed class StoreHandbackFileCommandHandler : IRequestHandler<StoreHandbackFileCommand, Result>
{
    private readonly IClinicContext _clinicContext;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _tenantScope;
    private readonly IRelayBuildInfo _build;
    private readonly IFileStorage _files;

    public StoreHandbackFileCommandHandler(
        IClinicContext clinicContext, IClinicRelayRepository relays, ITenantScope tenantScope, IRelayBuildInfo build,
        IFileStorage files)
    {
        _clinicContext = clinicContext;
        _relays = relays;
        _tenantScope = tenantScope;
        _build = build;
        _files = files;
    }

    public async Task<Result> Handle(StoreHandbackFileCommand request, CancellationToken cancellationToken)
    {
        var opened = await RelayFeedGate.OpenAsync(_clinicContext, _relays, _tenantScope, _build, request.RelayBuild, cancellationToken);
        if (opened.IsFailure)
        {
            return Result.Failure(opened.Error!, opened.Code);
        }

        if (!RelayHandbackFiles.IsClinicKey(opened.Value!.ClinicId, request.Key))
        {
            return Result.Failure(RelayRefusals.InvalidKey, RelayRefusals.InvalidRequestCode);
        }

        if (!await _files.ExistsAsync(request.Key, cancellationToken))
        {
            await _files.RestoreAtKeyAsync(request.Content, "application/octet-stream", request.Key, cancellationToken);
        }

        return Result.Success();
    }
}

/// <summary>A file the return may bring: one under this cabinet's own storage prefix, with no way out of it.</summary>
public static class RelayHandbackFiles
{
    public static bool IsClinicKey(Guid clinicId, string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.StartsWith($"clinics/{clinicId:D}/", StringComparison.Ordinal)
        && !key.Contains("..", StringComparison.Ordinal)
        && !key.Contains('\\')
        && key.Length <= 1024;
}
