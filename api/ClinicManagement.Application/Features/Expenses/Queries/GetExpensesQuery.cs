using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Expenses.Queries;

public class GetExpensesQuery : IRequest<Result<PagedResult<ExpenseDto>>>
{
    public DateTime? From { get; set; }
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

            var page = await _expenseRepository.GetByClinicIdAsync(
                clinic.Value,
                request.From,
                request.To,
                request.SearchTerm,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            return Result<PagedResult<ExpenseDto>>.Success(page.Map(e => e.ToDto()));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PagedResult<ExpenseDto>>.Failure($"Erreur lors de la récupération des dépenses : {ex.Message}");
        }
    }
}
