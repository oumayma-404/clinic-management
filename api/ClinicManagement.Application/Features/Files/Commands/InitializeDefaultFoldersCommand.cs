using ClinicManagement.Application.Common.Interfaces;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

public class InitializeDefaultFoldersCommand : IRequest<Result<IEnumerable<PatientFolderDto>>>
{
    public Guid PatientId { get; set; }
}

public class InitializeDefaultFoldersCommandHandler : IRequestHandler<InitializeDefaultFoldersCommand, Result<IEnumerable<PatientFolderDto>>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IUnitOfWork _unitOfWork;

    private static readonly string[] DefaultFolderNames = new[]
    {
        "Radiographie",
        "Photographie",
        "Analyses Biologiques",
        "Lettres de Liaisons"
    };

    public InitializeDefaultFoldersCommandHandler(
        IPatientRepository patientRepository,
        IPatientFolderRepository folderRepository,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _folderRepository = folderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IEnumerable<PatientFolderDto>>> Handle(InitializeDefaultFoldersCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null)
            {
                return Result<IEnumerable<PatientFolderDto>>.Failure("Patient not found");
            }

            var existingFolders = await _folderRepository.GetRootFoldersByPatientIdAsync(request.PatientId, cancellationToken);
            var existingFolderNames = existingFolders.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var createdFolders = new List<PatientFolder>();

            foreach (var folderName in DefaultFolderNames)
            {
                if (!existingFolderNames.Contains(folderName))
                {
                    // Generate a consistent ID based on folder name for default folders
                    var folderId = GenerateDefaultFolderId(request.PatientId, folderName);
                    var folder = new PatientFolder(folderId, request.PatientId, folderName);
                    await _folderRepository.AddAsync(folder, cancellationToken);
                    createdFolders.Add(folder);
                }
            }

            if (createdFolders.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // Return all root folders (including newly created ones)
            var allFolders = await _folderRepository.GetRootFoldersByPatientIdAsync(request.PatientId, cancellationToken);
            var dtos = allFolders.Select(f => new PatientFolderDto
            {
                Id = f.Id,
                PatientId = f.PatientId,
                ParentFolderId = f.ParentFolderId,
                Name = f.Name,
                FileCount = f.Files.Count,
                SubFolderCount = f.SubFolders.Count,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            });

            return Result<IEnumerable<PatientFolderDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<PatientFolderDto>>.Failure($"Error initializing default folders: {ex.Message}");
        }
    }

    private static Guid GenerateDefaultFolderId(Guid patientId, string folderName)
    {
        // Generate a deterministic GUID based on patient ID and folder name
        // This ensures default folders have consistent IDs
        var input = $"{patientId}-{folderName}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.MD5.HashData(bytes);
        return new Guid(hash);
    }
}









