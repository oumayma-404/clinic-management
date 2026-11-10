using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public void A_message_with_no_kicker_or_bullets_renders_neither_and_keeps_the_heading_row_unchanged()
    {
        var content = AnyContent();
        var html = EmailLayout.Html(content);

        Assert.Contains("<tr><td class=\"px\" style=\"padding:20px 32px 0 32px;\">\n  <h1", html.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("letter-spacing:0.02em", html);
        Assert.DoesNotContain("width:34px", html);
        Assert.StartsWith(content.Title + "\n", EmailLayout.PlainText(content));
    }

    [Fact]
    public void The_kicker_is_the_first_line_of_both_parts_and_sits_above_the_heading()
    {
        var content = AnyContent() with { Kicker = "Happy Apexing !" };
        var html = EmailLayout.Html(content);

        Assert.True(html.IndexOf("Happy Apexing !", StringComparison.Ordinal) < html.IndexOf("<h1", StringComparison.Ordinal));
        Assert.StartsWith("Happy Apexing !\n\n" + content.Title, EmailLayout.PlainText(content));
    }

    [Fact]
    public void Bullets_carry_their_marker_in_the_html_and_in_the_plain_text_in_order()
    {
        var content = AnyContent() with
        {
            Bullets = [new EmailBullet("🚀", "Les rappels partent la veille."), new EmailBullet("📅", "Agenda plus rapide.")]
        };
        var html = EmailLayout.Html(content);
        var text = EmailLayout.PlainText(content);

        Assert.Contains("&#128640;", html);
        Assert.True(html.IndexOf("Les rappels partent la veille.", StringComparison.Ordinal)
                    < html.IndexOf("Agenda plus rapide.", StringComparison.Ordinal));
        Assert.Contains("🚀 Les rappels partent la veille.\n📅 Agenda plus rapide.\n", text);
    }

    [Fact]
    public void A_bullet_is_escaped_like_every_other_value()
    {
        var html = EmailLayout.Html(AnyContent() with { Bullets = [new EmailBullet("<i>", "<b>gras</b>")] });

        Assert.DoesNotContain("<b>gras", html);
        Assert.Contains("&lt;b&gt;gras&lt;/b&gt;", html);
        Assert.Contains("&lt;i&gt;", html);
    }

    // ── AC-10 byte-identity, and the guide + WhatsApp contact added on 2026-11-10 ────────────────────────────────

    /// <summary>SHA-256 of the output with line endings normalised — the source's raw literals follow the checkout.</summary>
    private static string Hash(string rendered) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rendered.ReplaceLineEndings("\n"))));

    private static EmailContent Minimal() => new()
    {
        Title = "Votre mot de passe a été modifié",
        Preheader = "Si ce n'est pas vous, prévenez votre administrateur.",
        Intro = ["Le mot de passe de votre compte vient d'être modifié."]
    };

    private static EmailContent NewsWithoutGuides() => AnyContent() with
    {
        Kicker = "Happy Apexing ! 🦷✨",
        Bullets = [new EmailBullet("🚀", "Les rappels partent la veille."), new EmailBullet("📅", "Agenda plus rapide.")]
    };

    /// <summary>
    /// ⚠️ <b>AC-10, as bytes.</b> These hashes are the renderer's output for three fixtures <b>as it stood before the
    /// per-row guide and the WhatsApp contact were added</b> (computed from that commit's <c>EmailLayout</c>): a full
    /// transactional e-mail, a minimal one, and a news e-mail whose bullets carry no guide. A structural assertion
    /// cannot prove « unchanged »; a hash can. If this goes red, the change reached every e-mail that does not use
    /// the new parts — if that was intended, recompute the hash in the same commit and say so in its message.
    /// </summary>
    [Theory]
    [InlineData("full", "277ABF827BC29F61D4242BA5439DE7524A2E78B251B35C1CB062A16F9579897A", "E92F84769D4A2606EBD9C91E177739D31B469491A21AC1458186DD2AB02D5F29")]
    [InlineData("minimal", "587E75E43EA4C40B72F0DCB9F10576F0276B48B031633BED157930988E260A97", "78F65AB43FCBF7AA8D12B76228B02DFFC17EFF871CC09DEB142CB5DF6ED95480")]
    [InlineData("news-without-guides", "F0FAB5F600141159DC74064EC6A98937D7EA50A27F4FECF50D781AA3AB519A99", "10F45281F5D8339EF224F78B4F3C7C5B87769CE55BF2269955244618293D67AB")]
    public void An_Email_That_Uses_Neither_A_Guide_Nor_A_Contact_Renders_Byte_Identical_To_Before(
        string fixture, string htmlHash, string textHash)
    {
        var content = fixture switch
        {
            "full" => AnyContent(),
            "minimal" => Minimal(),
            _ => NewsWithoutGuides(),
        };

        Assert.Equal((htmlHash, textHash), (Hash(EmailLayout.Html(content)), Hash(EmailLayout.PlainText(content))));
    }

    [Fact]
    public void A_Bullet_Built_With_Empty_Guide_Parts_Renders_As_A_Two_Field_Bullet()
    {
        var bare = NewsWithoutGuides();
        var empty = bare with
        {
            Bullets = bare.Bullets.Select(b => b with { Description = "  ", StepsTitle = "Comment ça marche :", Steps = [] }).ToList()
        };

        Assert.Equal(EmailLayout.Html(bare), EmailLayout.Html(empty));
        Assert.Equal(EmailLayout.PlainText(bare), EmailLayout.PlainText(empty));
    }

    private static EmailContent WithGuide() => AnyContent() with
    {
        Bullets =
        [
            new EmailBullet("🌴", "Congés des secrétaires", "La secrétaire demande, vous décidez.", "Comment ça marche :",
                ["Paramètres → Équipe : activez « Congés »", "Utilisateurs → la secrétaire : fixez son droit", "Elle demande depuis « Mes congés »"]),
            new EmailBullet("🚀", "Agenda plus rapide."),
        ]
    };

    [Fact]
    public void A_Guide_Renders_Under_Its_Bullet_In_Order_Description_Then_Heading_Then_Numbered_Steps()
    {
        var html = EmailLayout.Html(WithGuide());

        int At(string fragment)
        {
            var index = html.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(index >= 0, $"« {fragment} » missing from the HTML");
            return index;
        }

        var line = At("Cong&#233;s des secr&#233;taires");
        var description = At("La secr&#233;taire demande, vous d&#233;cidez.");
        var heading = At("Comment &#231;a marche :");
        var first = At(">1.</td>");
        var third = At(">3.</td>");
        var stepThree = At("Elle demande depuis &#171; Mes cong&#233;s &#187;");
        var nextBullet = At("Agenda plus rapide.");

        Assert.True(line < description && description < heading && heading < first && first < third && third < stepThree && stepThree < nextBullet);
        Assert.DoesNotContain(">4.</td>", html);
        Assert.DoesNotContain("<ol", html);
    }

    [Fact]
    public void A_Guide_Reaches_The_Plain_Text_Indented_Under_Its_Line()
    {
        var text = EmailLayout.PlainText(WithGuide());

        Assert.Contains(
            "🌴 Congés des secrétaires\n"
            + "   La secrétaire demande, vous décidez.\n"
            + "   Comment ça marche :\n"
            + "   1. Paramètres → Équipe : activez « Congés »\n"
            + "   2. Utilisateurs → la secrétaire : fixez son droit\n"
            + "   3. Elle demande depuis « Mes congés »\n"
            + "\n"
            + "🚀 Agenda plus rapide.\n",
            text);
    }

    [Fact]
    public void A_Guide_Is_Escaped_Like_Every_Other_Value()
    {
        var html = EmailLayout.Html(AnyContent() with
        {
            Bullets = [new EmailBullet("🌴", "x", "<b>gras</b>", "<i>titre</i>", ["<script>alert(1)</script>"])]
        });

        Assert.DoesNotContain("<b>gras", html);
        Assert.DoesNotContain("<i>titre", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;b&gt;gras&lt;/b&gt;", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    private static EmailContent WithContact(string url = "https://wa.me/21651230456?text=Bonjour%2C%20ici%20Cabinet.") =>
        AnyContent() with
        {
            Contact = new EmailContact("Une question ? Écrivez-nous sur WhatsApp", "💬 WhatsApp +216 51 230 456", url)
        };

    [Fact]
    public void The_Contact_Is_A_Link_After_The_Button_And_Before_The_Sign_Off_Never_A_Second_Button()
    {
        var html = EmailLayout.Html(WithContact());

        var button = html.IndexOf("V&#233;rifier mon adresse", StringComparison.Ordinal);
        var prompt = html.IndexOf("Une question ? &#201;crivez-nous sur WhatsApp", StringComparison.Ordinal);
        var link = html.IndexOf("href=\"https://wa.me/21651230456?text=Bonjour%2C%20ici%20Cabinet.\"", StringComparison.Ordinal);
        var outro = html.IndexOf("Rien n&#39;a &#233;t&#233; cr&#233;&#233;.", StringComparison.Ordinal);

        Assert.True(button >= 0 && prompt > button && link > prompt && outro > link, $"{button} {prompt} {link} {outro}");
        Assert.Contains("&#128172; WhatsApp +216 51 230 456</a>", html);
        // The button's padded anchor exists exactly once: the contact is text, not a competing call to action.
        Assert.Single(html.Split("padding:15px 30px").Skip(1));
    }

    [Fact]
    public void The_Contact_Reaches_The_Plain_Text_With_Its_Url_On_A_Line_Of_Its_Own()
    {
        var text = EmailLayout.PlainText(WithContact());

        Assert.Contains(
            "Une question ? Écrivez-nous sur WhatsApp\n💬 WhatsApp +216 51 230 456 :\nhttps://wa.me/21651230456?text=Bonjour%2C%20ici%20Cabinet.\n\n",
            text);
        Assert.True(text.IndexOf("Installez l'application", StringComparison.Ordinal) < text.IndexOf("Une question ? É", StringComparison.Ordinal));
        Assert.True(text.IndexOf("WhatsApp +216", StringComparison.Ordinal) < text.IndexOf("Rien n'a été créé.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_Contact_Is_Escaped_Into_The_Href_And_The_Text()
    {
        var html = EmailLayout.Html(AnyContent() with
        {
            Contact = new EmailContact("<b>q</b>", "<i>l</i>", "https://wa.me/1?text=a\"onmouseover=\"x")
        });

        Assert.DoesNotContain("<b>q", html);
        Assert.DoesNotContain("<i>l", html);
        Assert.DoesNotContain("a\"onmouseover", html);
        Assert.Contains("a&quot;onmouseover=&quot;x", html);
    }
}
