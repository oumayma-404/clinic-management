using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
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
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetMedicalDocumentsQueryHandler(
        IMedicalDocumentRepository documentRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _documentRepository = documentRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<MedicalDocumentDto>>> Handle(GetMedicalDocumentsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<IEnumerable<MedicalDocumentDto>>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }
            var clinicId = clinicResult.Value;

            IEnumerable<Domain.Entities.MedicalDocument> documents;

            // Scope every branch to the caller's clinic via the owning patient (MedicalDocument has no
            // ClinicId). The no-arg branch is scoped in SQL (GetByClinicIdAsync) so other tenants' rows are
            // never materialized; the narrow by-patient / by-type branches verify the owning clinic in memory.
            if (request.PatientId.HasValue)
            {
                documents = await _documentRepository.GetByPatientIdAsync(request.PatientId.Value, cancellationToken);
                documents = documents.Where(d => d.Patient != null && d.Patient.ClinicId == clinicId);
            }
            else if (!string.IsNullOrWhiteSpace(request.DocumentType))
            {
                documents = await _documentRepository.GetByDocumentTypeAsync(request.DocumentType, cancellationToken);
                documents = documents.Where(d => d.Patient != null && d.Patient.ClinicId == clinicId);
            }
            else
            {
                documents = await _documentRepository.GetByClinicIdAsync(clinicId, cancellationToken);
            }

            var dtos = documents.Select(d => d.ToDto());

            return Result<IEnumerable<MedicalDocumentDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<MedicalDocumentDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

