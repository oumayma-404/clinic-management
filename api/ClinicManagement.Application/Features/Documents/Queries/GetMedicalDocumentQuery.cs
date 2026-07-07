using MediatR;
using ClinicManagement.Application.Common.Models;
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

    public GetMedicalDocumentQueryHandler(IMedicalDocumentRepository documentRepository)
    {
        _documentRepository = documentRepository;
    }

    public async Task<Result<MedicalDocumentDto>> Handle(GetMedicalDocumentQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentRepository.GetByIdAsync(request.Id, cancellationToken);
            if (document == null)
            {
                return Result<MedicalDocumentDto>.Failure("Medical document not found");
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

