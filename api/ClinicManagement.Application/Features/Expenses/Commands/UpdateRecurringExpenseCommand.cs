using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Expenses.Commands;

/// <summary>
/// « Modifier » on a monthly dépense — the loyer has gone from 800 to 850.
///
/// <para>It changes what FUTURE months will post and nothing else. Every occurrence already in la caisse keeps
/// the figure it was recorded with, because those rows are that month's money: rewriting them would move the Net
/// of a period a practice has already read, reconciled and possibly declared. The month on screen is corrected —
/// when it needs to be — as the ordinary dépense it is, in the table right below.</para>
/// </summary>
public class UpdateRecurringExpenseCommand : IRequest<Result<RecurringExpenseDto>>
{
    /// <summary>The code the controller branches on to choose 404 over 400 — never the French sentence.</summary>
    public const string NotFoundCode = "recurring_expense_not_found";

    public const string NotFound = "Dépense mensuelle introuvable.";

    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DayOfMonth { get; set; }

    /// <inheritdoc cref="UpdateExpenseCommand.Version"/>
    public uint Version { get; set; }
}

public class UpdateRecurringExpenseCommandHandler
    : IRequestHandler<UpdateRecurringExpenseCommand, Result<RecurringExpenseDto>>
{
    private readonly IRecurringExpenseRepository _recurringExpenseRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateRecurringExpenseCommandHandler(
        IRecurringExpenseRepository recurringExpenseRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _recurringExpenseRepository = recurringExpenseRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RecurringExpenseDto>> Handle(
        UpdateRecurringExpenseCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Category))
                return Result<RecurringExpenseDto>.Failure("La catégorie est requise.");
            // The same three column limits a one-off dépense is held to — a series writes into the same table.
            if (ExpenseDay.RefuseFields(request.Category, request.Description, request.Amount) is { } badField)
                return Result<RecurringExpenseDto>.Failure(badField);
            if (request.Amount <= 0)
                return Result<RecurringExpenseDto>.Failure("Le montant de la dépense doit être supérieur à 0.");
            if (MonthlyExpenseSchedule.RefuseDayOfMonth(request.DayOfMonth) is { } badDay)
                return Result<RecurringExpenseDto>.Failure(badDay);
            if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method))
                return Result<RecurringExpenseDto>.Failure("Mode de paiement invalide.");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<RecurringExpenseDto>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var series = await _recurringExpenseRepository.GetByIdAsync(request.Id, cancellationToken);
            // A stopped series is « introuvable » to the edit form too: it is off the list, and letting it be
            // re-priced would be editing a commitment the practice has ended.
            if (series == null || series.ClinicId != clinic.Value || !series.IsActive)
            {
                return Result<RecurringExpenseDto>.Failure(
                    UpdateRecurringExpenseCommand.NotFound,
                    UpdateRecurringExpenseCommand.NotFoundCode);
            }

            series.Update(
                request.Category.Trim(),
                request.Amount,
                method,
                request.DayOfMonth,
                string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());

            _unitOfWork.SetExpectedVersion(series, request.Version);

            await _recurringExpenseRepository.UpdateAsync(series, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<RecurringExpenseDto>.Success(series.ToDto());
        }
        catch (ArgumentException ex)
        {
            return Result<RecurringExpenseDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<RecurringExpenseDto>.Failure("Erreur lors de la mise à jour de la dépense mensuelle.", ex);
        }
    }
}
