using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Suppliers;

/// <summary>
/// The single answer to « may this record point at this fournisseur? », shared by the two write paths that link
/// one — <c>StockItem.SupplierId</c> and <c>LabWorkOrder.SupplierId</c>.
/// <para>
/// ⚠️ <b>It is shared rather than written twice because it is a tenancy check.</b> Four call sites (create and
/// update, on each of two aggregates) each doing their own lookup is the shape where one of them forgets the
/// <c>ClinicId</c> comparison — and a crafted request would then point one practice's stock at another practice's
/// supplier, which is a cross-tenant read of a name and a phone number.
/// </para>
/// <para>
/// ⚠️ <b>A deactivated supplier is accepted</b>, deliberately. Deactivation hides a contact from the pickers; it
/// does not make an existing link unsaveable, so re-saving an article whose dépôt has since closed must not
/// refuse (EC-4). What the pickers do not offer, they simply do not offer.
/// </para>
/// </summary>
public static class SupplierLink
{
    /// <summary>
    /// Resolves <paramref name="supplierId"/> against <paramref name="clinicId"/>. A null id is a successful
    /// « no supplier » — the common case (AC-5) — and an unknown or foreign one is a French refusal, never a
    /// silently-dropped link.
    /// </summary>
    public static async Task<Result<Supplier?>> ResolveAsync(
        ISupplierRepository suppliers,
        Guid clinicId,
        Guid? supplierId,
        CancellationToken cancellationToken)
    {
        if (supplierId is not { } id || id == Guid.Empty)
        {
            return Result<Supplier?>.Success(null);
        }

        var supplier = await suppliers.GetByIdAsync(id, cancellationToken);
        if (supplier is null || supplier.ClinicId != clinicId)
        {
            return Result<Supplier?>.Failure(SupplierRefusals.NotFound);
        }

        return Result<Supplier?>.Success(supplier);
    }
}
