using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.LabOrders.Queries;

public class GetLabWorkOrdersQuery : IRequest<Result<IEnumerable<LabWorkOrderDto>>>
{
    public Guid? PatientId { get; set; }
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

            if (request.PatientId.HasValue)
            {
                var patient = await _patientRepository.GetByIdAsync(request.PatientId.Value, cancellationToken);
                if (patient == null || patient.ClinicId != clinic.Value)
                    return Result<IEnumerable<LabWorkOrderDto>>.Failure("Patient introuvable.");

                var patientOrders = await _labWorkOrderRepository.GetByPatientIdAsync(request.PatientId.Value, cancellationToken);
                return Result<IEnumerable<LabWorkOrderDto>>.Success(patientOrders.Select(o => o.ToDto()));
            }

            var orders = await _labWorkOrderRepository.GetByClinicIdAsync(clinic.Value, cancellationToken);
            return Result<IEnumerable<LabWorkOrderDto>>.Success(orders.Select(o => o.ToDto()));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<LabWorkOrderDto>>.Failure($"Erreur lors de la récupération des bons de laboratoire : {ex.Message}");
        }
    }
}
