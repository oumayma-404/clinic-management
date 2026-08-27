using ClinicManagement.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="IReminderSecretProtector"/> over ASP.NET Core Data Protection: a purpose-scoped
/// <see cref="IDataProtector"/> encrypts/decrypts per-clinic reminder secrets. The key ring is persisted to
/// disk (see <c>Extensions.AddInfrastructure</c>) so ciphertext survives restarts. A rotated/unavailable key
/// makes <see cref="Unprotect"/> throw — callers translate that into "not configured" (never a crash).
/// </summary>
public class ReminderSecretProtector : IReminderSecretProtector
{
    private const string Purpose = "ClinicManagement.ReminderSecrets.v1";

    private readonly IDataProtector _protector;

    public ReminderSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
