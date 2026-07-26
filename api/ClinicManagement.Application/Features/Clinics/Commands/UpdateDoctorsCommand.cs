using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Commands;

public class UpdateDoctorsCommand : IRequest<Result<List<DoctorDto>>>
{
    public List<DoctorDto> Doctors { get; set; } = new();
}

public class UpdateDoctorsCommandHandler : IRequestHandler<UpdateDoctorsCommand, Result<List<DoctorDto>>>
{
    private readonly IDoctorRepository _doctorRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateDoctorsCommandHandler(
        IDoctorRepository doctorRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork)
    {
        _doctorRepository = doctorRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<List<DoctorDto>>> Handle(UpdateDoctorsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<List<DoctorDto>>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<List<DoctorDto>>.Failure("Utilisateur introuvable.");
            }

            var clinicId = user.ClinicId;

            // Get existing doctors for the clinic
            var existingDoctors = (await _doctorRepository.GetByClinicIdAsync(clinicId, cancellationToken)).ToList();

            // Filter out empty doctors
            var validDoctors = request.Doctors
                .Where(d => 
                    (!string.IsNullOrWhiteSpace(d.FirstName) && !string.IsNullOrWhiteSpace(d.LastName) || !string.IsNullOrWhiteSpace(d.Name)) 
                    && !string.IsNullOrWhiteSpace(d.Specialty))
                .ToList();

            // Process doctors: create, update, or delete
            var doctorIdsToKeep = new HashSet<Guid>();

            foreach (var doctorDto in validDoctors)
            {
                if (doctorDto.Id.HasValue)
                {
                    // Update existing doctor
                    var existingDoctor = existingDoctors.FirstOrDefault(d => d.Id == doctorDto.Id.Value);
                    if (existingDoctor != null)
                    {
                        // Use FirstName/LastName if provided, otherwise parse from Name
                        var firstName = doctorDto.FirstName ?? (doctorDto.Name?.Split(' ', 2)[0] ?? "");
                        var lastName = doctorDto.LastName ?? (doctorDto.Name?.Split(' ', 2).Length > 1 ? doctorDto.Name.Split(' ', 2)[1] : "");
                        
                        existingDoctor.Update(firstName, lastName, doctorDto.Specialty, doctorDto.Phone, doctorDto.Email, doctorDto.CodeProfessionnelSante);
                        _doctorRepository.Update(existingDoctor);
                        doctorIdsToKeep.Add(existingDoctor.Id);
                    }
                    // If doctor with this ID doesn't exist, treat as new doctor
                    else
                    {
                        // Use FirstName/LastName if provided, otherwise parse from Name
                        var firstName = doctorDto.FirstName ?? (doctorDto.Name?.Split(' ', 2)[0] ?? "");
                        var lastName = doctorDto.LastName ?? (doctorDto.Name?.Split(' ', 2).Length > 1 ? doctorDto.Name.Split(' ', 2)[1] : "");
                        
                        var newDoctor = new Doctor(
                            Guid.NewGuid(),
                            clinicId,
                            firstName,
                            lastName,
                            doctorDto.Specialty,
                            doctorDto.Phone,
                            doctorDto.Email);
                        await _doctorRepository.AddAsync(newDoctor, cancellationToken);
                        doctorIdsToKeep.Add(newDoctor.Id);
                    }
                }
                else
                {
                    // Create new doctor
                    // Use FirstName/LastName if provided, otherwise parse from Name
                    var firstName = doctorDto.FirstName ?? (doctorDto.Name?.Split(' ', 2)[0] ?? "");
                    var lastName = doctorDto.LastName ?? (doctorDto.Name?.Split(' ', 2).Length > 1 ? doctorDto.Name.Split(' ', 2)[1] : "");
                    
                    var newDoctor = new Doctor(
                        Guid.NewGuid(),
                        clinicId,
                        firstName,
                        lastName,
                        doctorDto.Specialty,
                        doctorDto.Phone,
                        doctorDto.Email,
                        doctorDto.CodeProfessionnelSante);
                    await _doctorRepository.AddAsync(newDoctor, cancellationToken);
                    doctorIdsToKeep.Add(newDoctor.Id);
                }
            }

            // Delete doctors that are no longer in the list
            var doctorsToDelete = existingDoctors.Where(d => !doctorIdsToKeep.Contains(d.Id)).ToList();
            foreach (var doctorToDelete in doctorsToDelete)
            {
                _doctorRepository.Remove(doctorToDelete);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Get updated list of doctors
            var updatedDoctors = await _doctorRepository.GetByClinicIdAsync(clinicId, cancellationToken);
            var doctorDtos = updatedDoctors.Select(d => new DoctorDto
            {
                Id = d.Id,
                Name = d.FullName, // Map FullName to Name for backward compatibility
                FirstName = d.FirstName,
                LastName = d.LastName,
                Specialty = d.Specialty,
                Phone = d.Phone,
                Email = d.Email,
                CodeProfessionnelSante = d.CodeProfessionnelSante
            }).ToList();

            return Result<List<DoctorDto>>.Success(doctorDtos);
        }
        catch (Exception ex)
        {
            return Result<List<DoctorDto>>.Failure($"Error updating doctors: {ex.Message}");
        }
    }
}

