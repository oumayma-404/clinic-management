using ClinicManagement.Application.DTOs;

namespace ClinicManagement.API.Models;

/// <summary>
/// Public clinic self-signup payload (<c>HostedMultiTenant</c> only). Nothing here is persisted as submitted:
/// the password is hashed before the row is written and the address is normalised the way
/// <c>User.CreateLocalUser</c> normalises it.
/// </summary>
public class ClinicSignUpRequest
{
    public string ClinicName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// ⚠️ The property name is what <c>AuthAttemptAccount</c> lifts out of the body before model binding, so the
    /// rate limiter partitions this endpoint per submitted account rather than per address. Renaming it silently
    /// moves the whole practice behind one NAT address back onto a shared budget.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }

    /// <summary>Optional: the signer-up is also the cabinet's practitioner (the single-dentist case).</summary>
    public DoctorPersonalInfoDto? DoctorInfo { get; set; }

    /// <summary>
    /// The onboarding wizard's « Horaires » step. Optional — a clinic that skips it gets no restriction, which is
    /// what <c>WorkingHoursResolver</c> already means by « none ».
    /// </summary>
    public string? WorkingHoursJson { get; set; }
}

/// <summary>The verification link's payload — the raw token, which exists only in the email that carried it.</summary>
public class ClinicSignUpVerifyRequest
{
    public string Token { get; set; } = string.Empty;
}
