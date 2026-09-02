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
}








