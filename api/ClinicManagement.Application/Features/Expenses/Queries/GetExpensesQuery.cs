using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Expenses.Queries;

public class GetExpensesQuery : IRequest<Result<IEnumerable<ExpenseDto>>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class GetExpensesQueryHandler : IRequestHandler<GetExpensesQuery, Result<IEnumerable<ExpenseDto>>>
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

    public async Task<Result<IEnumerable<ExpenseDto>>> Handle(GetExpensesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<IEnumerable<ExpenseDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var expenses = await _expenseRepository.GetByClinicIdAsync(clinic.Value, request.From, request.To, cancellationToken);
            return Result<IEnumerable<ExpenseDto>>.Success(expenses.Select(e => e.ToDto()));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<ExpenseDto>>.Failure($"Erreur lors de la récupération des dépenses : {ex.Message}");
        }
    }
}
