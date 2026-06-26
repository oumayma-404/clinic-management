using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Documents.Queries;

public class GetMedicalDocumentsQuery : IRequest<Result<IEnumerable<MedicalDocumentDto>>>
{
    public Guid? PatientId { get; set; }
    public string? DocumentType { get; set; }
}

public class GetMedicalDocumentsQueryHandler : IRequestHandler<GetMedicalDocumentsQuery, Result<IEnumerable<MedicalDocumentDto>>>
{
    private readonly IMedicalDocumentRepository _documentRepository;

    public GetMedicalDocumentsQueryHandler(IMedicalDocumentRepository documentRepository)
    {
        _documentRepository = documentRepository;
    }

    public async Task<Result<IEnumerable<MedicalDocumentDto>>> Handle(GetMedicalDocumentsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            IEnumerable<Domain.Entities.MedicalDocument> documents;

            if (request.PatientId.HasValue)
            {
                documents = await _documentRepository.GetByPatientIdAsync(request.PatientId.Value, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(request.DocumentType))
            {
                documents = await _documentRepository.GetByDocumentTypeAsync(request.DocumentType, cancellationToken);
            }
            else
            {
                documents = await _documentRepository.GetAllAsync(cancellationToken);
            }

            var dtos = documents.Select(d => new MedicalDocumentDto
            {
                Id = d.Id,
                PatientId = d.PatientId,
                PatientName = d.PatientName,
                PatientAge = d.PatientAge,
                DocumentType = d.DocumentType,
                DocumentDate = d.DocumentDate,
                RecipientDoctorName = d.RecipientDoctorName,
                RecipientDoctorSpecialty = d.RecipientDoctorSpecialty,
                ContentJson = d.ContentJson,
                ClinicName = d.ClinicName,
                ClinicAddress = d.ClinicAddress,
                ClinicPhone = d.ClinicPhone,
                DoctorName = d.DoctorName,
                DoctorSpecialty = d.DoctorSpecialty,
                IsDraft = d.IsDraft,
                FileId = d.FileId,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt
            });

            return Result<IEnumerable<MedicalDocumentDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<MedicalDocumentDto>>.Failure($"Error retrieving medical documents: {ex.Message}");
        }
    }
}

