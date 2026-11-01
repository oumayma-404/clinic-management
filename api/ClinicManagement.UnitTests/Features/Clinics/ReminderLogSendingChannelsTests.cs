using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Clinics.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// sms-rappels-tunisie-sms — the « Rappels » log says which channels can send, from the server's own resolution,
/// instead of the page guessing « nothing is configured » from an empty log (QA run 1, F1).
/// </summary>
public class ReminderLogSendingChannelsTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static ResolvedReminderSettings Sms(bool enabled, bool configured) => new()
    {
        EnabledChannels = enabled ? [NotificationType.SMS] : [],
        SmsApiUrl = configured ? "https://sms.example/send" : null,
        SmsSenderId = configured ? "APEXA" : null,
        SmsApiKey = configured ? "k" : null,
    };

    private static async Task<IReadOnlyList<string>> SendingChannels(ResolvedReminderSettings settings)
    {
        var user = User.CreateLocalUser(ClinicId, "secretary", "reception@cabinet.tn", "HASH", "Accueil");
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);

        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(r => r.GetClinicLogAsync(
                ClinicId, It.IsAny<NotificationStatus?>(), It.IsAny<NotificationType?>(), It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Notification>([], 1, 25, 0));
        notifications.Setup(r => r.GetClinicLogCountsAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReminderLogCounts(0, 0, 0, 0, 0, 0));

        var provider = new Mock<IReminderSettingsProvider>();
        provider.Setup(p => p.ResolveAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        var result = await new GetClinicReminderLogQueryHandler(
                notifications.Object, users.Object, context.Object, provider.Object)
            .Handle(new GetClinicReminderLogQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.SendingChannels;
    }

    [Fact]
    public async Task Sms_Switched_On_And_Configured_Is_Sending_Even_With_An_Empty_Log() =>
        Assert.Equal(["SMS"], await SendingChannels(Sms(enabled: true, configured: true)));

    [Fact]
    public async Task Sms_Configured_But_Not_Switched_On_Is_Not_Sending() =>
        Assert.Empty(await SendingChannels(Sms(enabled: false, configured: true)));

    [Fact]
    public async Task Sms_Switched_On_Without_A_Gateway_Is_Not_Sending() =>
        Assert.Empty(await SendingChannels(Sms(enabled: true, configured: false)));
}
