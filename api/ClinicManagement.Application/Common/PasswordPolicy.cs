namespace ClinicManagement.Application.Common;

/// <summary>
/// Single source of truth for the local-account password policy (FR-B2), so the minimum
/// length can't drift between first-run setup, self-registration, and password change.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
}
