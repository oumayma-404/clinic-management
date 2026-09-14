using System.Net;
using System.Reflection;
using System.Text;

namespace ClinicManagement.Application.Common.Email;

/// <summary>
/// Renders an <see cref="EmailContent"/> as the HTML a mail client paints and as the <c>text/plain</c> alternate
/// that travels beside it. <b>The only place either shape is composed.</b>
///
/// <para><b>Why the HTML looks like 2003.</b> Tables, <c>bgcolor</c>, inline styles and no shorthand — because
/// Outlook on Windows renders mail through <i>Word</i>, which supports no flexbox, no grid, no
/// <c>background-image</c>, no <c>border-radius</c> on a cell and no external stylesheet. Every rule below is
/// either supported by the worst client in the set or decorative enough to lose silently. Specifically:</para>
/// <list type="bullet">
///   <item><b>No SVG anywhere, and no remote image either.</b> Gmail, Outlook.com and Yahoo strip
///   <c>&lt;img src="…svg"&gt;</c> outright — which is why <c>apexa-email-lockup.png</c> exists beside the
///   committed lockup — and the PNG is attached to the message rather than linked, for the three reasons
///   <see cref="LogoContentId"/> records.</item>
///   <item><b>Every image has a styled <c>alt</c>, and nothing structural depends on one loading.</b> The
///   lockup's <c>alt</c> is « APEXA » in brand colour at the logo's own size, so a client that refuses the
///   attachment still shows a header. ⚠️ It carries <c>max-width</c> and not a fixed <c>width</c>: a
///   140 px box crops the word, which is how a fallback becomes an empty rectangle.</item>
///   <item><b>The button is a table, not a styled anchor.</b> Outlook refuses padding on an inline
///   <c>&lt;a&gt;</c>, so a padded anchor arrives as bare blue text on the one client a Tunisian practice is
///   most likely to be running.</item>
///   <item><b>The button is a flat fill, never the brand gradient.</b> <c>site/src/css/tokens.css</c> measures
///   white on <c>#2FC6E0</c> at <b>2.0:1</b> — a gradient running to the cyan end puts the label below legible
///   at exactly the point the reader is meant to click. The gradient is kept for the decorative hairline, where
///   contrast does not apply, and it carries a <c>bgcolor</c> twin so Outlook shows the flat blue instead of
///   nothing.</item>
///   <item><b>A <c>&lt;style&gt;</c> block narrows the padding on a phone, and the layout must be correct
///   without it.</b> Several clients drop the block entirely, so the base padding is one that already works at
///   320 px rather than one the media query rescues.</item>
/// </list>
///
/// <para>⚠️ <b>Every interpolated value goes through <see cref="Escape"/>.</b> The greeting carries
/// <c>User.FullName</c> and the panel carries an e-mail address, both typed by a stranger at an anonymous signup
/// form. This is the one file that encodes them, and a new row type added below without <c>Escape</c> is markup
/// injection into a message the recipient has every reason to trust.</para>
/// </summary>
public static class EmailLayout
{
    // ── The palette, as hex ────────────────────────────────────────────────────────────────────────────────────
    // ⚠️ Hex literals, and they cannot be anything else: `globals.css`'s tokens are `oklch()`, which no mail
    // client supports — Outlook would drop the declaration and paint the client default. These are the sRGB
    // conversions of the interface tokens, plus the logo's own two stops from `generate-icons.mjs`.
    private const string BrandDeep = "#1B54CE";   // BRAND_STOPS[0] — the fill that holds white text at 6.4:1
    private const string BrandCyan = "#2FC6E0";   // BRAND_STOPS[2] — decorative only, 2.0:1 on white
    private const string Ink = "#0A1B33";         // the lockup's own ink
    private const string InkMuted = "#5A6B80";
    private const string PageGround = "#F1F6FA";
    private const string Card = "#FFFFFF";
    private const string Panel = "#F5F9FC";
    private const string Hairline = "#E1EAF2";

    /// <summary>
    /// The one media query. Several clients drop a <c>&lt;style&gt;</c> block outright, so this only ever
    /// <i>narrows</i> padding a phone would otherwise be fine with — the base layout is correct at 320 px
    /// without it. Kept out of the interpolated markup because CSS braces and <c>$"…"</c> interpolation
    /// cannot share a string.
    /// </summary>
    private const string PhoneOverrides = """
        @media only screen and (max-width:520px) {
          .px { padding-left:20px !important; padding-right:20px !important; }
          .h1 { font-size:24px !important; }
        }
        """;

    private const string Font =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif";

    /// <summary>Where the practice can write back. The footer's only claim, so it must stay true.</summary>
    private const string SupportAddress = "contact@apexa.tn";

    /// <summary>
    /// The <c>Content-ID</c> the HTML references and the sender attaches the lockup under, so the image
    /// <b>travels inside the message</b>.
    ///
    /// <para>⚠️ <b>It was a URL on the deployment's own web origin, and that was wrong twice over.</b>
    /// Measured on the live deployment 2026-09-14, after a real signup: the file answered <c>307</c> at
    /// <c>/apexa-email-lockup.png</c> while <c>/icon-192.png</c> answered <c>200</c> — the asset is in
    /// <c>web/</c>, so it ships only when the <i>web</i> image is rebuilt, and an e-mail whose logo depends on
    /// another service's deploy cadence is broken by default on the day it is written. Worse, the web sends
    /// <c>Cross-Origin-Resource-Policy: same-site</c>, so a mail client fetching it <i>directly</i> (Apple Mail,
    /// Outlook desktop) is refused even once the file is there. And a remote image is blocked by default for
    /// unknown senders in Outlook and Gmail regardless.</para>
    ///
    /// <para>A <c>LinkedResource</c> has none of those failure modes: no network, no deploy coupling, nothing to
    /// block, and it works on the offline-LAN install where there is no route to anything. It costs ~7 KB per
    /// message.</para>
    /// </summary>
    public const string LogoContentId = "apexa-lockup";

    /// <summary>
    /// The lockup's bytes, from this assembly. Read once — a transactional send is not a hot path, but the
    /// stream is, and re-reading it per message would be a manifest lookup per e-mail for no reason.
    /// </summary>
    public static ReadOnlyMemory<byte> LogoPng => LazyLogo.Value;

    private static readonly Lazy<byte[]> LazyLogo = new(() =>
    {
        // The name is the default `<RootNamespace>.<folder path>.<file>` the SDK assigns an EmbeddedResource.
        const string resource = "ClinicManagement.Application.Common.Email.apexa-email-lockup.png";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"The e-mail lockup is not embedded in this assembly under '{resource}'. It is declared as an "
                + "EmbeddedResource in ClinicManagement.Application.csproj and regenerated by "
                + "web/scripts/generate-icons.mjs.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    });

    /// <summary>
    /// The HTML body. The lockup is referenced as <c>cid:</c> — see <see cref="LogoContentId"/> — so the
    /// sender <b>must</b> attach <see cref="LogoPng"/> as a linked resource or the header renders as a broken
    /// image.
    /// </summary>
    /// <param name="content">What to say.</param>
    public static string Html(EmailContent content)
    {
        var sb = new StringBuilder(4096);

        sb.Append(
            $"""
            <!DOCTYPE html>
            <html lang="fr"><head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="x-ua-compatible" content="ie=edge">
            <!-- Declared light-only on purpose: a client that auto-inverts turns the panel dark and leaves the
                 ink dark with it, which is the one failure mode that makes a verification link unreadable. -->
            <meta name="color-scheme" content="light">
            <meta name="supported-color-schemes" content="light">
            <title>{Escape(content.Title)}</title>
            <style>{PhoneOverrides}</style>
            </head>
            <body style="margin:0;padding:0;width:100%;background-color:{PageGround};">
            <div style="display:none;font-size:1px;color:{PageGround};line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;">{Escape(content.Preheader)}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:{PageGround};">
            <tr><td align="center" style="padding:24px 12px 36px 12px;">
            <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:600px;background-color:{Card};border:1px solid {Hairline};border-radius:14px;">

            """);

        // ── Header: the lockup, the heading, then the gradient rule ────────────────────────────────────────────
        // The lockup sits on the WHITE card and not on a coloured band: the mark is itself a blue→cyan plate, so
        // on a brand band it vanishes, and Outlook — which renders no CSS gradient — would show it on flat blue.
        sb.Append(
            $"""
            <tr><td class="px" style="padding:30px 32px 0 32px;">
              <img src="cid:{LogoContentId}" width="140" height="42" alt="APEXA"
                   style="display:block;height:42px;max-width:140px;border:0;outline:none;text-decoration:none;font-family:{Font};font-size:22px;font-weight:700;letter-spacing:-0.01em;color:{BrandDeep};">
            </td></tr>
            <tr><td class="px" style="padding:20px 32px 0 32px;">
              <h1 class="h1" style="margin:0;font-family:{Font};font-size:28px;line-height:1.22;font-weight:700;color:{Ink};">{Escape(content.Title)}</h1>
            </td></tr>
            <tr><td class="px" style="padding:20px 32px 0 32px;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
                <td height="3" bgcolor="{BrandDeep}" style="height:3px;font-size:0;line-height:0;mso-line-height-rule:exactly;background-color:{BrandDeep};background-image:linear-gradient(90deg,{BrandDeep} 0%,{BrandCyan} 100%);border-radius:2px;">&#8203;</td>
              </tr></table>
            </td></tr>

            """);

        if (!string.IsNullOrWhiteSpace(content.Greeting))
        {
            sb.Append(Paragraph(content.Greeting, top: 26, weight: "600"));
        }

        foreach (var paragraph in content.Intro)
        {
            sb.Append(Paragraph(paragraph, top: 14));
        }

        if (content.Details.Count > 0)
        {
            sb.Append(DetailPanel(content.Details));
        }

        if (content.Action is { } action)
        {
            sb.Append(Button(action));
        }

        if (content.Steps.Count > 0)
        {
            sb.Append(StepList(content.StepsTitle, content.Steps));
        }

        foreach (var paragraph in content.Outro)
        {
            sb.Append(Paragraph(paragraph, top: 18));
        }

        if (!string.IsNullOrWhiteSpace(content.Note))
        {
            sb.Append(
                $"""
                <tr><td class="px" style="padding:22px 32px 0 32px;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
                    <td height="1" bgcolor="{Hairline}" style="height:1px;font-size:0;line-height:0;mso-line-height-rule:exactly;background-color:{Hairline};">&#8203;</td>
                  </tr></table>
                  <p style="margin:16px 0 0 0;font-family:{Font};font-size:13px;line-height:1.55;color:{InkMuted};">{Escape(content.Note)}</p>
                </td></tr>

                """);
        }

        // ── Footer ────────────────────────────────────────────────────────────────────────────────────────────
        // Inside the card rather than under it: a footer on the page ground is the block a client's "trim quoted
        // text" collapses, and this one carries the support address.
        sb.Append(
            $"""
            <tr><td class="px" style="padding:28px 32px 30px 32px;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
                <td height="1" bgcolor="{Hairline}" style="height:1px;font-size:0;line-height:0;mso-line-height-rule:exactly;background-color:{Hairline};">&#8203;</td>
              </tr></table>
              <p style="margin:16px 0 0 0;font-family:{Font};font-size:13px;line-height:1.6;color:{InkMuted};">
                <strong style="color:{Ink};">APEXA</strong> &#183; logiciel de gestion pour cabinet dentaire<br>
                Une question&#160;? Répondez à ce message ou écrivez à
                <a href="mailto:{SupportAddress}" style="color:{BrandDeep};text-decoration:underline;">{SupportAddress}</a>.
              </p>
              <p style="margin:10px 0 0 0;font-family:{Font};font-size:12px;line-height:1.6;color:{InkMuted};">
                Ce message est envoyé automatiquement&#160;: il ne contient jamais votre mot de passe, et APEXA ne
                vous le demandera jamais par e-mail.
              </p>
            </td></tr>

            </table>
            </td></tr>
            </table>
            </body></html>
            """);

        return sb.ToString();
    }

    /// <summary>
    /// The <c>text/plain</c> alternate, from the same object.
    ///
    /// <para>⚠️ <b>Not optional.</b> Sent as HTML alone, a client with HTML refused — and a spam filter scoring
    /// the message — sees a single part it will not render, so the reader gets either raw markup or nothing. This
    /// is also the copy that has to carry the verification URL as text, since a text client has no button.</para>
    /// </summary>
    public static string PlainText(EmailContent content)
    {
        var sb = new StringBuilder(1024);

        sb.Append(content.Title).Append('\n');
        sb.Append(new string('=', Math.Min(content.Title.Length, 60))).Append("\n\n");

        if (!string.IsNullOrWhiteSpace(content.Greeting))
        {
            sb.Append(content.Greeting).Append("\n\n");
        }

        foreach (var paragraph in content.Intro)
        {
            sb.Append(paragraph).Append("\n\n");
        }

        foreach (var detail in content.Details)
        {
            sb.Append(detail.Label).Append(" : ").Append(detail.Value).Append('\n');
        }

        if (content.Details.Count > 0)
        {
            sb.Append('\n');
        }

        // The URL on its own line, unwrapped and unpunctuated: a mail client auto-links a bare URL and swallows a
        // trailing full stop into the href, which is how a verification link arrives broken.
        if (content.Action is { } action)
        {
            sb.Append(action.Label).Append(" :\n").Append(action.Url).Append("\n\n");
        }

        if (content.Steps.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(content.StepsTitle))
            {
                sb.Append(content.StepsTitle).Append("\n\n");
            }

            for (var i = 0; i < content.Steps.Count; i++)
            {
                sb.Append(i + 1).Append(". ").Append(content.Steps[i].Title).Append('\n');
                if (!string.IsNullOrWhiteSpace(content.Steps[i].Detail))
                {
                    sb.Append("   ").Append(content.Steps[i].Detail).Append('\n');
                }
            }

            sb.Append('\n');
        }

        foreach (var paragraph in content.Outro)
        {
            sb.Append(paragraph).Append("\n\n");
        }

        if (!string.IsNullOrWhiteSpace(content.Note))
        {
            sb.Append(content.Note).Append("\n\n");
        }

        sb.Append("--\nAPEXA — logiciel de gestion pour cabinet dentaire\n");
        sb.Append("Une question ? Répondez à ce message ou écrivez à ").Append(SupportAddress).Append(".\n");
        sb.Append(
            "Ce message est envoyé automatiquement : il ne contient jamais votre mot de passe, et APEXA ne vous "
            + "le demandera jamais par e-mail.\n");

        return sb.ToString();
    }

    private static string Paragraph(string text, int top, string weight = "400") =>
        $"""
        <tr><td class="px" style="padding:{top}px 32px 0 32px;">
          <p style="margin:0;font-family:{Font};font-size:15px;line-height:1.65;font-weight:{weight};color:{Ink};">{Escape(text)}</p>
        </td></tr>

        """;

    /// <summary>
    /// The facts panel.
    ///
    /// <para>⚠️ <c>word-break:break-all</c> on a link value, not <c>break-word</c>: a verification URL is one
    /// unbroken 90-character token, so a client that will not break inside a word widens the table past the
    /// viewport and the message scrolls sideways on a phone.</para>
    /// </summary>
    private static string DetailPanel(IReadOnlyList<EmailDetail> details)
    {
        var rows = new StringBuilder();

        for (var i = 0; i < details.Count; i++)
        {
            var detail = details[i];
            var value = detail.IsLink
                ? $"""<a href="{Escape(detail.Value)}" style="color:{BrandDeep};text-decoration:underline;word-break:break-all;">{Escape(detail.Value)}</a>"""
                : Escape(detail.Value);

            if (i > 0)
            {
                rows.Append(
                    $"""
                    <tr><td style="padding:0 18px;">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
                        <td height="1" bgcolor="{Hairline}" style="height:1px;font-size:0;line-height:0;mso-line-height-rule:exactly;background-color:{Hairline};">&#8203;</td>
                      </tr></table>
                    </td></tr>

                    """);
            }

            rows.Append(
                $"""
                <tr><td style="padding:14px 18px;">
                  <p style="margin:0 0 3px 0;font-family:{Font};font-size:11px;font-weight:700;letter-spacing:0.06em;text-transform:uppercase;color:{InkMuted};">{Escape(detail.Label)}</p>
                  <p style="margin:0;font-family:{Font};font-size:15px;line-height:1.5;color:{Ink};word-break:break-word;">{value}</p>
                </td></tr>

                """);
        }

        return $"""
            <tr><td class="px" style="padding:24px 32px 0 32px;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:{Panel};border:1px solid {Hairline};border-radius:10px;">
            {rows}  </table>
            </td></tr>

            """;
    }

    /// <summary>
    /// The one button. A table with a <c>bgcolor</c> cell and a padded <c>inline-block</c> anchor — the shape
    /// Outlook actually paints.
    ///
    /// <para>⚠️ <c>min-height:44px</c> via the padding, not by hoping: this is tapped on a phone, and the device
    /// contract this product is held to (<c>.claude/rules/frontend-web.md</c>) puts the floor at 44 px on a
    /// coarse pointer. 15 px of vertical padding on a 16 px line clears it.</para>
    /// </summary>
    private static string Button(EmailAction action) =>
        $"""
        <tr><td class="px" style="padding:26px 32px 0 32px;">
          <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
            <td align="center" bgcolor="{BrandDeep}" style="border-radius:10px;background-color:{BrandDeep};">
              <a href="{Escape(action.Url)}" style="display:inline-block;padding:15px 30px;font-family:{Font};font-size:16px;line-height:1.1;font-weight:600;color:#FFFFFF;text-decoration:none;border-radius:10px;">{Escape(action.Label)}</a>
            </td>
          </tr></table>
        </td></tr>

        """;

    /// <summary>
    /// The numbered walkthrough.
    ///
    /// <para>⚠️ A table and not an <c>&lt;ol&gt;</c>: Outlook and Gmail disagree about a list's indent by about
    /// 40 px, so the markers land outside the card on one of them. The number is a fixed-width cell, which also
    /// keeps a two-line step's text aligned under itself.</para>
    /// </summary>
    private static string StepList(string? title, IReadOnlyList<EmailStep> steps)
    {
        var rows = new StringBuilder();

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var detail = string.IsNullOrWhiteSpace(step.Detail)
                ? string.Empty
                : $"""<p style="margin:3px 0 0 0;font-family:{Font};font-size:14px;line-height:1.55;color:{InkMuted};">{Escape(step.Detail)}</p>""";

            rows.Append(
                $"""
                <tr>
                  <td width="30" valign="top" style="width:30px;padding:{(i == 0 ? 0 : 14)}px 0 0 0;">
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                      <td width="22" height="22" align="center" valign="middle" bgcolor="{BrandDeep}" style="width:22px;height:22px;border-radius:11px;background-color:{BrandDeep};font-family:{Font};font-size:12px;font-weight:700;line-height:22px;color:#FFFFFF;mso-line-height-rule:exactly;">{i + 1}</td>
                    </tr></table>
                  </td>
                  <td valign="top" style="padding:{(i == 0 ? 0 : 14)}px 0 0 0;">
                    <p style="margin:0;font-family:{Font};font-size:15px;line-height:1.5;font-weight:600;color:{Ink};">{Escape(step.Title)}</p>
                    {detail}
                  </td>
                </tr>

                """);
        }

        var heading = string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : $"""<p style="margin:0 0 14px 0;font-family:{Font};font-size:15px;line-height:1.5;font-weight:700;color:{Ink};">{Escape(title)}</p>""";

        return $"""
            <tr><td class="px" style="padding:26px 32px 0 32px;">
              {heading}
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
            {rows}  </table>
            </td></tr>

            """;
    }

    /// <summary>
    /// The one encoder. <see cref="WebUtility.HtmlEncode"/> and not a hand-rolled replace chain — it covers the
    /// quote characters an attribute needs, which is where <c>href</c> and <c>alt</c> live.
    /// </summary>
    private static string Escape(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
