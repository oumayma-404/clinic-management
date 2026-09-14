using System.Net.Mail;
using ClinicManagement.Application.Common.Email;
using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure;

/// <summary>
/// What actually goes on the wire, read out of a real <c>.eml</c> rather than asserted about the intention.
///
/// <para><b>Why this exists at all.</b> The brand lockup shipped as a URL on the deployment's own web origin
/// and every unit test was green: the asset lives in the <c>web/</c> image, so a real signup between two
/// deploys produced a message whose header was a broken rectangle — measured on the live deployment,
/// <c>/apexa-email-lockup.png</c> answering <b>307</b> while every other public asset answered 200. Nothing in
/// the suite could see it, because nothing in the suite looked at the assembled message.</para>
///
/// <para>It writes the message through <c>SmtpDeliveryMethod.SpecifiedPickupDirectory</c>, which needs no mail
/// server and no network: <see cref="SmtpClient"/> serialises the MIME to a file exactly as it would transmit
/// it.</para>
/// </summary>
public class TransactionalEmailMimeTests
{
    private static string RenderToEml()
    {
        var content = new EmailContent
        {
            Title = "Vérifiez votre adresse",
            Preheader = "Un clic pour créer votre cabinet.",
            Greeting = "Bonjour Dr Benali,",
            Intro = ["Il reste une seule étape."],
            Details = [new EmailDetail("Lien", "https://app.example.tn/x#token=abc", IsLink: true)],
            Action = new EmailAction("Vérifier mon adresse", "https://app.example.tn/x#token=abc")
        };

        var dir = Directory.CreateTempSubdirectory("apexa-mime-").FullName;
        try
        {
            using var mail = SmtpTransactionalEmailSender.BuildMessage(
                "dr.benali@cabinet.tn", "Vérifiez votre adresse", content, "contact@apexa.tn", "APEXA");

            using var client = new SmtpClient
            {
                DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = dir
            };
            client.Send(mail);

            return File.ReadAllText(Directory.GetFiles(dir, "*.eml").Single());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Both parts travel, in the order that makes a graphical client pick the HTML and a text-only client the
    /// prose. Sent as HTML alone, a reader with HTML refused gets raw tags or nothing.
    /// </summary>
    [Fact]
    public void The_message_carries_both_a_text_and_an_html_part()
    {
        var eml = RenderToEml();

        Assert.Contains("multipart/alternative", eml);
        Assert.Contains("text/plain", eml);
        Assert.Contains("text/html", eml);

        // Ordering: the plain part must appear before the HTML one, or every client prefers the plain text.
        Assert.True(eml.IndexOf("text/plain", StringComparison.Ordinal)
                    < eml.IndexOf("text/html", StringComparison.Ordinal));
    }

    /// <summary>
    /// ⚠️ <b>The case the live defect would have failed.</b> The logo must be a part of the message, carrying
    /// the exact <c>Content-ID</c> the HTML names — and the message must contain no reference to a file on a
    /// web host.
    /// </summary>
    [Fact]
    public void The_lockup_travels_inside_the_message_and_not_as_a_url()
    {
        var eml = RenderToEml();

        Assert.Contains("multipart/related", eml);
        Assert.Contains("image/png", eml);
        Assert.Contains($"Content-ID: <{EmailLayout.LogoContentId}>", eml);

        // The asset name must not appear anywhere: its presence means somebody restored the URL.
        Assert.DoesNotContain("apexa-email-lockup.png", eml);
    }

    /// <summary>
    /// A message under ~100 KB, so the embedded logo has not quietly become a full-resolution asset — and so
    /// Gmail does not clip it (it truncates around 102 KB and hides the footer behind « View entire message »).
    /// </summary>
    [Fact]
    public void The_message_stays_well_under_the_size_a_client_clips()
    {
        var eml = RenderToEml();

        Assert.InRange(eml.Length, 1_000, 100_000);
    }
}
