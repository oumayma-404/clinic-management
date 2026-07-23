using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Expenses.Commands;

public class UpdateExpenseCommand : IRequest<Result<ExpenseDto>>
{
    public Guid Id { get; set; }
    public DateTime ExpenseDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateExpenseCommandHandler : IRequestHandler<UpdateExpenseCommand, Result<ExpenseDto>>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateExpenseCommandHandler(
        IExpenseRepository expenseRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _expenseRepository = expenseRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ExpenseDto>> Handle(UpdateExpenseCommand request, CancellationToken cancellationToken)
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

            var expense = await _expenseRepository.GetByIdAsync(request.Id, cancellationToken);
            if (expense == null || expense.ClinicId != clinic.Value)
                return Result<ExpenseDto>.Failure("Dépense introuvable.");

            expense.Update(
                request.ExpenseDate,
                request.Category.Trim(),
                request.Amount,
                method,
                string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());

            await _expenseRepository.UpdateAsync(expense, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ExpenseDto>.Success(expense.ToDto());
        }
        catch (ArgumentException ex)
        {
            return Result<ExpenseDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            return Result<ExpenseDto>.Failure($"Erreur lors de la mise à jour de la dépense : {ex.Message}");
        }
    }
}
