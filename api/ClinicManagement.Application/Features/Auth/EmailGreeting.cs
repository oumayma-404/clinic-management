namespace ClinicManagement.Application.Features.Auth;

/// <summary>
/// The opening line of a transactional e-mail addressed to one account.
///
/// <para><c>User.FullName</c> is nullable — an account provisioned by a console verb or restored from an archive
/// may hold none — and « Bonjour , » is the shape that reaches a real person when nobody thinks about it. Shared
/// by the two password-reset mails rather than written twice, so the day one of them is reworded the other does not
/// quietly keep the old defect.</para>
/// </summary>
internal static class EmailGreeting
{
    public static string For(string? fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? "Bonjour," : $"Bonjour {fullName.Trim()},";
}
