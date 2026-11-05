namespace ClinicManagement.Application.DTOs;

/// <summary>One « Premiers pas » step: done by the cabinet's own data or ticked by hand (<paramref name="DoneByHand"/> says which).</summary>
/// <param name="DoneByHand">Done only because the person ticked it — the card offers to untick it.</param>
public sealed record GettingStartedStepDto(string Key, bool Done, bool DoneByHand = false);

/// <summary>
/// One account of the cabinet and whether it has signed in from the phone app — the « Déjà fait » chips of
/// « Votre téléphone ». <paramref name="Platform"/> is <c>User.PhoneAppSurfaces</c>' value, null until then;
/// <paramref name="IsYou"/> marks the reader's own row (« Vous »).
/// </summary>
public sealed record PhoneAppPersonDto(
    string Name, string Role, string? Platform, DateTime? FirstSignInAt, bool IsYou);

/// <summary>
/// « Premiers pas » for the signed-in administrator's cabinet (<c>guided-first-days</c> B).
///
/// <para><paramref name="Visible"/> is false after the cabinet's first 30 days and once every step is done; the
/// lists are then empty, because nothing was read. <paramref name="Completed"/> tells the second case from the first,
/// so the browser can say « Bienvenue dans votre nouveau cabinet ! » whichever page the last step was done on.</para>
/// </summary>
public sealed record GettingStartedDto(
    bool Visible,
    IReadOnlyList<GettingStartedStepDto> Steps,
    IReadOnlyList<PhoneAppPersonDto> PhoneApp,
    bool Completed = false)
{
    public static GettingStartedDto Hidden { get; } = new(false, [], []);

    public static GettingStartedDto Finished { get; } = new(false, [], [], Completed: true);
}
