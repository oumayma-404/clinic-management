namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// The time-based one-time code (RFC 6238) half of the console's second factor (FR-1, AC-1.2).
///
/// <para>Declared here and implemented in Infrastructure over <c>Otp.NET</c>, so the Domain keeps its zero
/// package references and the handlers stay testable without a real HMAC.</para>
/// </summary>
public interface ITotpService
{
    /// <summary>
    /// A fresh base32 shared secret, in the form an authenticator app accepts. For a console account only the
    /// bootstrap verb calls this (AC-1.3). A clinic account's sign-in enrolment (<c>EnrolTotpCommand</c>) mints one
    /// at request time, but only for an account that holds no confirmed factor (or a replacement grant earned with a
    /// recovery code), after the password is verified — so it never lets a password alone replace a working factor.
    /// </summary>
    string GenerateSecret();

    /// <summary>
    /// True when <paramref name="code"/> is valid for <paramref name="base32Secret"/> now.
    ///
    /// <para>⚠️ The implementation allows <b>one step either side</b>. Not laxity: a clinic PC and a phone
    /// disagree by seconds routinely, and with a zero window a code typed at the end of its period is refused
    /// while looking correct — which reads as « the app is broken » and is the fastest route to somebody turning
    /// the factor off.</para>
    /// </summary>
    bool VerifyCode(string base32Secret, string code);
}
