using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Suppliers.Commands;

/// <summary>Files a new fournisseur. Only the nom is required (AC-1).</summary>
public class CreateSupplierCommand : IRequest<Result<SupplierDto>>
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}

public class CreateSupplierCommandHandler : IRequestHandler<CreateSupplierCommand, Result<SupplierDto>>
{
    private readonly ISupplierRepository _suppliers;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public CreateSupplierCommandHandler(
        ISupplierRepository suppliers,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _suppliers = suppliers;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SupplierDto>> Handle(
        CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Result<SupplierDto>.Failure("Le nom du fournisseur est requis.");
            }

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<SupplierDto>.Failure(clinic.Error ?? "Cabinet introuvable.");
            }

            var existing = await _suppliers.FindByNameAsync(
                clinic.Value, request.Name, excludingId: null, cancellationToken);
            if (existing is not null)
            {
                return Result<SupplierDto>.Failure(
                    SupplierRefusals.Duplicate(existing.Name), SupplierRefusals.DuplicateCode);
            }

            var supplier = new Supplier(
                Guid.NewGuid(),
                clinic.Value,
                request.Name,
                request.Category,
                request.PhoneNumber,
                request.Address,
                request.Notes);

            await _suppliers.AddAsync(supplier, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // A supplier that has just been created is referenced by nothing, so the default usage is the truth
            // and a batched read here would be a round trip to learn zero.
            return Result<SupplierDto>.Success(supplier.ToDto());
        }
        catch (ArgumentException ex)
        {
            // The aggregate's own French guards (nom vide, nom trop long) — surfaced verbatim rather than
            // restated here, so the entity stays the single authority on what a valid fournisseur is.
            return Result<SupplierDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<SupplierDto>.Failure("Erreur lors de la création du fournisseur.");
        }
    }
}
