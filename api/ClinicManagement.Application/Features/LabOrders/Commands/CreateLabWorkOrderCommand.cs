using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Suppliers;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.LabOrders.Commands;

public class CreateLabWorkOrderCommand : IRequest<Result<LabWorkOrderDto>>
{
    public Guid PatientId { get; set; }
    public int? ToothNumber { get; set; }
    public string Prosthetist { get; set; } = string.Empty;
    public string WorkDescription { get; set; } = string.Empty;
    public DateTime? SentDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public decimal? Cost { get; set; }
    public string? Notes { get; set; }

    /// <summary>Optional — the séance this prothèse belongs to (AC-23). Validated clinic- and patient-side.</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>
    /// Optional — the laboratory as a fournisseur, so the bon carries a number somebody can call. Independent of
    /// <see cref="Prosthetist"/>, which stays required: a lab used once must be recordable without filing one.
    /// </summary>
    public Guid? SupplierId { get; set; }
}

public class CreateLabWorkOrderCommandHandler : IRequestHandler<CreateLabWorkOrderCommand, Result<LabWorkOrderDto>>
{
    private readonly ILabWorkOrderRepository _labWorkOrderRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public CreateLabWorkOrderCommandHandler(
        ILabWorkOrderRepository labWorkOrderRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        ISupplierRepository supplierRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _labWorkOrderRepository = labWorkOrderRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _supplierRepository = supplierRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LabWorkOrderDto>> Handle(CreateLabWorkOrderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.PatientId == Guid.Empty)
                return Result<LabWorkOrderDto>.Failure("Le patient est requis.");
            if (string.IsNullOrWhiteSpace(request.Prosthetist))
                return Result<LabWorkOrderDto>.Failure("Le prothésiste est requis.");
            if (string.IsNullOrWhiteSpace(request.WorkDescription))
                return Result<LabWorkOrderDto>.Failure("La description du travail est requise.");
            if (request.Cost.HasValue && request.Cost.Value < 0)
                return Result<LabWorkOrderDto>.Failure("Le coût ne peut pas être négatif.");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<LabWorkOrderDto>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinic.Value)
                return Result<LabWorkOrderDto>.Failure("Patient introuvable.");

            var link = await LabOrderAppointmentLink.ValidateAsync(
                _appointmentRepository, request.AppointmentId, clinic.Value, request.PatientId, cancellationToken);
            if (link.IsFailure)
                return Result<LabWorkOrderDto>.Failure(link.Error!);

            var supplier = await SupplierLink.ResolveAsync(
                _supplierRepository, clinic.Value, request.SupplierId, cancellationToken);
            if (supplier.IsFailure)
                return Result<LabWorkOrderDto>.FailureFrom(supplier);

            var order = new LabWorkOrder(
                Guid.NewGuid(),
                clinic.Value,
                request.PatientId,
                request.Prosthetist.Trim(),
                request.WorkDescription.Trim(),
                request.ToothNumber,
                request.SentDate,
                request.ExpectedDate,
                request.Cost,
                string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                request.AppointmentId,
                supplier.Value?.Id);

            await _labWorkOrderRepository.AddAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<LabWorkOrderDto>.Success(order.ToDto(patient.GetFullName(), supplier.Value));
        }
        catch (ArgumentException ex)
        {
            return Result<LabWorkOrderDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<LabWorkOrderDto>.Failure($"Erreur lors de la création du bon de laboratoire : {ex.Message}");
        }
    }
}
