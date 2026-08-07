using ClinicManagement.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="ITtnSecretProtector"/> over ASP.NET Core Data Protection, mirroring
/// <see cref="ReminderSecretProtector"/> with its own purpose so the two subsystems' ciphertext cannot be
/// interchanged. The key ring is persisted (see <c>Extensions.AddInfrastructure</c>) so ciphertext survives
/// restarts and redeploys.
/// </summary>
public class TtnSecretProtector : ITtnSecretProtector
{
    private const string Purpose = "ClinicManagement.TtnSecrets.v1";

    private readonly IDataProtector _protector;

    public TtnSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
