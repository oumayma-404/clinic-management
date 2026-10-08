namespace ClinicManagement.API.Models;

/// <summary>« Installer le PC de secours ici… » on any PC (clinic-pc-copy AC-1.5): an admin's proof, and this PC's name.</summary>
public sealed record RelayPairingCodeSignInRequest(string? Email, string? Password, string? TotpCode, string? Label);
