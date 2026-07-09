namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Persists the Google OAuth refresh token outside the committed <c>appsettings.json</c> (US-3 / FR-E3).
/// The previous callback rewrote the token back into the tracked config file at runtime; this seam
/// stores it in a gitignored per-install file instead. Registered <b>Singleton</b> so an in-memory cache
/// serves the immediate read-after-write behavior the old in-place config set provided (no restart needed).
/// </summary>
public interface IGoogleTokenStore
{
    /// <summary>
    /// Returns the current refresh token, or <c>null</c> when none is stored. Reads from the per-install
    /// file, falling back to <c>GoogleCalendar:RefreshToken</c> in configuration (Cloud / back-compat — R-5).
    /// </summary>
    string? GetRefreshToken();

    /// <summary>Persists <paramref name="refreshToken"/> durably and updates the in-memory cache.</summary>
    Task SaveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}
