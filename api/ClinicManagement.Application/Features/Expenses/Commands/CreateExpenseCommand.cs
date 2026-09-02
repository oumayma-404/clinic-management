using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Expenses.Commands;

public class CreateExpenseCommand : IRequest<Result<ExpenseDto>>
{
    /// <summary>
    /// The day in the CABINET's calendar. Nullable and required — see <see cref="ExpenseDay"/> for why both halves
    /// of that sentence were defects.
    /// </summary>
    public DateTime? ExpenseDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// « Répéter chaque mois ». The dépense being recorded is the series' FIRST occurrence — its day becomes the
    /// series' day of the month and its month the marker the posting pass starts after — so ticking the switch
    /// costs one tap and no second form, and cannot post the month the user has just typed twice.
    /// </summary>
    public bool RepeatMonthly { get; set; }
}

public class CreateExpenseCommandHandler : IRequestHandler<CreateExpenseCommand, Result<ExpenseDto>>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IRecurringExpenseRepository _recurringExpenseRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public CreateExpenseCommandHandler(
        IExpenseRepository expenseRepository,
        IRecurringExpenseRepository recurringExpenseRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _expenseRepository = expenseRepository;
        _recurringExpenseRepository = recurringExpenseRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ExpenseDto>> Handle(CreateExpenseCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (ExpenseDay.Resolve(request.ExpenseDate) is not { } expenseDay)
                return Result<ExpenseDto>.Failure(ExpenseDay.Required, ExpenseDay.RequiredCode);
            if (ExpenseDay.RefuseDay(expenseDay) is { } tooFar)
                return Result<ExpenseDto>.Failure(tooFar);
            if (ExpenseDay.RefuseFields(request.Category, request.Description, request.Amount) is { } badField)
                return Result<ExpenseDto>.Failure(badField);
            if (string.IsNullOrWhiteSpace(request.Category))
                return Result<ExpenseDto>.Failure("La catégorie est requise.");
            if (request.Amount <= 0)
                return Result<ExpenseDto>.Failure("Le montant de la dépense doit être supérieur à 0.");
            if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method))
                return Result<ExpenseDto>.Failure("Mode de paiement invalide.");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<ExpenseDto>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var category = request.Category.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            RecurringExpense? series = null;
            if (request.RepeatMonthly)
            {
                series = new RecurringExpense(
                    Guid.NewGuid(),
                    clinic.Value,
                    category,
                    request.Amount,
                    method,
                    MonthlyExpenseSchedule.DayOfMonthOf(expenseDay),
                    MonthlyExpenseSchedule.MonthOf(expenseDay),
                    description);

                await _recurringExpenseRepository.AddAsync(series, cancellationToken);
            }

            var expense = new Expense(
                Guid.NewGuid(),
                clinic.Value,
                expenseDay,
                category,
                request.Amount,
                method,
                description,
                series?.Id);

            await _expenseRepository.AddAsync(expense, cancellationToken);
            // One save, so a series can never exist without the dépense that started it, or the reverse.
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
            return Result<ExpenseDto>.Failure("Erreur lors de la création de la dépense.");
        }
    }
}
