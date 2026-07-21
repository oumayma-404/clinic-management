using ClinicManagement.Application.DTOs;

namespace ClinicManagement.API.Models;

/// <summary>First-run setup payload (Local mode): creates the clinic + first admin.</summary>
public class SetupRequest
{
    public string ClinicName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }

    /// <summary>
    /// Optional: when the first admin is also the cabinet's practitioner (the single-dentist case), the
    /// specialty (+ derived name) needed to create and link a Doctor record so "Mon profil" / cachet /
    /// certificats work. Null → an admin-only account with no practitioner profile.
    /// </summary>
    public DoctorPersonalInfoDto? DoctorInfo { get; set; }
}
