using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Expenses.Commands;

public class CreateExpenseCommand : IRequest<Result<ExpenseDto>>
{
    public DateTime ExpenseDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CreateExpenseCommandHandler : IRequestHandler<CreateExpenseCommand, Result<ExpenseDto>>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public CreateExpenseCommandHandler(
        IExpenseRepository expenseRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _expenseRepository = expenseRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ExpenseDto>> Handle(CreateExpenseCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Category))
                return Result<ExpenseDto>.Failure("La catégorie est requise.");
            if (request.Amount <= 0)
                return Result<ExpenseDto>.Failure("Le montant de la dépense doit être supérieur à 0.");
            if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method))
                return Result<ExpenseDto>.Failure("Mode de paiement invalide.");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<ExpenseDto>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var expense = new Expense(
                Guid.NewGuid(),
                clinic.Value,
                request.ExpenseDate,
                request.Category.Trim(),
                request.Amount,
                method,
                string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());

            await _expenseRepository.AddAsync(expense, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ExpenseDto>.Success(expense.ToDto());
        }
        catch (ArgumentException ex)
        {
            return Result<ExpenseDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            return Result<ExpenseDto>.Failure($"Erreur lors de la création de la dépense : {ex.Message}");
        }
    }
}
