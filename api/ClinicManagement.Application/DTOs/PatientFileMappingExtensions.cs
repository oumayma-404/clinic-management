using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The one <see cref="PatientFile"/> → <see cref="PatientFileDto"/> mapping.
///
/// <para>⚠️ Three copies of this initializer existed — upload, update and the paged read — so adding `Version`
/// meant three edits, and a missed one returns 0, which the concurrency check reads as « not supplied » and
/// silently skips. Same reason as <see cref="MedicalDocumentMappingExtensions"/>.</para>
/// </summary>
public static class PatientFileMappingExtensions
{
    public static PatientFileDto ToDto(this PatientFile file) => new()
    {
        Id = file.Id,
        PatientId = file.PatientId,
        FolderId = file.FolderId,
        FileName = file.FileName,
        ContentType = file.ContentType,
        FileSize = file.FileSize,
        FileType = file.FileType.ToString(),
        Description = file.Description,
        UploadedAt = file.UploadedAt,
        UploadedBy = file.UploadedBy,
        Version = file.Version,
    };
}
