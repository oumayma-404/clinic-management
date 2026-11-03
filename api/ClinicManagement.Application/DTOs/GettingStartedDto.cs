namespace ClinicManagement.Application.DTOs;

/// <summary>One « Premiers pas » step and whether the cabinet's own data says it is done.</summary>
public sealed record GettingStartedStepDto(string Key, bool Done);

/// <summary>
/// One account of the cabinet and whether it has signed in from the phone app — the « Déjà fait » chips of
/// « Votre téléphone ». <paramref name="Platform"/> is <c>User.PhoneAppSurfaces</c>' value, null until then.
/// </summary>
public sealed record PhoneAppPersonDto(string Name, string Role, string? Platform, DateTime? FirstSignInAt);

/// <summary>
/// « Premiers pas » for the signed-in administrator's cabinet (<c>guided-first-days</c> B).
///
/// <para><paramref name="Visible"/> is false after the cabinet's first 30 days and once every step is done; the
/// lists are then empty, because nothing was read.</para>
/// </summary>
public sealed record GettingStartedDto(
    bool Visible,
    IReadOnlyList<GettingStartedStepDto> Steps,
    IReadOnlyList<PhoneAppPersonDto> PhoneApp)
{
    public static GettingStartedDto Hidden { get; } = new(false, [], []);
}
