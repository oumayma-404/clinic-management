using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Expenses.Commands;

public class UpdateExpenseCommand : IRequest<Result<ExpenseDto>>
{
    public Guid Id { get; set; }

    /// <summary>The day in the CABINET's calendar. Nullable and required — see <see cref="ExpenseDay"/>.</summary>
    public DateTime? ExpenseDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user was
    /// editing; <c>0</c> means « not supplied » and skips the check (see <c>IUnitOfWork.SetExpectedVersion</c>).
    /// </summary>
    public uint Version { get; set; }
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
            if (ExpenseDay.Resolve(request.ExpenseDate) is not { } expenseDay)
                return Result<ExpenseDto>.Failure(ExpenseDay.Required, ExpenseDay.RequiredCode);
            if (ExpenseDay.RefuseDay(expenseDay) is { } tooFar)
                return Result<ExpenseDto>.Failure(tooFar);
            if (ExpenseDay.RefuseFields(request.Category, request.Description, request.Amount) is { } badField)
                return Result<ExpenseDto>.Failure(badField);
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
                expenseDay,
                request.Category.Trim(),
                request.Amount,
                method,
                string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());

            // Band B — a dépense is money, and two tabs editing the same one both answered 200 with « Dépense mise
            // à jour » while one amount silently replaced the other.
            _unitOfWork.SetExpectedVersion(expense, request.Version);

            await _expenseRepository.UpdateAsync(expense, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ExpenseDto>.Success(expense.ToDto());
        }
        catch (ArgumentException ex)
        {
            return Result<ExpenseDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // No `ex.Message`: an EF/Npgsql sentence is English machine text and this string is rendered verbatim.
            return Result<ExpenseDto>.Failure("Erreur lors de la mise à jour de la dépense.");
        }
    }
}
