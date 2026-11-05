namespace ClinicManagement.Application.Common;

/// <summary>
/// The vendor's public pages a message sends people to — the welcome e-mail and the colleague invitation
/// (<c>guided-first-days</c>). One owner, so two e-mails cannot point at different pages for the same thing.
/// </summary>
public static class ApexaPublicLinks
{
    /// <summary>Where the Windows and Android apps download (and how to add APEXA to an iPhone's home screen).</summary>
    public const string InstallPage = "https://apexa.tn/#telecharger";

    /// <summary>The « Premiers pas » videos.</summary>
    public const string TutorialsPage = "https://apexa.tn/tutoriels.html";
}
