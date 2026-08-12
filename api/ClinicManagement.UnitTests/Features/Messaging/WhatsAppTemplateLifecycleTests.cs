using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Messaging;

/// <summary>
/// The template's life: submitted on the cabinet's behalf at connection (§ 33, AC-1.3), reconciled by the poll when
/// Meta's webhook never arrives (§ 35, FR-7a), and never typed by the practice (§ 38, AC-1.7).
///
/// <para><b>⚠️ The load-bearing case is <see cref="On_A_Deployment_That_Does_Not_Sell_Vendor_Messaging_No_Template_Is_Submitted"/></b>,
/// and it is the one nothing else in the suite can see. A stored template state is what makes
/// <c>OutboxMessagingGate</c> hold a cabinet's reminders — and on the other two deployment kinds neither writer that
/// could clear it exists (the webhook 404s, the daily pass is not registered). Submitting there would hold a working
/// cabinet's reminders for ever, which is the exact opposite of EC-16's « byte-for-byte unchanged ».</para>
/// </summary>
public class WhatsAppTemplateLifecycleTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime CheckedAt = new(2026, 8, 12, 9, 0, 0, DateTimeKind.Utc);

    // ---- § 33: the submission at connection ------------------------------------------------------

    private sealed class ConnectHarness
    {
        public Mock<IClinicReminderSettingsRepository> Settings { get; } = new();
        public Mock<IWhatsAppTemplateService> Templates { get; } = new();
        public ClinicReminderSettings? Saved { get; private set; }
        public ConnectClinicWhatsAppCommandHandler Handler { get; }

        public ConnectHarness(bool sellsVendorMessaging = true, WhatsAppTemplateState? submitted = null)
        {
            var admin = User.CreateLocalUser(ClinicId, "admin", "admin@clinic.com", "HASH", "Admin");

            var users = new Mock<IUserRepository>();
            users.Setup(r => r.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);

            var context = new Mock<IClinicContext>();
            context.Setup(c => c.GetUserId()).Returns(admin.Id);

            var protector = new Mock<IReminderSecretProtector>();
            protector.Setup(p => p.Protect(It.IsAny<string>())).Returns<string>(v => $"enc:{v}");

            var onboarding = new Mock<IWhatsAppOnboardingService>();
            onboarding.Setup(o => o.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("the-token");

            Templates
                .Setup(t => t.SubmitReminderTemplateAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(submitted);

            Settings.Setup(r => r.AddAsync(It.IsAny<ClinicReminderSettings>(), It.IsAny<CancellationToken>()))
                .Callback<ClinicReminderSettings, CancellationToken>((s, _) => Saved = s)
                .Returns(Task.CompletedTask);

            var availability = new Mock<IVendorMessagingAvailability>();
            availability.SetupGet(a => a.SellsVendorMessaging).Returns(sellsVendorMessaging);

            Handler = new ConnectClinicWhatsAppCommandHandler(
                Settings.Object, users.Object, context.Object, protector.Object, onboarding.Object,
                Templates.Object, availability.Object, Mock.Of<IUnitOfWork>());
        }

        public Task<Result<ReminderSettingsDto>> ConnectAsync() => Handler.Handle(
            new ConnectClinicWhatsAppCommand
            {
                Request = new ConnectWhatsAppRequest { Code = "code", WabaId = "WABA-1", PhoneNumberId = "PN-9" },
            },
            CancellationToken.None);
    }

    /// <summary>
    /// [AC-1.3] Connecting submits the French template on the cabinet's behalf and records what Meta granted —
    /// including its <b>name</b>, so the sender names the template that was actually submitted rather than whatever a
    /// per-install config key happens to say.
    /// </summary>
    [Fact]
    public async Task Connecting_Submits_The_Reminder_Template_And_Records_What_Meta_Granted()
    {
        var harness = new ConnectHarness(
            submitted: new WhatsAppTemplateState(WhatsAppTemplateStatus.PendingReview, "UTILITY", "TPL-77"));

        var result = await harness.ConnectAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(harness.Saved);
        Assert.Equal(WhatsAppTemplateStatus.PendingReview, harness.Saved!.WhatsAppTemplateStatus);
        Assert.Equal("UTILITY", harness.Saved.WhatsAppTemplateCategory);
        Assert.Equal("TPL-77", harness.Saved.WhatsAppTemplateId);
        Assert.Equal(WhatsAppReminderTemplate.Name, harness.Saved.WhatsAppTemplateName);
        Assert.Equal(WhatsAppReminderTemplate.Language, harness.Saved.WhatsAppTemplateLanguage);
    }

    /// <summary>
    /// [AC-1.3] A submission Meta did not answer must not undo a connection it already accepted: the cabinet is
    /// connected and left « en attente de validation » for the poll, rather than the whole connect failing.
    /// </summary>
    [Fact]
    public async Task A_Failed_Submission_Still_Leaves_The_Cabinet_Connected()
    {
        var harness = new ConnectHarness(submitted: null);

        var result = await harness.ConnectAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(WhatsAppConnectionStatus.Connected, harness.Saved!.WhatsAppConnectionStatus);
        Assert.Equal(WhatsAppTemplateStatus.PendingReview, harness.Saved.WhatsAppTemplateStatus);
    }

    /// <summary>[EC-16] See the ⚠️ on this class — the case nothing else can see.</summary>
    [Fact]
    public async Task On_A_Deployment_That_Does_Not_Sell_Vendor_Messaging_No_Template_Is_Submitted()
    {
        var harness = new ConnectHarness(sellsVendorMessaging: false);

        var result = await harness.ConnectAsync();

        Assert.True(result.IsSuccess);
        harness.Templates.Verify(
            t => t.SubmitReminderTemplateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Null, not NotSubmitted: the gate reads null as « we do not track a template here » and lets reminders out.
        Assert.Null(harness.Saved!.WhatsAppTemplateStatus);
    }

    // ---- The entity's own rules -------------------------------------------------------------------

    /// <summary>
    /// [FR-7a] A re-submission that could not be read keeps an <b>approved</b> template approved. Overwriting it with
    /// « en attente » because one Graph call timed out would hold a working cabinet's reminders until the next poll.
    /// </summary>
    [Fact]
    public void An_Unreadable_Resubmission_Does_Not_Downgrade_An_Approved_Template()
    {
        var settings = new ClinicReminderSettings(ClinicId);
        settings.ApplyWhatsAppConnection("WABA-1", "PN-9");
        settings.SetWhatsAppTemplateState(WhatsAppTemplateStatus.Approved, "UTILITY", "TPL-1", CheckedAt);

        settings.ApplySubmittedReminderTemplate(
            WhatsAppReminderTemplate.Name, WhatsAppReminderTemplate.Language,
            status: null, category: null, templateId: null, submittedAtUtc: CheckedAt);

        Assert.Equal(WhatsAppTemplateStatus.Approved, settings.WhatsAppTemplateStatus);
    }

    /// <summary>
    /// [FR-7b] A status notification carries no category, and re-confirming a status must not erase the one the
    /// submission recorded — it is the only thing the <c>messaging-report</c> finding reads.
    /// </summary>
    [Fact]
    public void Confirming_A_Status_Preserves_The_Stored_Category()
    {
        var settings = new ClinicReminderSettings(ClinicId);
        settings.SetWhatsAppTemplateState(WhatsAppTemplateStatus.PendingReview, "MARKETING", "TPL-1", CheckedAt);

        settings.SetWhatsAppTemplateState(WhatsAppTemplateStatus.Approved, category: null, templateId: null, CheckedAt);

        Assert.Equal("MARKETING", settings.WhatsAppTemplateCategory);
        Assert.Equal("TPL-1", settings.WhatsAppTemplateId);
    }

    /// <summary>
    /// [FR-7a] Disconnecting clears the template state. It describes a template inside the WABA being disconnected, so
    /// keeping it would leave the cabinet reading « modèle refusé » about a business account it no longer has.
    /// </summary>
    [Fact]
    public void Disconnecting_Clears_The_Template_State()
    {
        var settings = new ClinicReminderSettings(ClinicId);
        settings.ApplyWhatsAppConnection("WABA-1", "PN-9");
        settings.SetWhatsAppTemplateState(WhatsAppTemplateStatus.Rejected, "UTILITY", "TPL-1", CheckedAt);

        settings.ClearWhatsAppConnection();

        Assert.Null(settings.WhatsAppTemplateStatus);
        Assert.Null(settings.WhatsAppTemplateCategory);
        Assert.Null(settings.WhatsAppTemplateId);
        Assert.Null(settings.WhatsAppTemplateStatusCheckedAtUtc);
    }

    // ---- § 35: the reconciling poll ---------------------------------------------------------------

    private sealed class PollHarness
    {
        public Mock<IClinicReminderSettingsRepository> Settings { get; } = new();
        public Mock<IWhatsAppTemplateService> Templates { get; } = new();
        public ClinicReminderSettings Candidate { get; }
        public MessagingAllowanceJob Job { get; }

        public PollHarness(WhatsAppTemplateState? readBack, bool decryptable = true)
        {
            Candidate = new ClinicReminderSettings(ClinicId);
            Candidate.ApplyWhatsAppConnection("WABA-1", "PN-9");
            Candidate.SetWhatsAppAccessTokenEncrypted("enc-token");
            Candidate.SetWhatsAppTemplateState(WhatsAppTemplateStatus.PendingReview, "UTILITY", "TPL-1", CheckedAt);

            Settings.Setup(r => r.GetAwaitingTemplateReviewAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Candidate });

            Templates
                .Setup(t => t.ReadReminderTemplateAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(readBack);

            var protector = new Mock<IReminderSecretProtector>();
            if (decryptable)
            {
                protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns("the-token");
            }
            else
            {
                protector.Setup(p => p.Unprotect(It.IsAny<string>())).Throws(new InvalidOperationException("key ring"));
            }

            var clinics = new Mock<IClinicRepository>();
            clinics.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<Clinic>());

            var availability = new Mock<IVendorMessagingAvailability>();
            availability.SetupGet(a => a.SellsVendorMessaging).Returns(true);

            Job = new MessagingAllowanceJob(
                clinics.Object, Mock.Of<IMessagingAllowanceRepository>(),
                Mock.Of<IClinicSubscriptionRepository>(), Mock.Of<INotificationGenerator>(),
                availability.Object, Mock.Of<ISubscriptionPolicy>(),
                Settings.Object, Templates.Object, protector.Object, Mock.Of<IUnitOfWork>(),
                Mock.Of<IAuditActorProvider>(), Mock.Of<ITenantScope>(),
                NullLogger<MessagingAllowanceJob>.Instance);
        }
    }

    /// <summary>
    /// [FR-7a] The poll is what makes AC-1.5 true <b>at all</b> for a webhook Meta never delivered: an approval read
    /// back moves the cabinet's state, so its held reminders are released on the next dispatch tick.
    /// </summary>
    [Fact]
    public async Task The_Poll_Records_An_Approval_The_Webhook_Never_Delivered()
    {
        var harness = new PollHarness(
            new WhatsAppTemplateState(WhatsAppTemplateStatus.Approved, "UTILITY", "TPL-1"));

        await harness.Job.ReviewMessagingAllowances(new DateTime(2026, 8, 12));

        Assert.Equal(WhatsAppTemplateStatus.Approved, harness.Candidate.WhatsAppTemplateStatus);
    }

    /// <summary>
    /// [FR-7b] A reclassification is recorded verbatim. It changes no unit — one message is one unit whatever Meta
    /// charges — and it holds no reminder; it is the vendor's cost that moved, so it surfaces on the vendor's
    /// surfaces alone.
    /// </summary>
    [Fact]
    public async Task The_Poll_Records_A_Category_Meta_Changed()
    {
        var harness = new PollHarness(
            new WhatsAppTemplateState(WhatsAppTemplateStatus.Approved, "MARKETING", "TPL-1"));

        await harness.Job.ReviewMessagingAllowances(new DateTime(2026, 8, 12));

        Assert.Equal("MARKETING", harness.Candidate.WhatsAppTemplateCategory);
        Assert.Equal(WhatsAppTemplateStatus.Approved, harness.Candidate.WhatsAppTemplateStatus);
    }

    /// <summary>
    /// [FR-7a] A read that answered nothing — the call failed, or the WABA holds no such template — leaves the stored
    /// state exactly as it is. Asserting something about a template we could not see is the one thing worse than not
    /// knowing.
    /// </summary>
    [Fact]
    public async Task A_Read_That_Answered_Nothing_Changes_Nothing()
    {
        var harness = new PollHarness(readBack: null);

        await harness.Job.ReviewMessagingAllowances(new DateTime(2026, 8, 12));

        Assert.Equal(WhatsAppTemplateStatus.PendingReview, harness.Candidate.WhatsAppTemplateStatus);
    }

    /// <summary>
    /// [FR-7a] A token that no longer decrypts (a rotated key ring) is « we cannot ask », not « the template is
    /// gone » — and it must not take the whole pass down with it.
    /// </summary>
    [Fact]
    public async Task An_Undecryptable_Token_Leaves_The_State_Alone_Without_Throwing()
    {
        var harness = new PollHarness(
            new WhatsAppTemplateState(WhatsAppTemplateStatus.Approved, "UTILITY", "TPL-1"),
            decryptable: false);

        await harness.Job.ReviewMessagingAllowances(new DateTime(2026, 8, 12));

        Assert.Equal(WhatsAppTemplateStatus.PendingReview, harness.Candidate.WhatsAppTemplateStatus);
    }

    /// <summary>
    /// [FR-7a] The candidate set and <c>IsTerminal</c> are derived from <b>one</b> array, so « which cabinets does the
    /// poll cover? » has a single answer — and the repository's predicate is SQL, which a <c>switch</c> could not be.
    /// ⚠️ <c>Paused</c> is deliberately non-terminal: Meta un-pauses a recovered template with no guaranteed webhook.
    /// </summary>
    [Fact]
    public void The_Polls_Candidate_Set_And_IsTerminal_Are_One_Answer()
    {
        Assert.Contains(WhatsAppTemplateStatus.Paused, WhatsAppTemplateStatuses.AwaitingMeta);
        Assert.Contains(WhatsAppTemplateStatus.PendingReview, WhatsAppTemplateStatuses.AwaitingMeta);
        Assert.Contains(WhatsAppTemplateStatus.NotSubmitted, WhatsAppTemplateStatuses.AwaitingMeta);

        foreach (var status in Enum.GetValues<WhatsAppTemplateStatus>())
        {
            Assert.Equal(
                !WhatsAppTemplateStatuses.AwaitingMeta.Contains(status),
                WhatsAppTemplateStatuses.IsTerminal(status));
        }
    }

    // ---- § 38 / AC-1.7: the manual fields are refused ---------------------------------------------

    private static UpdateClinicReminderSettingsCommandHandler VendorManagedHandler(
        Mock<IClinicReminderSettingsRepository> settings, User admin)
    {
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);

        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(admin.Id);

        var provider = new Mock<IReminderSettingsProvider>();
        provider.Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedReminderSettings { EnabledChannels = Array.Empty<NotificationType>() });

        var availability = new Mock<IVendorMessagingAvailability>();
        availability.SetupGet(a => a.SellsVendorMessaging).Returns(true);

        return new UpdateClinicReminderSettingsCommandHandler(
            settings.Object, users.Object, context.Object, Mock.Of<IReminderSecretProtector>(),
            provider.Object, Mock.Of<IOutboundEndpointPolicy>(), availability.Object, Mock.Of<IUnitOfWork>());
    }

    /// <summary>
    /// [AC-1.7] Where the vendor provisions WhatsApp, a request carrying one of its credentials is <b>refused</b> with
    /// a code — the server-side half of « the fields are not offered ».
    /// </summary>
    [Theory]
    [InlineData("https://graph.facebook.com/v21.0", null, null)]
    [InlineData(null, "123456789", null)]
    [InlineData(null, null, "a-token")]
    public async Task A_Manual_WhatsApp_Credential_Is_Refused_Where_The_Vendor_Provisions_It(
        string? apiUrl, string? phoneNumberId, string? accessToken)
    {
        var admin = User.CreateLocalUser(ClinicId, "admin", "admin@clinic.com", "HASH", "Admin");
        var settings = new Mock<IClinicReminderSettingsRepository>();
        var handler = VendorManagedHandler(settings, admin);

        var result = await handler.Handle(
            new UpdateClinicReminderSettingsCommand
            {
                Settings = new UpdateReminderSettingsRequest
                {
                    WhatsAppApiUrl = apiUrl,
                    WhatsAppPhoneNumberId = phoneNumberId,
                    WhatsAppAccessToken = accessToken,
                },
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MessagingRefusals.ManualWhatsAppCode, result.Code);
        settings.Verify(r => r.AddAsync(It.IsAny<ClinicReminderSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// [AC-1.7] <b>And an ordinary save keeps the connection.</b> The screen no longer renders those fields, so it
    /// posts nulls — and <c>ApplyNonSecretSettings</c> replaces every field it is given. Without carrying the stored
    /// values across, saving an unrelated SMS setting would erase the phone-number id « Connecter WhatsApp » wrote and
    /// silently un-configure the channel.
    /// </summary>
    [Fact]
    public async Task An_Ordinary_Save_Does_Not_Erase_A_Vendor_Provisioned_Connection()
    {
        var admin = User.CreateLocalUser(ClinicId, "admin", "admin@clinic.com", "HASH", "Admin");

        var stored = new ClinicReminderSettings(ClinicId);
        stored.ApplyWhatsAppConnection("WABA-1", "PN-9");
        stored.ApplySubmittedReminderTemplate(
            WhatsAppReminderTemplate.Name, WhatsAppReminderTemplate.Language,
            WhatsAppTemplateStatus.Approved, "UTILITY", "TPL-1", CheckedAt);

        var settings = new Mock<IClinicReminderSettingsRepository>();
        settings.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var result = await VendorManagedHandler(settings, admin).Handle(
            new UpdateClinicReminderSettingsCommand
            {
                // Exactly what the screen posts: an SMS change, and nulls for the WhatsApp identity it no longer shows.
                Settings = new UpdateReminderSettingsRequest { SmsSenderId = "MaClinique" },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("PN-9", stored.WhatsAppPhoneNumberId);
        Assert.Equal(WhatsAppReminderTemplate.Name, stored.WhatsAppTemplateName);
        Assert.True(result.Value!.WhatsAppVendorManaged);
    }
}
