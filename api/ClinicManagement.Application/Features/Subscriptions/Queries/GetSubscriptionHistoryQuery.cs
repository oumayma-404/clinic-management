using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Subscriptions.Queries;

/// <summary>
/// What the cabinet has paid: one page of its subscription ledger, newest first (AC-2.3).
///
/// <para><b>Administrator only</b>, gated by the controller's <c>AdminOnly</c> policy. That is the one thing US-2
/// keeps restricted — the screen itself is open to every role (AC-2.2), because reception meets the refusal and has
/// to be able to read why; what the practice has paid its software vendor, and by which cheque, is the owner's.</para>
///
/// <para><b>⚠️ The whole ledger is folded and the page is cut afterwards</b>, through
/// <see cref="PagedResult{T}.FromSource"/>. Every entry's « période couverte » is a function of the non-cancelled
/// entries recorded before it, so a SQL <c>OFFSET</c> would hand the fold a window and the spans on page 2 would
/// restart from that window's first row instead of continuing page 1's. It is the same reason « Créances » and the
/// « extrait de caisse » page in memory: no single query knows a row's place in the answer.</para>
/// </summary>
public class GetSubscriptionHistoryQuery : IRequest<Result<SubscriptionHistoryPageDto>>
{
    public int? Page { get; set; }

    public int? PageSize { get; set; }
}

public class GetSubscriptionHistoryQueryHandler
    : IRequestHandler<GetSubscriptionHistoryQuery, Result<SubscriptionHistoryPageDto>>
{
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetSubscriptionHistoryQueryHandler> _logger;

    public GetSubscriptionHistoryQueryHandler(
        IClinicSubscriptionRepository subscriptions,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetSubscriptionHistoryQueryHandler> logger)
    {
        _subscriptions = subscriptions;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<SubscriptionHistoryPageDto>> Handle(
        GetSubscriptionHistoryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<SubscriptionHistoryPageDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var entries = await _subscriptions.GetEntriesAsync(clinicResult.Value, cancellationToken);
            var (_, spans) = SubscriptionLedger.FoldWithSpans(entries.Select(e => e.ToLedgerEntry()));
            var spanById = spans.ToDictionary(s => s.EntryId);

            // Newest first, like the audit ledger and the notification feed: the entry an owner came to check is the
            // one they just paid. The FOLD's order is the ledger's own and is applied inside FoldWithSpans, so
            // reversing here for display cannot move a single date.
            var rows = entries
                .OrderByDescending(e => e.RecordedAtUtc)
                .ThenByDescending(e => e.Id)
                .Select(e => new SubscriptionPeriodDto
                {
                    Id = e.Id,
                    Kind = e.Kind.ToString(),
                    KindLabel = SubscriptionLabels.PeriodKind(e.Kind),
                    FromDay = spanById.TryGetValue(e.Id, out var span) ? span.FromDay : null,
                    ThroughDay = span?.ThroughDay,
                    Amount = e.Amount,
                    Method = e.Method?.ToString(),
                    MethodLabel = e.Method is { } method ? SubscriptionLabels.PaymentMethod(method) : null,
                    Reference = e.Reference,
                    Note = e.Note,
                    RecordedAt = e.RecordedAtUtc,
                    RecordedBy = e.RecordedBy,
                    IsCancelled = e.IsCancelled,
                    CancelledAt = e.CancelledAtUtc,
                    CancelReason = e.CancelReason,
                })
                .ToList();

            var page = PagedResult<SubscriptionPeriodDto>.FromSource(
                rows, PageRequest.From(request.Page, request.PageSize));

            return Result<SubscriptionHistoryPageDto>.Success(new SubscriptionHistoryPageDto
            {
                Items = page.Items.ToList(),
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
            _logger.LogError(ex, "Error reading the clinic subscription history.");
            return Result<SubscriptionHistoryPageDto>.Failure(
                "Erreur lors de la lecture de l'historique des paiements.");
        }
    }
}
