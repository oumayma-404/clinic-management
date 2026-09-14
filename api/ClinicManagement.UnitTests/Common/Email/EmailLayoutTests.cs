using ClinicManagement.Application.Common.Email;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Email;

/// <summary>
/// The e-mail renderer's own contract. Every case here is something that breaks **silently** — an e-mail that
/// arrives, and is wrong.
/// </summary>
public class EmailLayoutTests
{
    private static EmailContent AnyContent() => new()
    {
        Title = "Vérifiez votre adresse",
        Preheader = "Un clic pour créer votre cabinet.",
        Greeting = "Bonjour Dr Benali,",
        Intro = ["Il reste une seule étape."],
        Details =
        [
            new EmailDetail("Adresse du compte", "dr.benali@cabinet.tn"),
            new EmailDetail("Lien", "https://app.example.tn/signup/verifier#token=abc", IsLink: true)
        ],
        Action = new EmailAction("Vérifier mon adresse", "https://app.example.tn/signup/verifier#token=abc"),
        StepsTitle = "Ensuite",
        Steps = [new EmailStep("Installez l'application", "Gratuit.")],
        Outro = ["Rien n'a été créé."],
        Note = "Valable 24 heures."
    };

    /// <summary>
    /// ⚠️ <b>The load-bearing case.</b> The lockup is an <c>EmbeddedResource</c> looked up by a string built
    /// from the project's root namespace and folder path, so <b>moving the file or renaming the folder</b>
    /// breaks it — and the failure surfaces as a broken image in a dentist's inbox, on the one send nobody is
    /// watching, not as a build error. This test is the only thing between that rename and a silent regression.
    /// </summary>
    [Fact]
    public void The_lockup_is_embedded_in_the_assembly_and_is_a_png()
    {
        var bytes = EmailLayout.LogoPng.ToArray();

        Assert.NotEmpty(bytes);

        // The 8-byte PNG signature. Asserted rather than trusting the extension: a resource that resolves to
        // the wrong bytes renders exactly as one that is missing.
        Assert.Equal<byte[]>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], bytes[..8]);
    }

    /// <summary>
    /// The image must be referenced as <c>cid:</c> and never as a URL again. It <i>was</i> a URL on the
    /// deployment's own web origin, which is in the <c>web/</c> image — measured on the live deployment
    /// 2026-09-14, the file answered <c>307</c> while every other public asset answered <c>200</c>, so a real
    /// signup produced a message with no logo. See <see cref="EmailLayout.LogoContentId"/>.
    /// </summary>
    [Fact]
    public void The_html_references_the_lockup_as_an_attached_resource_not_a_url()
    {
        var html = EmailLayout.Html(AnyContent());

        Assert.Contains($"src=\"cid:{EmailLayout.LogoContentId}\"", html);
        Assert.DoesNotContain("apexa-email-lockup.png", html);
    }

    /// <summary>
    /// A blocked or unrenderable image must leave a header behind, so the <c>alt</c> carries the brand name —
    /// and it may not be boxed to the image's own width, which crops the word to an empty rectangle.
    /// </summary>
    [Fact]
    public void A_lockup_that_does_not_render_still_leaves_the_brand_name()
    {
        var html = EmailLayout.Html(AnyContent());

        Assert.Contains("alt=\"APEXA\"", html);

        // `max-width`, so the fallback text can run past the image's own box. ⚠️ Tested with the preceding `;`
        // on purpose: "max-width:140px" itself *contains* "width:140px", so the naive negative assertion here
        // was red against correct markup.
        Assert.Contains("max-width:140px", html);
        Assert.DoesNotContain(";width:140px", html);
    }

    /// <summary>
    /// Both halves come from one object, so anything the reader needs has to survive the plain-text rendering.
    /// The URL is what makes this load-bearing: a text client has no button.
    /// </summary>
    [Fact]
    public void The_plain_text_carries_the_action_url_and_no_markup()
    {
        var text = EmailLayout.PlainText(AnyContent());

        Assert.Contains("https://app.example.tn/signup/verifier#token=abc", text);
        Assert.Contains("Valable 24 heures.", text);
        Assert.Contains("Installez l'application", text);
        Assert.DoesNotContain("<", text);
        Assert.DoesNotContain("&nbsp;", text);
    }

    /// <summary>
    /// ⚠️ The greeting carries <c>User.FullName</c> and the panel an e-mail address, both typed by a stranger at
    /// an anonymous signup form. Un-escaped, a chosen name injects markup into a message its recipient trusts.
    /// </summary>
    [Fact]
    public void User_supplied_text_is_escaped_into_the_html()
    {
        var html = EmailLayout.Html(AnyContent() with
        {
            Greeting = "Bonjour <script>alert(1)</script>,",
            Details = [new EmailDetail("Compte", "a\"b@x.tn")]
        });

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("a\"b@x.tn", html);
    }

    /// <summary>
    /// Every absent part renders as nothing rather than as an empty box — the two security notices carry no
    /// action, no details and no steps at all.
    /// </summary>
    [Fact]
    public void A_message_with_no_action_details_or_steps_renders_neither_button_nor_empty_panel()
    {
        var html = EmailLayout.Html(new EmailContent
        {
            Title = "Votre mot de passe a été modifié",
            Preheader = "Si ce n'est pas vous, prévenez votre administrateur.",
            Intro = ["Le mot de passe de votre compte vient d'être modifié."]
        });

        // The BUTTON's own padded anchor, not "any anchor": the footer legitimately carries a `mailto:` link,
        // which is what the second assertion pins — without it this test would pass on a page with no links at
        // all and would stop being about the button.
        Assert.DoesNotContain("padding:15px 30px", html);
        Assert.Contains("mailto:", html);
        Assert.Contains("Le mot de passe de votre compte vient d", html);
    }
}
