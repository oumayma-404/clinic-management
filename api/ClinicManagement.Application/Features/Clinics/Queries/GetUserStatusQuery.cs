using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Queries;

public class GetUserStatusQuery : IRequest<Result<UserStatusDto>>
{
}

public class GetUserStatusQueryHandler : IRequestHandler<GetUserStatusQuery, Result<UserStatusDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;

    public GetUserStatusQueryHandler(
        IUserRepository userRepository,
        IClinicRepository clinicRepository,
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext)
    {
        _userRepository = userRepository;
        _clinicRepository = clinicRepository;
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<UserStatusDto>> Handle(GetUserStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<UserStatusDto>.Failure("User ID not found in token");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            
            if (user == null)
            {
                // User doesn't exist in database yet
                return Result<UserStatusDto>.Success(new UserStatusDto
                {
                    HasClinic = false,
                    User = null
                });
            }

            // User exists, get clinic info
            var clinic = await _clinicRepository.GetByIdAsync(user.ClinicId, cancellationToken);
            
            // Get doctors for the clinic
            var doctors = await _doctorRepository.GetByClinicIdAsync(user.ClinicId, cancellationToken);
            var doctorDtos = doctors.Select(d => new DoctorDto
            {
                Id = d.Id,
                UserId = d.UserId, // authoritative link so the client can resolve the current user's doctor by id
                Name = d.FullName, // Map FullName to Name for backward compatibility
                FirstName = d.FirstName,
                LastName = d.LastName,
                Specialty = d.Specialty,
                Phone = d.Phone,
                Email = d.Email,
                CodeProfessionnelSante = d.CodeProfessionnelSante,
                OrdreNumberCnomdt = d.OrdreNumberCnomdt,
                HasCachet = d.CachetStorageKey != null
            }).ToList();
            
            var dto = new UserStatusDto
            {
                HasClinic = true,
                ClinicId = user.ClinicId,
                ClinicName = clinic?.Name,
                Role = user.Role,
                User = new UserDto
                {
                    Id = user.Id,
                    ClinicId = user.ClinicId,
                    Role = user.Role,
                    Email = user.Email,
                    FullName = user.FullName,
                    CreatedAt = user.CreatedAt
                },
                Clinic = clinic != null ? new ClinicDto
                {
                    Id = clinic.Id,
                    Name = clinic.Name,
                    Address = clinic.Address,
                    City = clinic.City,
                    Phone = clinic.Phone,
                    Email = clinic.Email,
                    Code = clinic.Code,
                    LogoUrl = clinic.LogoUrl,
                    MatriculeFiscal = clinic.MatriculeFiscal,
                    VatApplicable = clinic.VatApplicable,
                    VatRate = clinic.VatRate,
                    StampDutyEnabled = clinic.StampDutyEnabled,
                    StampDutyAmount = clinic.StampDutyAmount,
                    TtnEInvoicingEnabled = clinic.TtnEInvoicingEnabled,
                    TtnEnvironment = clinic.TtnEnvironment,
                    CreatedAt = clinic.CreatedAt
                } : null,
                Doctors = doctorDtos
            };

            return Result<UserStatusDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<UserStatusDto>.Failure($"Error getting user status: {ex.Message}");
        }
    }
}

