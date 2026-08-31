using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Application.Features.LabOrders.Queries;

public class GetLabWorkOrdersQuery : IRequest<Result<PagedResult<LabWorkOrderDto>>>
{
    public Guid? PatientId { get; set; }

    /// <summary>
    /// Optional stage filter (<c>Sent</c> / <c>InProgress</c> / <c>Received</c> / <c>Fitted</c>). The list had no
    /// filter of any kind, which left the dashboard's « Prothèses en retard » card with nowhere truthful to land.
    /// An unrecognised value is ignored rather than refused — a stale bookmark should show the full list, not an
    /// error (matching the graceful-deep-link rule the other pages follow).
    /// </summary>
    public string? Status { get; set; }

    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>Free-text filter, matched in SQL across the whole clinic — never only the requested page.</summary>
    public string? SearchTerm { get; set; }

    /// <summary>Optional fiche-fournisseur filter — « quels bons sont chez ce labo ? ».</summary>
    public Guid? SupplierId { get; set; }

    /// <summary>
    /// <c>expected</c> orders by « Prévu » ascending (dateless last); anything else keeps newest-created first.
    /// An unrecognised value is ignored rather than refused, like <see cref="Status"/>.
    /// </summary>
    public string? SortBy { get; set; }
}

public class GetLabWorkOrdersQueryHandler : IRequestHandler<GetLabWorkOrdersQuery, Result<PagedResult<LabWorkOrderDto>>>
{
    private readonly ILabWorkOrderRepository _labWorkOrderRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetLabWorkOrdersQueryHandler(
        ILabWorkOrderRepository labWorkOrderRepository,
        IPatientRepository patientRepository,
        ISupplierRepository supplierRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _labWorkOrderRepository = labWorkOrderRepository;
        _patientRepository = patientRepository;
        _supplierRepository = supplierRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PagedResult<LabWorkOrderDto>>> Handle(GetLabWorkOrdersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<PagedResult<LabWorkOrderDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            // Unparseable / unknown status => no filter, not a failure (graceful deep-link).
            LabOrderStatus? status = Enum.TryParse<LabOrderStatus>(request.Status, ignoreCase: true, out var parsed)
                ? parsed
                : null;

            // The patient check stays (it is a tenant guard, not a filter), but the filtering itself is now one
            // repository call for both cases. The old shape branched to GetByPatientIdAsync and re-applied the
            // status filter in memory, so the two views used different predicates and only one of them could
            // ever have been paged.
            if (request.PatientId.HasValue)
            {
                var patient = await _patientRepository.GetByIdAsync(request.PatientId.Value, cancellationToken);
                if (patient == null || patient.ClinicId != clinic.Value)
                    return Result<PagedResult<LabWorkOrderDto>>.Failure("Patient introuvable.");
            }

            var page = await _labWorkOrderRepository.GetByClinicIdAsync(
                clinic.Value,
                status,
                request.PatientId,
                request.SearchTerm,
                request.SupplierId,
                string.Equals(request.SortBy, "expected", StringComparison.OrdinalIgnoreCase),
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            // One batched read for the page's laboratories — « relancer le labo » needs a number on every row, and
            // resolving per row is the companion-read defect `list-pagination` documents.
            var supplierIds = page.Items
                .Where(o => o.SupplierId.HasValue)
                .Select(o => o.SupplierId!.Value)
                .ToList();
            var suppliers = await _supplierRepository.GetByIdsAsync(clinic.Value, supplierIds, cancellationToken);

            // Compiled once for the page, not once per row.
            var isOverdue = LabOrderOverdue.Evaluator(LabOrderOverdue.CutoffUtc());

            return Result<PagedResult<LabWorkOrderDto>>.Success(page.Map(o => o.ToDto(
                supplier: o.SupplierId.HasValue && suppliers.TryGetValue(o.SupplierId.Value, out var s) ? s : null,
                isOverdue: isOverdue(o))));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PagedResult<LabWorkOrderDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
