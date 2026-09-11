using ClinicManagement.DesktopShell;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// The disposition rule, driven from the schemes and origins this product actually emits.
///
/// <para>The first test is the reported defect: a <c>tel:</c> link on the patient file replaced the whole app
/// with « Impossible de joindre le serveur du cabinet » while that server was answering <c>/health</c> with 200
/// (2026-09-11).</para>
/// </summary>
public class ExternalNavigationTests
{
    private static ServerConfig Hosted() =>
        new() { Host = "app.apexa.tn", Port = 443, PortIsExplicit = true };

    private static ServerConfig OwnPc() =>
        new() { Host = "192.168.1.20", Port = 5001, PortIsExplicit = true };

    [Theory]
    // The reported crash, in both the form the patient file emits (spaces and all) and the normalised one.
    [InlineData("tel:+33 6 12 34 56 78")]
    [InlineData("tel:+33612345678")]
    [InlineData("tel:20123456")]
    // « Contacter le support » on Abonnement and both rappels cards.
    [InlineData("mailto:support@apexa.tn")]
    public void A_scheme_no_web_view_can_resolve_goes_to_windows(string uri)
    {
        Assert.Equal(NavigationDisposition.HandToOperatingSystem,
            ExternalNavigation.DispositionFor(uri, Hosted()));
    }

    [Theory]
    // « Connecter Google Agenda » — Google refuses to serve sign-in inside an embedded browser, so the shell
    // must never hold this origin.
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?client_id=x")]
    // « Contacter par WhatsApp », the one foreign origin the product opens itself.
    [InlineData("https://wa.me/33612345678")]
    // Same host, different port: another service on the clinic's own PC is not the clinic's app.
    [InlineData("https://app.apexa.tn:8443/elsewhere")]
    [InlineData("http://app.apexa.tn/insecure")]
    public void Another_web_origin_goes_to_the_real_browser(string uri)
    {
        Assert.Equal(NavigationDisposition.OpenInBrowser,
            ExternalNavigation.DispositionFor(uri, Hosted()));
    }

    [Theory]
    [InlineData("https://app.apexa.tn/patients/1")]
    [InlineData("https://app.apexa.tn:443/patients/1")]
    // A hostname is not case-sensitive; the same server spelled louder is the same server.
    [InlineData("https://APP.APEXA.TN/dashboard")]
    public void The_clinics_own_pages_load_in_the_shell(string uri)
    {
        Assert.Equal(NavigationDisposition.LoadInShell, ExternalNavigation.DispositionFor(uri, Hosted()));
        Assert.True(ExternalNavigation.IsClinicDocument(uri, Hosted()));
    }

    /// <summary>
    /// ⚠️ The whitelist's whole point. <c>blob:</c> and <c>data:</c> are how this app previews a decoded file and
    /// hands over a download, and <c>about:blank</c> is where a popup starts — treating « not http(s) » as
    /// « give it to Windows » would have broken all three to fix <c>tel:</c>, and asked <c>ShellExecute</c> to
    /// run whatever a page named.
    /// </summary>
    [Theory]
    [InlineData("blob:https://app.apexa.tn/9f3a-4c21")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("about:blank")]
    [InlineData("javascript:void(0)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    public void Everything_else_stays_with_the_web_view(string uri)
    {
        Assert.Equal(NavigationDisposition.LoadInShell, ExternalNavigation.DispositionFor(uri, Hosted()));
    }

    /// <summary>
    /// ⚠️ And none of them may claim to be the clinic's document, or a failed file preview would raise the
    /// unreachable panel over the whole application.
    /// </summary>
    [Theory]
    [InlineData("blob:https://app.apexa.tn/9f3a-4c21")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("about:blank")]
    [InlineData("https://accounts.google.com/signin")]
    [InlineData("tel:+33612345678")]
    public void Only_a_page_of_the_clinics_server_is_its_document(string uri)
    {
        Assert.False(ExternalNavigation.IsClinicDocument(uri, Hosted()));
    }

    [Fact]
    public void A_clinics_own_pc_is_matched_on_its_own_port()
    {
        Assert.Equal(NavigationDisposition.LoadInShell,
            ExternalNavigation.DispositionFor("https://192.168.1.20:5001/agenda", OwnPc()));
        // 443 on the same machine is not the app — the hosted port is not this deployment's port.
        Assert.Equal(NavigationDisposition.OpenInBrowser,
            ExternalNavigation.DispositionFor("https://192.168.1.20/agenda", OwnPc()));
    }

    /// <summary>
    /// Relative and unparseable targets are the WebView's business. Inventing a hand-off for something we
    /// cannot read is how a shell starts launching things it does not understand.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/patients/1")]
    [InlineData("not a uri at all")]
    public void An_unreadable_target_is_left_alone(string? uri)
    {
        Assert.Equal(NavigationDisposition.LoadInShell, ExternalNavigation.DispositionFor(uri, Hosted()));
        Assert.False(ExternalNavigation.IsClinicDocument(uri, Hosted()));
    }
}
