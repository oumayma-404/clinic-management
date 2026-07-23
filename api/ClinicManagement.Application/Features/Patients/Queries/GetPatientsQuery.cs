using System.Globalization;
using System.Text;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetPatientsQuery : IRequest<Result<IEnumerable<PatientDto>>>
{
    /// <summary>Optional free-text filter matched (case- and accent-insensitive) against first name, last
    /// name, the "first last" combination, and the phone number. When null/blank, all patients are returned.</summary>
    public string? SearchTerm { get; set; }

    /// <summary>Optional cap on the number of results returned (applied after filtering). Ignored when null or ≤ 0.</summary>
    public int? Limit { get; set; }
}

public class GetPatientsQueryHandler : IRequestHandler<GetPatientsQuery, Result<IEnumerable<PatientDto>>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetPatientsQueryHandler(
        IPatientRepository patientRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _patientRepository = patientRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<IEnumerable<PatientDto>>> Handle(GetPatientsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<IEnumerable<PatientDto>>.Failure("User ID not found in token");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<IEnumerable<PatientDto>>.Failure("User not found");
            }

            var clinicId = user.ClinicId;

            IEnumerable<Patient> patients = await _patientRepository.GetByClinicIdAsync(clinicId, cancellationToken);

            // Server-side filter: match first/last/full name and phone, case- and accent-insensitive.
            var normalizedTerm = NormalizeForSearch(request.SearchTerm);
            if (!string.IsNullOrEmpty(normalizedTerm))
            {
                patients = patients.Where(p =>
                    NormalizeForSearch(p.FirstName).Contains(normalizedTerm) ||
                    NormalizeForSearch(p.LastName).Contains(normalizedTerm) ||
                    NormalizeForSearch($"{p.FirstName} {p.LastName}").Contains(normalizedTerm) ||
                    NormalizeForSearch(p.PhoneNumber.Value).Contains(normalizedTerm));
            }

            // Stable order so a capped result is deterministic.
            patients = patients.OrderBy(p => p.LastName).ThenBy(p => p.FirstName);

            if (request.Limit is > 0)
            {
                patients = patients.Take(request.Limit.Value);
            }

            var dtos = patients.Select(p => new PatientDto
            {
                Id = p.Id,
                ClinicId = p.ClinicId,
                FirstName = p.FirstName,
                LastName = p.LastName,
                DateOfBirth = p.DateOfBirth,
                Gender = p.Gender,
                Email = p.Email.Value,
                PhoneNumber = p.PhoneNumber.Value,
                MedicalHistory = p.MedicalHistory,
                Allergies = p.Allergies,
                CreatedAt = p.CreatedAt,
                Flags = p.Flags.Select(f => new PatientFlagDto
                {
                    Id = f.Id,
                    FlagType = f.FlagType.ToString(),
                    Description = f.Description,
                    Notes = f.Notes,
                    IsActive = f.IsActive
                }).ToList()
            }).ToList();

            return Result<IEnumerable<PatientDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<PatientDto>>.Failure($"Error retrieving patients: {ex.Message}");
        }
    }

    // Lowercases and strips diacritics so "amine" matches "Amïne" (accent-insensitive) without a Postgres
    // unaccent extension. Runs in memory over the clinic's already-loaded patient list.
    private static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
