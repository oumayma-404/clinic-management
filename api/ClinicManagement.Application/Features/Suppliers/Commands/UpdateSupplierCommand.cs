using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Suppliers.Commands;

/// <summary>Edits a fournisseur, and is also how « Désactiver » / « Réactiver » is recorded.</summary>
public class UpdateSupplierCommand : IRequest<Result<SupplierDto>>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Omit to leave the flag alone. Tri-state because « Désactiver » is a one-field action from the row's menu
    /// and must not have to echo the whole record back — and because a missing key defaulting to <c>false</c>
    /// would deactivate a supplier on every ordinary edit.
    /// </summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user was
    /// editing; <c>0</c> means « not supplied » and skips the check (see <c>IUnitOfWork.SetExpectedVersion</c>).
    /// </summary>
    public uint Version { get; set; }
}

public class UpdateSupplierCommandHandler : IRequestHandler<UpdateSupplierCommand, Result<SupplierDto>>
{
    private readonly ISupplierRepository _suppliers;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateSupplierCommandHandler(
        ISupplierRepository suppliers,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _suppliers = suppliers;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SupplierDto>> Handle(
        UpdateSupplierCommand request, CancellationToken cancellationToken)
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

            var supplier = await _suppliers.GetByIdAsync(request.Id, cancellationToken);
            if (supplier is null || supplier.ClinicId != clinic.Value)
            {
                return Result<SupplierDto>.Failure(SupplierRefusals.NotFound);
            }

            // Excluding this row, or renaming a supplier to the case it already has would refuse itself.
            var duplicate = await _suppliers.FindByNameAsync(
                clinic.Value, request.Name, supplier.Id, cancellationToken);
            if (duplicate is not null)
            {
                return Result<SupplierDto>.Failure(
                    SupplierRefusals.Duplicate(duplicate.Name), SupplierRefusals.DuplicateCode);
            }

            supplier.Update(request.Name, request.Category, request.PhoneNumber, request.Address, request.Notes);

            if (request.IsActive is { } isActive)
            {
                supplier.SetActive(isActive);
            }

            _unitOfWork.SetExpectedVersion(supplier, request.Version);

            await _suppliers.UpdateAsync(supplier, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var usage = await _suppliers.GetUsageAsync(supplier.Id, cancellationToken);
            return Result<SupplierDto>.Success(supplier.ToDto(usage));
        }
        catch (ArgumentException ex)
        {
            return Result<SupplierDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<SupplierDto>.Failure("Erreur lors de la mise à jour du fournisseur.");
        }
    }
}
