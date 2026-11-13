using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Models;

/// <summary>Opening a resumable upload: everything that can be judged before a byte is sent.</summary>
public class StartFileUploadRequest
{
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// The whole file's length. ⚠️ A <b>claim</b>, and it is checked against the bytes that actually arrive
    /// before anything is stored — asking for it is what lets an oversized or wrong-format upload be refused in
    /// the first request rather than after four minutes of the clinic's uplink.
    /// </summary>
    public long FileSize { get; set; }

    public Guid? FolderId { get; set; }

    public string? Description { get; set; }

    /// <summary>A camera original going up in parts (clinic-PC install) — see <see cref="UploadFileRequest"/>.</summary>
    public string? CaptureKey { get; set; }

    public DateTime? CapturedAtLocal { get; set; }

    public int? CapturedAtOffsetMinutes { get; set; }

    public string? CameraModel { get; set; }

    public Application.Features.Files.CameraCaptureInput ToCapture() =>
        new(CaptureKey, CapturedAtLocal, CapturedAtOffsetMinutes, CameraModel);
}

/// <summary>Finishing one: the assembled file, plus the stand-in image the browser built along the way.</summary>
public class CompleteFileUploadRequest
{
    /// <summary>Optional, and never load-bearing — an unusable one is dropped and the file is stored regardless.</summary>
    public IFormFile? Preview { get; set; }
}
