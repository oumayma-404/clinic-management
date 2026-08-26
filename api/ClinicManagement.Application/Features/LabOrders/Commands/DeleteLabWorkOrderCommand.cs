using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.LabOrders.Commands;

public class DeleteLabWorkOrderCommand : IRequest<Result<bool>>
{
    public Guid Id { get; set; }
}

public class DeleteLabWorkOrderCommandHandler : IRequestHandler<DeleteLabWorkOrderCommand, Result<bool>>
{
    private readonly ILabWorkOrderRepository _labWorkOrderRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteLabWorkOrderCommandHandler(
        ILabWorkOrderRepository labWorkOrderRepository,
        IExpenseRepository expenseRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _labWorkOrderRepository = labWorkOrderRepository;
        _expenseRepository = expenseRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(DeleteLabWorkOrderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<bool>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var order = await _labWorkOrderRepository.GetByIdAsync(request.Id, cancellationToken);
            if (order == null || order.ClinicId != clinic.Value)
                return Result<bool>.Failure("Bon de laboratoire introuvable.");

            /*
             * ⚠️ The bon's caisse dépense goes with it. `LabOrderCaisseExpense` posts one when the piece arrives,
             * and nothing used to take it away: deleting a received bon left a dépense in la caisse belonging to
             * a bon that no longer exists — 3 orphans totalling 661,750 DT in the QA pass — permanently reducing
             * a reported Net with nothing on any screen to explain it.
             *
             * Deleted, not reassigned: it was posted BY this bon and describes it in its own libellé, so there is
             * nothing to reassign it to. And the caisse's own « Nouvelle dépense » is how a practice records a
             * laboratory cost it still wants to keep.
             *
             * The clinic check is not redundant — `ExpenseId` is a plain Guid on the bon, not a navigation, so a
             * cross-tenant id could only be a data fault, and a delete must not act on one.
             */
            if (order.ExpenseId is { } expenseId)
            {
                var expense = await _expenseRepository.GetByIdAsync(expenseId, cancellationToken);
                if (expense != null && expense.ClinicId == clinic.Value)
                {
                    await _expenseRepository.DeleteAsync(expenseId, cancellationToken);
                }
            }

            await _labWorkOrderRepository.DeleteAsync(request.Id, cancellationToken);
            // One save for both, so a bon can never be gone with its dépense left behind.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // No `ex.Message`: an EF/Npgsql sentence is English machine text and this string is rendered verbatim.
            return Result<bool>.Failure("Erreur lors de la suppression du bon de laboratoire.");
        }
    }
}
