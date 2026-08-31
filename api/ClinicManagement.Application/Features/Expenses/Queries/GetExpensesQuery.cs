using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Billing;
using ClinicManagement.Application.Features.Billing.Queries;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Expenses.Queries;

public class GetExpensesQuery : IRequest<Result<PagedResult<ExpenseDto>>>
{
    /// <summary>
    /// The window as bare clinic-local calendar days, resolved by <see cref="CaissePeriod"/> (AC-6).
    ///
    /// <para>They are here — and not only on the two <c>billing/caisse</c> reads — because la caisse renders the
    /// dépenses table <b>inside the same window</b> as the totals and the extrait. Leaving this one endpoint on
    /// client-computed instants would have meant the page composing its period twice, in two conventions, with the
    /// money-out list silently answering for a different day from the money-out figure above it.</para>
    /// </summary>
    public string? FromDay { get; set; }

    /// <inheritdoc cref="FromDay"/>
    public string? ToDay { get; set; }

    /// <inheritdoc cref="GetCaisseSummaryQuery.From"/>
    public DateTime? From { get; set; }

    /// <inheritdoc cref="GetCaisseSummaryQuery.From"/>
    public DateTime? To { get; set; }

    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>Free-text filter, matched in SQL across the whole clinic — never only the requested page.</summary>
    public string? SearchTerm { get; set; }
}

public class GetExpensesQueryHandler : IRequestHandler<GetExpensesQuery, Result<PagedResult<ExpenseDto>>>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetExpensesQueryHandler(
        IExpenseRepository expenseRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _expenseRepository = expenseRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PagedResult<ExpenseDto>>> Handle(GetExpensesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<PagedResult<ExpenseDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            // ⚠️ Only resolved when the caller actually asked for a window. Unlike the two caisse reads, « no dates »
            // here means « toutes les dépenses » — the /expenses list is also read unbounded — so defaulting to
            // today the way CaissePeriod does would silently turn the full list into one day's.
            DateTime? from = request.From;
            DateTime? to = request.To;
            if (!string.IsNullOrWhiteSpace(request.FromDay) || !string.IsNullOrWhiteSpace(request.ToDay))
            {
                var period = CaissePeriod.Resolve(request.FromDay, request.ToDay, null, null);
                if (period.IsFailure)
                    return Result<PagedResult<ExpenseDto>>.FailureFrom(period);
                (from, to) = (period.Value!.From, period.Value.To);
            }

            var page = await _expenseRepository.GetByClinicIdAsync(
                clinic.Value,
                from,
                to,
                request.SearchTerm,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            return Result<PagedResult<ExpenseDto>>.Success(page.Map(e => e.ToDto()));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PagedResult<ExpenseDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
