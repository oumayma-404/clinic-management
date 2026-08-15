using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Suppliers.Commands;

/// <summary>
/// Deletes a fournisseur nothing points at.
/// <para>
/// ⚠️ <b>A referenced one is refused, never cascaded and never silently unlinked</b> (AC-4). The FK is
/// <c>ON DELETE RESTRICT</c>, but reaching the constraint would surface as a 500 with no French sentence, so the
/// guard lives here and names the counts; the constraint is the backstop against a race.
/// </para>
/// </summary>
public class DeleteSupplierCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class DeleteSupplierCommandHandler : IRequestHandler<DeleteSupplierCommand, Result>
{
    private readonly ISupplierRepository _suppliers;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteSupplierCommandHandler(
        ISupplierRepository suppliers,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _suppliers = suppliers;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteSupplierCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result.Failure(clinic.Error ?? "Cabinet introuvable.");
            }

            var supplier = await _suppliers.GetByIdAsync(request.Id, cancellationToken);
            if (supplier is null || supplier.ClinicId != clinic.Value)
            {
                return Result.Failure(SupplierRefusals.NotFound);
            }

            var usage = await _suppliers.GetUsageAsync(supplier.Id, cancellationToken);
            if (usage.IsReferenced)
            {
                return Result.Failure(
                    SupplierRefusals.InUse(usage.StockItems, usage.LabOrders), SupplierRefusals.InUseCode);
            }

            await _suppliers.DeleteAsync(supplier, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result.Failure("Erreur lors de la suppression du fournisseur.");
        }
    }
}
