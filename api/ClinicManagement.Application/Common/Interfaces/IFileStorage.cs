namespace ClinicManagement.Application.Common.Interfaces;

public interface IFileStorage
{
    Task<string> UploadAsync(Stream file, string contentType, CancellationToken cancellationToken = default);
    Task<string> UploadAsync(Stream file, string contentType, string customPath, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}







