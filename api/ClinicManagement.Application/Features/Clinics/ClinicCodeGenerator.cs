using System.Security.Cryptography;

namespace ClinicManagement.Application.Features.Clinics;

/// <summary>
/// Generates the clinic self-registration code. This code is the sole gate for the anonymous,
/// LAN-reachable staff registration endpoint (AC-4.2), so it is minted with a cryptographically
/// secure RNG rather than <see cref="System.Random"/>. Shared by clinic creation and code
/// regeneration to keep one implementation.
/// </summary>
internal static class ClinicCodeGenerator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int CodeLength = 6;

    public static string Generate()
    {
        var chars = new char[CodeLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(chars);
    }
}
