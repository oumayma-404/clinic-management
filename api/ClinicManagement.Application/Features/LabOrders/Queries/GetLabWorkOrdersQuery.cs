using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.LabOrders.Queries;

public class GetLabWorkOrdersQuery : IRequest<Result<IEnumerable<LabWorkOrderDto>>>
{
    public Guid? PatientId { get; set; }

    /// <summary>
    /// Optional stage filter (<c>Sent</c> / <c>InProgress</c> / <c>Received</c> / <c>Fitted</c>). The list had no
    /// filter of any kind, which left the dashboard's « Prothèses en retard » card with nowhere truthful to land.
    /// An unrecognised value is ignored rather than refused — a stale bookmark should show the full list, not an
    /// error (matching the graceful-deep-link rule the other pages follow).
    /// </summary>
    public string? Status { get; set; }
}

public class GetLabWorkOrdersQueryHandler : IRequestHandler<GetLabWorkOrdersQuery, Result<IEnumerable<LabWorkOrderDto>>>
{
    private readonly ILabWorkOrderRepository _labWorkOrderRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetLabWorkOrdersQueryHandler(
        ILabWorkOrderRepository labWorkOrderRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _labWorkOrderRepository = labWorkOrderRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<LabWorkOrderDto>>> Handle(GetLabWorkOrdersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<IEnumerable<LabWorkOrderDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            // Unparseable / unknown status => no filter, not a failure (graceful deep-link).
            LabOrderStatus? status = Enum.TryParse<LabOrderStatus>(request.Status, ignoreCase: true, out var parsed)
                ? parsed
                : null;

            if (request.PatientId.HasValue)
            {
                var patient = await _patientRepository.GetByIdAsync(request.PatientId.Value, cancellationToken);
                if (patient == null || patient.ClinicId != clinic.Value)
                    return Result<IEnumerable<LabWorkOrderDto>>.Failure("Patient introuvable.");

                var patientOrders = await _labWorkOrderRepository.GetByPatientIdAsync(request.PatientId.Value, cancellationToken);
                if (status.HasValue)
                {
                    patientOrders = patientOrders.Where(o => o.Status == status.Value);
                }
                return Result<IEnumerable<LabWorkOrderDto>>.Success(patientOrders.Select(o => o.ToDto()));
            }

            var orders = await _labWorkOrderRepository.GetByClinicIdAsync(clinic.Value, status, cancellationToken);
            return Result<IEnumerable<LabWorkOrderDto>>.Success(orders.Select(o => o.ToDto()));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<LabWorkOrderDto>>.Failure($"Erreur lors de la récupération des bons de laboratoire : {ex.Message}");
        }
    }
}
