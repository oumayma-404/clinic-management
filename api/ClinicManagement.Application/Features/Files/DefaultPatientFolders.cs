using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Files;

/// <summary>
/// The four folders every patient's « Fichiers » starts with, and the one rule for their ids.
///
/// <para>⚠️ <b>Moved out of <c>InitializeDefaultFoldersCommand</c>, not copied</b>, when the camera import needed
/// « Photographie » on its own (<c>features/camera-photo-import</c>). The initializer creates EVERY missing default
/// folder, so calling it to get one would resurrect folders a cabinet had deleted; and a second copy of the id rule
/// would mint a different id for the same folder the day either side changed.</para>
///
/// <para>⚠️ <b>The id is deterministic</b> (patient + name), so two requests creating the same folder at once collide
/// on the primary key instead of producing two « Photographie » folders side by side.</para>
/// </summary>
public static class DefaultPatientFolders
{
    public const string PhotographyKey = "photographie";
    public const string RadiographyKey = "radiographie";

    /// <summary>In the order the initializer creates them, which is the order a new patient's tab shows.</summary>
    public static readonly IReadOnlyList<(string Key, string Name)> All = new[]
    {
        (RadiographyKey, "Radiographie"),
        (PhotographyKey, "Photographie"),
        ("analyses", "Analyses Biologiques"),
        ("liaisons", "Lettres de Liaisons"),
    };

    public static string? NameFor(string? key) =>
        All.FirstOrDefault(folder => string.Equals(folder.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase)).Name;

    /// <summary>Whether a folder name is one of the four, compared as the initializer always has (case-insensitive).</summary>
    public static bool IsDefaultName(string? name) =>
        All.Any(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase));

    public static Guid IdFor(Guid patientId, string folderName)
    {
        var bytes = Encoding.UTF8.GetBytes($"{patientId}-{folderName}");
        return new Guid(MD5.HashData(bytes));
    }

    public static PatientFolderDto ToDto(PatientFolder folder) => new()
    {
        Id = folder.Id,
        PatientId = folder.PatientId,
        ParentFolderId = folder.ParentFolderId,
        Name = folder.Name,
        FileCount = folder.Files.Count,
        SubFolderCount = folder.SubFolders.Count,
        CreatedAt = folder.CreatedAt,
        UpdatedAt = folder.UpdatedAt
    };
}
