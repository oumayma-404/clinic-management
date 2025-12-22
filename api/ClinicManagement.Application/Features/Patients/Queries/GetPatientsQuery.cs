using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetPatientsQuery : IRequest<Result<IEnumerable<PatientDto>>>
{
    public string? SearchTerm { get; set; }
    public int? Limit { get; set; }
}

public class GetPatientsQueryHandler : IRequestHandler<GetPatientsQuery, Result<IEnumerable<PatientDto>>>
{
    private readonly IPatientRepository _patientRepository;

    public GetPatientsQueryHandler(IPatientRepository patientRepository)
    {
        _patientRepository = patientRepository;
    }

    public async Task<Result<IEnumerable<PatientDto>>> Handle(GetPatientsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var patients = await _patientRepository.GetAllAsync(cancellationToken);

            // Apply search filter if provided
            if (!string.IsNullOrWhiteSpace(request.SearchTerm))
            {
                var searchTerm = request.SearchTerm.ToLowerInvariant();
                patients = patients.Where(p =>
                    p.FirstName.ToLowerInvariant().Contains(searchTerm) ||
                    p.LastName.ToLowerInvariant().Contains(searchTerm) ||
                    p.Email.Value.ToLowerInvariant().Contains(searchTerm) ||
                    p.PhoneNumber.Value.Contains(searchTerm));
            }

            // Apply limit if provided
            if (request.Limit.HasValue && request.Limit.Value > 0)
            {
                patients = patients.Take(request.Limit.Value);
            }

            var dtos = patients.Select(p => new PatientDto
            {
                Id = p.Id,
                FirstName = p.FirstName,
                LastName = p.LastName,
                DateOfBirth = p.DateOfBirth,
                Gender = p.Gender,
                Email = p.Email.Value,
                PhoneNumber = p.PhoneNumber.Value,
                MedicalHistory = p.MedicalHistory,
                Allergies = p.Allergies,
                EmergencyContactName = p.EmergencyContactName,
                EmergencyContactPhone = p.EmergencyContactPhone?.Value,
                Flags = p.Flags.Select(f => new PatientFlagDto
                {
                    Id = f.Id,
                    FlagType = f.FlagType.ToString(),
                    Description = f.Description,
                    Notes = f.Notes,
                    IsActive = f.IsActive
                }).ToList(),
                CreatedAt = p.CreatedAt
            }).OrderBy(p => p.LastName).ThenBy(p => p.FirstName);

            return Result<IEnumerable<PatientDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<PatientDto>>.Failure($"Error retrieving patients: {ex.Message}");
        }
    }
}

