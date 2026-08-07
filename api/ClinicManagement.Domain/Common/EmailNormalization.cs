namespace ClinicManagement.Domain.Common;

/// <summary>
/// The single answer to « what is the stored form of an email address? ».
///
/// <para>It existed as three separate inline copies of <c>Trim().ToLowerInvariant()</c> — in
/// <c>User.CreateLocalUser</c>, in <c>UserRepository.GetByEmailAsync</c> and in <c>ClinicSignup</c> — and the
/// three must agree or a signup's « does this address already have an account? » check silently misses the
/// account it was written to find.</para>
/// </summary>
public static class EmailNormalization
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
