using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// File-backed <see cref="IGoogleTokenStore"/> (US-3 / FR-E3). Persists the Google OAuth refresh token to
/// a gitignored per-install file (<c>.local/google-refresh-token</c> by default) instead of rewriting the
/// committed <c>appsettings.json</c>. Registered <b>Singleton</b>: a save writes the file atomically and
/// updates an in-memory cache; a read serves the cache (populated lazily from the file, falling back to
/// <c>GoogleCalendar:RefreshToken</c> for Cloud / upgrade back-compat — R-5). The cache is always updated on
/// save, so a first read that found no token never hides a later save.
/// </summary>
public sealed class FileGoogleTokenStore : IGoogleTokenStore
{
    private const string RefreshTokenConfigKey = "GoogleCalendar:RefreshToken";

    private readonly IConfiguration _configuration;
    private readonly ILogger<FileGoogleTokenStore> _logger;
    private readonly string _filePath;
    private readonly object _lock = new();

    private string? _cachedToken;
    private bool _loaded;

    public FileGoogleTokenStore(IConfiguration configuration, ILogger<FileGoogleTokenStore> logger)
    {
        _configuration = configuration;
        _logger = logger;
        // Same gitignored .local/ folder as the per-install signing key. Overridable for tests/installers.
        _filePath = configuration["GoogleCalendar:RefreshTokenPath"]
                    ?? Path.Combine(Directory.GetCurrentDirectory(), ".local", "google-refresh-token");
    }

    public string? GetRefreshToken()
    {
        lock (_lock)
        {
            if (!_loaded)
            {
                _cachedToken = ReadFromFile() ?? _configuration[RefreshTokenConfigKey];
                _loaded = true;
            }
            return _cachedToken;
        }
    }

    public async Task SaveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("Refresh token must not be null or empty.", nameof(refreshToken));
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Atomic write: stage to a temp file then move over the target so a crash mid-write can't leave
        // a truncated token file.
        var tempPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, refreshToken, cancellationToken);
        File.Move(tempPath, _filePath, overwrite: true);

        lock (_lock)
        {
            _cachedToken = refreshToken;
            _loaded = true;
        }

        _logger.LogInformation("Google refresh token persisted to the local token store.");
    }

    private string? ReadFromFile()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var content = File.ReadAllText(_filePath).Trim();
                return string.IsNullOrWhiteSpace(content) ? null : content;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the Google refresh token store at {Path}.", _filePath);
        }

        return null;
    }
}
