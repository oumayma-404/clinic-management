using MediatR;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Expenses.Commands;

/// <summary>
/// « Arrêter » — the credit is paid off, so nothing further is owed.
///
/// <para><b>It is not a deletion and it asks for no reason.</b> Every month already posted stays exactly as it
/// is, so no caisse figure the practice has read changes; the series itself is kept, stopped, because a row that
/// vanishes takes the explanation for eighteen dépenses with it. And a motif would be a field to fill in for the
/// most self-evident event in the feature: the thing being paid for has been paid for.</para>
///
/// <para><b>It does not settle up.</b> A month left unposted when the series stops stays unposted — « arrêter »
/// means stop, and a pass that helpfully caught up on the way out would post the very instalment the user just
/// said was no longer due.</para>
/// </summary>
public class StopRecurringExpenseCommand : IRequest<Result<bool>>
{
    public Guid Id { get; set; }
}

public class StopRecurringExpenseCommandHandler : IRequestHandler<StopRecurringExpenseCommand, Result<bool>>
{
    private readonly IRecurringExpenseRepository _recurringExpenseRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public StopRecurringExpenseCommandHandler(
        IRecurringExpenseRepository recurringExpenseRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _recurringExpenseRepository = recurringExpenseRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(StopRecurringExpenseCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<bool>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var series = await _recurringExpenseRepository.GetByIdAsync(request.Id, cancellationToken);
            if (series == null || series.ClinicId != clinic.Value)
            {
                return Result<bool>.Failure(
                    UpdateRecurringExpenseCommand.NotFound,
                    UpdateRecurringExpenseCommand.NotFoundCode);
            }

            // Idempotent: a double tap on « Arrêter », or a second tab, must not be an error — the outcome the
            // caller asked for already holds. `Stop` keeps the first instant, so the journal keeps the real one.
            if (series.IsActive)
            {
                series.Stop(DateTime.UtcNow);
                await _recurringExpenseRepository.UpdateAsync(series, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<bool>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
