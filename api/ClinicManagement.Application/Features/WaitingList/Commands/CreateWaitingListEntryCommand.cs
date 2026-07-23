using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.WaitingList.Commands;

public class CreateWaitingListEntryCommand : IRequest<Result<WaitingListEntryDto>>
{
    public Guid PatientId { get; set; }
    public Guid? PreferredDoctorId { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string? DesiredTimeframe { get; set; }
    public string? Note { get; set; }
}

public class CreateWaitingListEntryCommandHandler : IRequestHandler<CreateWaitingListEntryCommand, Result<WaitingListEntryDto>>
{
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public CreateWaitingListEntryCommandHandler(
        IWaitingListRepository waitingListRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _waitingListRepository = waitingListRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WaitingListEntryDto>> Handle(CreateWaitingListEntryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.PatientId == Guid.Empty)
                return Result<WaitingListEntryDto>.Failure("Le patient est requis.");

            var priorityInput = string.IsNullOrWhiteSpace(request.Priority) ? "Normal" : request.Priority;
            if (!Enum.TryParse<WaitingListPriority>(priorityInput, ignoreCase: true, out var priority))
                return Result<WaitingListEntryDto>.Failure("Priorité invalide.");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<WaitingListEntryDto>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinic.Value)
                return Result<WaitingListEntryDto>.Failure("Patient introuvable.");

            var entry = new WaitingListEntry(
                Guid.NewGuid(),
                clinic.Value,
                request.PatientId,
                priority,
                request.PreferredDoctorId,
                string.IsNullOrWhiteSpace(request.DesiredTimeframe) ? null : request.DesiredTimeframe.Trim(),
                string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim());

            await _waitingListRepository.AddAsync(entry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<WaitingListEntryDto>.Success(entry.ToDto(patient.GetFullName()));
        }
        catch (ArgumentException ex)
        {
            return Result<WaitingListEntryDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            return Result<WaitingListEntryDto>.Failure($"Erreur lors de l'ajout à la liste d'attente : {ex.Message}");
        }
    }
}
