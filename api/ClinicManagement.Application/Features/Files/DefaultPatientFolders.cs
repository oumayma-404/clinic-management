using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Application.Features.Files;

/// <summary>
/// The four folders every patient's « Fichiers » starts with, the one rule for their ids, and the one lookup that
/// says whether a patient already has one.
///
/// <para>⚠️ <b>Moved out of <c>InitializeDefaultFoldersCommand</c>, not copied</b>, when the camera import needed
/// « Photographie » on its own (<c>features/camera-photo-import</c>). The initializer creates EVERY missing default
/// folder, so calling it to get one would resurrect folders a cabinet had deleted; and a second copy of the id rule
/// would mint a different id for the same folder the day either side changed.</para>
///
/// <para>⚠️ <b>The id is deterministic</b> (patient + name), so two requests creating the same folder at once collide
/// on the primary key instead of producing two « Photographie » folders side by side.</para>
///
/// <para>⚠️ <b>A default folder is found by its id first, then by its name</b> (<see cref="Find"/>). « Renommer »
/// keeps the id, so a renamed « Photographie » still owns <see cref="IdFor"/>: a lookup by name alone missed it and
/// created a second entity under the same key, which EF refuses before any SQL runs — every photo filed to that
/// patient failed, and so did « Changer de patient » towards them. Nothing re-parents a folder, so a default folder is
/// always at the root.</para>
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

    /// <summary>
    /// Which default folder <paramref name="folder"/> is — by its id first (a renamed « Photographie » is still the
    /// photography folder), then by its name — or null for a folder the cabinet made.
    /// </summary>
    public static string? DefaultNameOf(PatientFolder? folder)
    {
        if (folder is null)
        {
            return null;
        }

        var byId = All.FirstOrDefault(d => IdFor(folder.PatientId, d.Name) == folder.Id).Name;
        return byId ?? All.FirstOrDefault(d =>
            string.Equals(d.Name, folder.Name, StringComparison.OrdinalIgnoreCase)).Name;
    }

    /// <summary>The root folder that IS this default: the one holding its id (a rename keeps it), else one of that name.</summary>
    public static PatientFolder? Find(IEnumerable<PatientFolder> roots, Guid patientId, string name)
    {
        var list = roots as IReadOnlyCollection<PatientFolder> ?? roots.ToList();
        var id = IdFor(patientId, name);
        return list.FirstOrDefault(folder => folder.Id == id)
               ?? list.FirstOrDefault(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The patient's default folder <paramref name="name"/>, found or created and SAVED — the camera import's folder
    /// and « Changer de patient »'s landing place.
    ///
    /// <para>⚠️ <b>A patient with no folder at all gets the whole default set</b>, exactly as the « Fichiers » tab
    /// seeds it: the tab seeds only a patient with no folder, so a patient whose first file came from the camera would
    /// otherwise keep « Photographie » alone for good. A patient who has folders gets only the one asked for — the
    /// others may have been deleted on purpose.</para>
    ///
    /// <para>⚠️ In its own save: two requests creating the same folder collide on <see cref="IdFor"/>, and the loser
    /// re-reads and returns the winner's folder.</para>
    /// </summary>
    public static async Task<PatientFolder> EnsureAsync(
        IPatientFolderRepository folders,
        IUnitOfWork unitOfWork,
        Patient patient,
        string name,
        CancellationToken cancellationToken)
    {
        var roots = (await folders.GetRootFoldersByPatientIdAsync(patient.Id, cancellationToken)).ToList();
        var found = Find(roots, patient.Id, name);
        if (found is not null)
        {
            return found;
        }

        var names = roots.Count == 0 ? All.Select(folder => folder.Name) : new[] { name };
        var settled = await CreateAsync(folders, unitOfWork, patient, roots, names.ToList(), cancellationToken);
        return Find(settled, patient.Id, name)
               ?? throw new InvalidOperationException($"The default folder « {name} » could not be created.");
    }

    /// <summary>
    /// Creates every default folder the patient does not have — by id or by name, as <see cref="Find"/> asks — and
    /// returns the root folders after it. The « Fichiers » tab's seed.
    /// </summary>
    public static async Task<IReadOnlyList<PatientFolder>> EnsureAllAsync(
        IPatientFolderRepository folders,
        IUnitOfWork unitOfWork,
        Patient patient,
        CancellationToken cancellationToken)
    {
        var roots = (await folders.GetRootFoldersByPatientIdAsync(patient.Id, cancellationToken)).ToList();
        var missing = All
            .Where(folder => Find(roots, patient.Id, folder.Name) is null)
            .Select(folder => folder.Name)
            .ToList();

        return missing.Count == 0
            ? roots
            : await CreateAsync(folders, unitOfWork, patient, roots, missing, cancellationToken);
    }

    private static async Task<IReadOnlyList<PatientFolder>> CreateAsync(
        IPatientFolderRepository folders,
        IUnitOfWork unitOfWork,
        Patient patient,
        IReadOnlyList<PatientFolder> roots,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        var created = names
            .Select(name => new PatientFolder(IdFor(patient.Id, name), patient.Id, patient.ClinicId, name))
            .ToList();

        try
        {
            foreach (var folder in created)
            {
                await folders.AddAsync(folder, cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return roots.Concat(created).ToList();
        }
        catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
        {
            // Another request created them a moment ago under the same ids: drop the losing inserts, read the winner's.
            foreach (var folder in created)
            {
                unitOfWork.StopTracking(folder);
            }

            return (await folders.GetRootFoldersByPatientIdAsync(patient.Id, cancellationToken)).ToList();
        }
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
