using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Models;

public class UploadFileRequest
{
    public IFormFile File { get; set; } = null!;
    public Guid? FolderId { get; set; }
    public string? Description { get; set; }
}








