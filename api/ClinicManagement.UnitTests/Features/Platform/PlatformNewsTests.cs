using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The news e-mail's pure pieces: what the form may hold, who it reaches, what it says, and the broadcast aggregate.
/// </summary>
public class PlatformNewsTests
{
    private static readonly Guid Essai = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Payant = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid SansAdmin = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private static PlatformNewsItemRequest Row(string? text, string? emoji = "🚀") => new(emoji, text);

    private static PlatformNewsDraft Draft(params string[] rows) =>
        new("Les nouveautés Apexa ✨", rows.Select(r => new PlatformNewsItemInput("🚀", r)).ToList());

    [Fact]
    public void Normalize_Drops_Blank_Rows_Collapses_Whitespace_And_Defaults_A_Missing_Emoji()
    {
        var result = PlatformNewsRules.Normalize(
            "  Les   nouveautés\r\nApexa ",
            [Row("   "), Row("  Agenda\n plus   rapide ", emoji: " "), Row("SMS", emoji: "📱")]);

        Assert.True(result.IsSuccess);
        Assert.Equal("Les nouveautés Apexa", result.Value!.Subject);
        Assert.Equal(
            new[] { ("✨", "Agenda plus rapide"), ("📱", "SMS") },
            result.Value.Items.Select(i => (i.Emoji, i.Text)).ToArray());
    }

    public static IEnumerable<object[]> RefusedForms() =>
    [
        ["  ", new[] { Row("x") }],
        ["Objet", new[] { Row("   "), Row(null) }],
        [new string('s', 121), new[] { Row("x") }],
        ["Objet", new[] { Row(new string('x', 301)) }],
        ["Objet", Enumerable.Range(0, 13).Select(i => Row($"r{i}")).ToArray()],
        ["Objet", new[] { Row("x", emoji: new string('e', 17)) }],
    ];

    [Theory]
    [MemberData(nameof(RefusedForms))]
    public void Normalize_Refuses_An_Invalid_Form_With_The_Invalid_Code_And_A_French_Sentence(
        string subject, PlatformNewsItemRequest[] items)
    {
        var result = PlatformNewsRules.Normalize(subject, items);

        Assert.True(result.IsFailure);
        Assert.Equal(PlatformNewsRules.InvalidCode, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void The_Preview_Never_Refuses_And_Shows_Placeholders_For_An_Empty_Form()
    {
        var draft = PlatformNewsRules.ForPreview(" ", [Row(""), Row(new string('x', 400), emoji: null)]);

        Assert.Equal("Objet de l'e-mail", draft.Subject);
        Assert.Single(draft.Items);
        Assert.Equal(300, draft.Items[0].Text.Length);
        Assert.Equal(PlatformNewsRules.DefaultEmoji, draft.Items[0].Emoji);
        Assert.Contains("première nouveauté", PlatformNewsRules.ForPreview(null, null).Items[0].Text);
    }

    [Fact]
    public void Every_Admin_Of_An_Eligible_Cabinet_Is_A_Recipient_And_A_Cabinet_Without_One_Is_Counted_Apart()
    {
        var plan = PlatformNewsAudiencePlan.From(
        [
            new PlatformNewsAudienceClinic(Essai, "Cabinet Essai", [new("a@cab.tn", "Dr A")]),
            new PlatformNewsAudienceClinic(Payant, "Cabinet Payant", [new("b@cab.tn", "Dr B"), new("b2@cab.tn", null)]),
            new PlatformNewsAudienceClinic(SansAdmin, "Cabinet Sans Admin", []),
        ]);

        Assert.Equal(new[] { "a@cab.tn", "b@cab.tn", "b2@cab.tn" }, plan.Recipients.Select(r => r.Email));
        Assert.Equal(2, plan.Clinics);
        Assert.Equal(1, plan.ClinicsWithoutAdminEmail);
        Assert.Equal("Cabinet Payant", plan.Recipients[2].ClinicName);
    }

    [Fact]
    public void One_Address_Held_By_Two_Accounts_Receives_One_Email_Whatever_Its_Case()
    {
        var plan = PlatformNewsAudiencePlan.From(
        [
            new PlatformNewsAudienceClinic(Essai, "Cabinet Essai", [new("Dr.Sassi@Cab.tn", "Dr Sassi")]),
            new PlatformNewsAudienceClinic(Payant, "Cabinet Payant", [new("dr.sassi@cab.tn", "Dr Sassi")]),
        ]);

        Assert.Single(plan.Recipients);
        Assert.Equal(1, plan.Clinics);
        Assert.Equal(0, plan.ClinicsWithoutAdminEmail);
    }

    [Fact]
    public void The_Email_Frames_The_Rows_Between_The_Fun_Opening_And_The_Warm_Closing()
    {
        var content = PlatformNewsEmail.Compose(Draft("Agenda plus rapide.", "SMS la veille."), "Dr Sassi", "Cabinet Sassi", "https://app.apexa.tn/login");

        Assert.Equal("Happy Apexing ! 🦷✨", content.Kicker);
        Assert.Equal("Les nouveautés Apexa ✨", content.Title);
        Assert.Equal("Bonjour Dr Sassi,", content.Greeting);
        Assert.Equal(new[] { "Voici les nouveautés d'Apexa :" }, content.Intro);
        Assert.Equal(new[] { "Agenda plus rapide.", "SMS la veille." }, content.Bullets.Select(b => b.Text));
        Assert.Equal(new[] { "Apexa, toujours à vos côtés 💙", "L'équipe Apexa" }, content.Outro);
        Assert.Equal("Vous recevez cet e-mail en tant qu'administrateur du cabinet Cabinet Sassi.", content.Note);
        Assert.Equal("https://app.apexa.tn/login", content.Action?.Url);
    }

    [Fact]
    public void An_Install_With_No_Public_Address_Sends_No_Button()
    {
        var content = PlatformNewsEmail.Compose(Draft("x"), null, "Cabinet", appUrl: null);

        Assert.Null(content.Action);
        Assert.Equal("Bonjour,", content.Greeting);
    }

    [Theory]
    [InlineData(true, "https://app.apexa.tn/", "https://app.apexa.tn/login")]
    [InlineData(false, "", null)]
    public void The_Button_Leads_To_The_Sign_In_Page_Of_The_Configured_Address(bool configured, string baseUrl, string? expected)
    {
        var appUrl = new Mock<IPublicAppUrlProvider>();
        appUrl.SetupGet(a => a.IsConfigured).Returns(configured);
        appUrl.SetupGet(a => a.BaseUrl).Returns(baseUrl);

        Assert.Equal(expected, PlatformNewsEmail.AppLink(appUrl.Object));
    }

    [Fact]
    public void The_Preview_Is_The_Real_Html_With_The_Logo_Inlined_For_A_Browser()
    {
        var content = PlatformNewsEmail.Compose(Draft("x"), "Dr A", "Cabinet", null);

        var preview = PlatformNewsEmail.PreviewHtml(content);

        Assert.DoesNotContain("cid:", preview);
        Assert.Contains("data:image/png;base64," + Convert.ToBase64String(EmailLayout.LogoPng.Span), preview);
        Assert.Equal(EmailLayout.Html(content).Replace("cid:" + EmailLayout.LogoContentId, ""), preview.Replace("data:image/png;base64," + Convert.ToBase64String(EmailLayout.LogoPng.Span), ""));
    }

    private static PlatformNewsBroadcast Broadcast(params string[] emails) =>
        PlatformNewsBroadcast.Create(
            Guid.NewGuid(),
            "Objet",
            [new PlatformNewsItemInput("🚀", "Une ligne")],
            emails.Select(e => new PlatformNewsRecipientInput(Essai, "Cabinet Essai", e, null)),
            Guid.NewGuid(),
            "Oumayma",
            new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void A_Broadcast_Starts_With_Every_Recipient_Pending_And_Cannot_Complete_Until_None_Is()
    {
        var broadcast = Broadcast("a@cab.tn", "b@cab.tn");
        var now = new DateTime(2026, 11, 6, 10, 5, 0, DateTimeKind.Utc);

        Assert.Equal(2, broadcast.PendingRecipients().Count);
        Assert.Throws<InvalidOperationException>(() => broadcast.Complete(now));

        broadcast.RecordSent(broadcast.Recipients.First().Id, now);
        broadcast.RecordFailed(broadcast.Recipients.Last().Id, new string('e', 600), now);
        broadcast.Complete(now);

        Assert.Empty(broadcast.PendingRecipients());
        Assert.Equal(now, broadcast.CompletedAtUtc);
        Assert.Equal(PlatformNewsRecipient.ErrorMaxLength, broadcast.Recipients.Last().Error!.Length);
        Assert.Equal(PlatformNewsDeliveryStatus.Sent, broadcast.Recipients.First().Status);
    }

    [Fact]
    public void A_Recipient_Can_Be_Claimed_Once_And_A_Claimed_Send_Is_Not_Settled()
    {
        var broadcast = Broadcast("a@cab.tn");
        var recipient = broadcast.Recipients.Single();
        var now = new DateTime(2026, 11, 6, 10, 5, 0, DateTimeKind.Utc);

        broadcast.Claim(recipient.Id, now);

        Assert.Throws<InvalidOperationException>(() => broadcast.Claim(recipient.Id, now));
        Assert.False(broadcast.IsSettled);
        Assert.Throws<InvalidOperationException>(() => broadcast.Complete(now));
        Assert.Empty(broadcast.PendingRecipients());
    }

    [Fact]
    public void Only_Claims_Older_Than_The_Cutoff_Expire_And_They_Fail_Rather_Than_Return_To_Pending()
    {
        var broadcast = Broadcast("old@cab.tn", "new@cab.tn");
        var old = broadcast.Recipients.First();
        var fresh = broadcast.Recipients.Last();
        var cutoff = new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);
        broadcast.Claim(old.Id, cutoff.AddMinutes(-1));
        broadcast.Claim(fresh.Id, cutoff.AddMinutes(1));

        var expired = broadcast.ExpireStaleClaims(cutoff, cutoff.AddMinutes(2));

        Assert.Equal(1, expired);
        Assert.Equal(PlatformNewsDeliveryStatus.Failed, old.Status);
        Assert.Equal(PlatformNewsDeliveryStatus.Sending, fresh.Status);
    }

    [Fact]
    public void A_Broadcast_Refuses_No_Recipient_And_No_Row()
    {
        Assert.Throws<ArgumentException>(() => Broadcast());
        Assert.Throws<ArgumentException>(() => PlatformNewsBroadcast.Create(
            Guid.NewGuid(), "Objet", [], [new PlatformNewsRecipientInput(Essai, "C", "a@cab.tn", null)],
            Guid.NewGuid(), "O", new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void The_Rows_Read_Back_In_The_Order_They_Were_Written()
    {
        var broadcast = PlatformNewsBroadcast.Create(
            Guid.NewGuid(), "Objet",
            [new PlatformNewsItemInput("1️⃣", "un"), new PlatformNewsItemInput("2️⃣", "deux"), new PlatformNewsItemInput("3️⃣", "trois")],
            [new PlatformNewsRecipientInput(Essai, "C", "a@cab.tn", null)],
            Guid.NewGuid(), "O", new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new[] { "un", "deux", "trois" }, broadcast.OrderedItems().Select(i => i.Text));
    }
}
