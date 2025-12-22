using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Infrastructure.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(string basePath, ILogger<LocalFileStorageService> logger)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    public async Task<string> SaveFileAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var sanitizedFileName = SanitizeFileName(fileName);
        var filePath = Path.Combine(_basePath, sanitizedFileName);
        var uniqueFilePath = GetUniqueFilePath(filePath);

        using (var fileStreamWriter = new FileStream(uniqueFilePath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fileStreamWriter, cancellationToken);
        }

        _logger.LogInformation("File saved: {FilePath}", uniqueFilePath);
        return uniqueFilePath;
    }

    public Task<Stream> GetFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return Task.FromResult<Stream>(fileStream);
    }

    public Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("File deleted: {FilePath}", filePath);
        }

        return Task.CompletedTask;
    }

    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(filePath));
    }

    private string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }

    private string GetUniqueFilePath(string filePath)
    {
        if (!File.Exists(filePath))
            return filePath;

        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        var counter = 1;

        string newFilePath;
        do
        {
            var newFileName = $"{fileNameWithoutExtension}_{counter}{extension}";
            newFilePath = Path.Combine(directory, newFileName);
            counter++;
        } while (File.Exists(newFilePath));

        return newFilePath;
    }
}



