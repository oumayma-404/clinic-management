using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>The phone half of a chat alert (staff-chat Part 7): who, when, and what leaves the building.</summary>
public class StaffChatPushQueueTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Salma = "local|salma";
    private const string Amine = "local|amine";

    private sealed class Harness
    {
        public List<PushDelivery> Rows { get; } = new();
        public StaffChatPushQueue Queue { get; }

        public Harness(bool available = true, IEnumerable<DeviceRegistration>? devices = null, bool deliveriesThrow = false)
        {
            var deviceList = (devices ?? new[] { Device(Amine), Device(Salma) }).ToList();
            var deviceRepo = new Mock<IDeviceRegistrationRepository>();
            deviceRepo.Setup(d => d.GetActiveForUsersAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IEnumerable<string> ids, CancellationToken _) => deviceList.Where(x => ids.Contains(x.UserId)).ToList());

            var deliveries = new Mock<IPushDeliveryRepository>();
            var add = deliveries.Setup(d => d.AddRangeAsync(It.IsAny<IEnumerable<PushDelivery>>(), It.IsAny<CancellationToken>()));
            if (deliveriesThrow)
            {
                add.ThrowsAsync(new InvalidOperationException("database down"));
            }
            else
            {
                add.Callback<IEnumerable<PushDelivery>, CancellationToken>((rows, _) => Rows.AddRange(rows)).Returns(Task.CompletedTask);
            }

            var availability = new Mock<IOsPushAvailability>();
            availability.Setup(a => a.IsAvailableAtAll).Returns(available);

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            Queue = new StaffChatPushQueue(
                deviceRepo.Object, deliveries.Object, unitOfWork.Object, availability.Object,
                new ConfigurationBuilder().Build(), NullLogger<StaffChatPushQueue>.Instance);
        }
    }

    private static DeviceRegistration Device(string userId) =>
        DeviceRegistration.Create(ClinicId, userId, DevicePlatform.Android, $"token-{userId}", "1.4.0", DateTime.UtcNow);

    private static StaffMessage Message(DateTime sentAt) =>
        StaffMessage.Create(ClinicId, StaffConversation.Direct(Salma, Amine), Salma, "La patiente est arrivée", null, null, null, sentAt);

    [Fact]
    public async Task One_Row_Per_Device_Of_The_Recipients_Never_The_Senders()
    {
        var harness = new Harness();
        var message = Message(DateTime.UtcNow);

        await harness.Queue.QueueAsync(message, new[] { Amine, Salma });

        var row = Assert.Single(harness.Rows);
        Assert.Equal(Amine, row.RecipientUserId);
        Assert.Equal(NotificationCategory.StaffChatMessage, row.Category);
        Assert.Equal(message.Id, row.StaffMessageId);
        Assert.Equal("Nouveau message", row.Label);
        Assert.Null(row.AppointmentId);
    }

    // The grace period: a message read on the PC in the next 30 s must have a chance to stop the phone.
    [Fact]
    public async Task The_Push_Waits_The_Grace_Period_Before_It_May_Leave()
    {
        var harness = new Harness();
        var message = Message(DateTime.UtcNow);

        await harness.Queue.QueueAsync(message, new[] { Amine });

        Assert.True(Assert.Single(harness.Rows).SendNotBefore >= message.SentAt + StaffChatPushQueue.Grace);
    }

    [Fact]
    public async Task Nothing_Is_Queued_Where_Push_Is_Unavailable()
    {
        var harness = new Harness(available: false);

        await harness.Queue.QueueAsync(Message(DateTime.UtcNow), new[] { Amine });

        Assert.Empty(harness.Rows);
    }

    // Post-commit and best-effort: a failure here must never reach the send that already succeeded.
    [Fact]
    public async Task A_Queue_Failure_Never_Throws()
    {
        var harness = new Harness(deliveriesThrow: true);

        var error = await Record.ExceptionAsync(() => harness.Queue.QueueAsync(Message(DateTime.UtcNow), new[] { Amine }));

        Assert.Null(error);
    }
}
