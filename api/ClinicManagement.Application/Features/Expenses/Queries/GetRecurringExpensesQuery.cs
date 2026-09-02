using MediatR;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Expenses.Queries;

/// <summary>
/// « Dépenses mensuelles » — the clinic's active series.
///
/// <para><b>No window and no paging</b>, unlike every other read on this screen. A series is not period data: it
/// is a standing instruction, so « les dépenses mensuelles du 3 août » is not a question, and a practice has a
/// handful of them — a page-two of standing commitments would hide one behind a pager nobody would look for.</para>
///
/// <para>Stopped series are excluded rather than returned flagged: once the credit is paid off the row exists for
/// the audit journal, not for a list whose job is « what will go out again ».</para>
/// </summary>
public class GetRecurringExpensesQuery : IRequest<Result<IReadOnlyList<RecurringExpenseDto>>>
{
}

public class GetRecurringExpensesQueryHandler
    : IRequestHandler<GetRecurringExpensesQuery, Result<IReadOnlyList<RecurringExpenseDto>>>
{
    private readonly IRecurringExpenseRepository _recurringExpenseRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetRecurringExpensesQueryHandler(
        IRecurringExpenseRepository recurringExpenseRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _recurringExpenseRepository = recurringExpenseRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IReadOnlyList<RecurringExpenseDto>>> Handle(
        GetRecurringExpensesQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<IReadOnlyList<RecurringExpenseDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var series = await _recurringExpenseRepository.GetActiveByClinicIdAsync(clinic.Value, cancellationToken);

            return Result<IReadOnlyList<RecurringExpenseDto>>.Success(
                series.Select(s => s.ToDto()).ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IReadOnlyList<RecurringExpenseDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
