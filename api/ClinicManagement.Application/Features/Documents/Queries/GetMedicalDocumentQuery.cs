using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Documents.Queries;

public class GetMedicalDocumentQuery : IRequest<Result<MedicalDocumentDto>>
{
    public Guid Id { get; set; }
}

public class GetMedicalDocumentQueryHandler : IRequestHandler<GetMedicalDocumentQuery, Result<MedicalDocumentDto>>
{
    private readonly IMedicalDocumentRepository _documentRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;

    public GetMedicalDocumentQueryHandler(
        IMedicalDocumentRepository documentRepository,
        IClinicContext clinicContext,
        IUserRepository userRepository)
    {
        _documentRepository = documentRepository;
        _clinicContext = clinicContext;
        _userRepository = userRepository;
    }

    public async Task<Result<MedicalDocumentDto>> Handle(GetMedicalDocumentQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentRepository.GetByIdAsync(request.Id, cancellationToken);
            if (document == null)
            {
                return Result<MedicalDocumentDto>.Failure("Document médical introuvable.");
            }

            // Verify the document's owning patient belongs to the caller's clinic (MedicalDocument has no
            // ClinicId of its own). Skipped when there is no clinic in scope — e.g. PdfGenerationJob runs
            // in a background scope with no authenticated user (DEV-1, mirrors the global filter's AC-3 rule).
            var userId = _clinicContext.GetUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
                if (user == null || document.Patient == null || document.Patient.ClinicId != user.ClinicId)
                {
                    return Result<MedicalDocumentDto>.Failure("Document médical introuvable.");
                }
            }

            var dto = new MedicalDocumentDto
            {
                Id = document.Id,
                PatientId = document.PatientId,
                PatientName = document.PatientName,
                PatientAge = document.PatientAge,
                DocumentType = document.DocumentType,
                DocumentDate = document.DocumentDate,
                RecipientDoctorName = document.RecipientDoctorName,
                RecipientDoctorSpecialty = document.RecipientDoctorSpecialty,
                ContentJson = document.ContentJson,
                ClinicName = document.ClinicName,
                ClinicAddress = document.ClinicAddress,
                ClinicPhone = document.ClinicPhone,
                DoctorName = document.DoctorName,
                DoctorSpecialty = document.DoctorSpecialty,
                IsDraft = document.IsDraft,
                FileId = document.FileId,
                AppointmentId = document.AppointmentId,
                CreatedAt = document.CreatedAt,
                UpdatedAt = document.UpdatedAt
            };

            return Result<MedicalDocumentDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<MedicalDocumentDto>.Failure($"Error retrieving medical document: {ex.Message}");
        }
    }
}

