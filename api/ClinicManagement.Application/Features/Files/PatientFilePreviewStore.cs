using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Files;

/// <summary>
/// Storing the small stand-in image a patient file carries — <b>one copy, for both doors</b>.
///
/// <para>It began inside <c>RegisterVaultFileCommand</c>, where the coffre was the only caller. When the hosted
/// upload started carrying a preview too, copying twenty lines would have given the product two answers to
/// « how big may a stand-in be, and what happens to a bad one? » — the defect shape this repository keeps
/// meeting. The rules live here and nowhere else.</para>
///
/// <para>⚠️ <b>A preview never fails an upload.</b> It is a convenience for the file list and for the machines
/// that cannot reach the coffre, while the row is the record. Oversized, unreadable, or in a format the
/// <see cref="FileUploadProfile.ProfileImage"/> door refuses — every one of those is dropped, logged at
/// information, and the file is stored regardless.</para>
///
/// <para>⚠️ <b>Validated through <see cref="FileUploadProfile.ProfileImage"/>, deliberately.</b> A preview is a
/// small raster this deployment will serve back <i>inline</i> — unlike every other stored blob, which leaves as
/// an <c>attachment</c> — so it goes through the narrowest door in the catalog rather than the patient
/// drawer's. That door is PNG and JPEG, which is what the browser-side builder encodes.</para>
/// </summary>
public static class PatientFilePreviewStore
{
    /// <summary>
    /// Stores the preview and returns its object key, or null when there is nothing usable to store.
    /// </summary>
    /// <param name="fileId">
    /// The file the preview stands for. It names the object, so a preview cannot outlive its file's key space
    /// or collide with another's.
    /// </param>
    public static async Task<string?> StoreAsync(
        IFileStorage fileStorage,
        ILogger logger,
        Guid fileId,
        Guid clinicId,
        Stream? previewStream,
        string? previewFileName,
        long previewSize,
        CancellationToken cancellationToken)
    {
        if (previewStream == null || previewSize <= 0 || previewSize > FileTypeCatalog.PreviewBytes)
        {
            return null;
        }

        var validation = await FileUploadValidator.ValidateAsync(
            FileUploadProfile.ProfileImage,
            previewFileName,
            previewSize,
            previewStream,
            cancellationToken);

        if (validation.IsFailure)
        {
            logger.LogInformation(
                "Dropped an unusable preview for patient file {FileId}: {Reason}", fileId, validation.Error);
            return null;
        }

        var preview = validation.Value!;
        var extension = ExtensionSuffix(preview.FileName);

        return await fileStorage.UploadAsync(
            preview.Content, preview.ContentType, clinicId, $"previews/{fileId:D}{extension}", cancellationToken);
    }

    private static string ExtensionSuffix(string fileName)
    {
        var extension = FileNameSanitizer.ExtensionOf(fileName);

        return extension.Length == 0 ? string.Empty : $".{extension}";
    }
}
