using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Models;

public class UploadFileRequest
{
    public IFormFile File { get; set; } = null!;
    public Guid? FolderId { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// A small stand-in image the browser built from the same file, for the drawer's thumbnails. Optional, and
    /// an unusable one is dropped rather than refusing the upload — see <c>PatientFilePreviewStore</c>.
    /// </summary>
    public IFormFile? Preview { get; set; }

    /// <summary>
    /// Set by « Importer les photos » only (<c>features/camera-photo-import</c>): the shot's key, its capture time as
    /// clinic wall-clock (<c>2026-11-12T09:14:03</c>, no offset), the camera's own offset when it wrote one, and the
    /// camera's model.
    /// </summary>
    public string? CaptureKey { get; set; }

    public DateTime? CapturedAtLocal { get; set; }

    public int? CapturedAtOffsetMinutes { get; set; }

    public string? CameraModel { get; set; }

    public Application.Features.Files.CameraCaptureInput ToCapture() =>
        new(CaptureKey, CapturedAtLocal, CapturedAtOffsetMinutes, CameraModel);
}








