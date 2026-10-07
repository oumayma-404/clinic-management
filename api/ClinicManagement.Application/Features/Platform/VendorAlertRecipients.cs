using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>
/// Who the vendor's alert e-mails go to: the addresses the operator named, else every active console account. One rule
/// for every vendor alert — the PC de secours's (<c>clinic-pc-copy</c> AC-9.2) and the off-server backups'
/// (<c>server-loss-recovery</c> Part 3) — so the vendor configures one address and is told about both.
/// </summary>
public static class VendorAlertRecipients
{
    /// <summary>
    /// Comma-separated addresses; unset ⇒ every active console account. The key carries the backup feature's name
    /// because that feature introduced the channel; it now names the vendor's alert inbox for everything.
    /// </summary>
    public const string AlertEmailKey = "Backup:AlertEmail";

    public static async Task<IReadOnlyList<string>> ResolveAsync(
        string? configured, IPlatformAccountRepository accounts, CancellationToken cancellationToken = default)
    {
        var named = (configured ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return named.Count > 0 ? named : await accounts.GetActiveEmailsAsync(cancellationToken);
    }
}
