using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Models;

/// <summary>
/// What arrives when a file stays at the cabinet: a description of bytes this deployment will never hold, plus an
/// optional small image standing in for them.
///
/// <para>⚠️ <c>FileId</c> is supplied by the caller because the coffre path is derived from it — the browser has to
/// know the id before it writes the file, so minting one here would name something that is not on the disk. It is
/// treated as untrusted input all the same.</para>
/// </summary>
public class RegisterVaultFileRequest
{
    public Guid FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }

    /// <summary>Lower-case hex SHA-256 of the original, computed while it was being written.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public Guid? FolderId { get; set; }
    public string? Description { get; set; }

    /// <summary>Optional, and never load-bearing: a registration is never refused for want of a preview.</summary>
    public IFormFile? Preview { get; set; }
}
