using ClinicManagement.Application.Features.StaffChat;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.StaffChat;

public class SendStaffMessageTests
{
    // staff-chat Part 7: the phones a message reaches are exactly the people the screen rings, never the sender.
    [Fact]
    public async Task A_Message_Queues_The_Phones_Of_The_People_It_Rings()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var key = StaffConversation.Direct(h.Salma.Id, h.Owner.Id).Key;

        var result = await h.Send().Handle(new SendStaffMessageCommand { Conversation = key, Body = "La patiente est arrivée" }, default);

        Assert.True(result.IsSuccess);
        h.Push.Verify(p => p.QueueAsync(
            It.Is<StaffMessage>(m => m.ConversationKey == key),
            It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { h.Owner.Id })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // A refused message queues nothing.
    [Fact]
    public async Task A_Refused_Message_Queues_No_Phone()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);

        var result = await h.Send().Handle(new SendStaffMessageCommand { Conversation = "clinic", Body = "   " }, default);

        Assert.False(result.IsSuccess);
        h.Push.Verify(p => p.QueueAsync(It.IsAny<StaffMessage>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Message_Is_Saved_And_Signals_The_Two_People_Of_The_Thread_Only() // AC-2.3, FR-2
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var key = StaffConversation.Direct(h.Salma.Id, h.Owner.Id).Key;
        StaffMessage? saved = null;
        h.Messages.Setup(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()))
            .Callback<StaffMessage, CancellationToken>((m, _) => saved = m);

        var result = await h.Send().Handle(new SendStaffMessageCommand { Conversation = key, Body = "  Le labo a appelé\n  " }, default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.Equal("Le labo a appelé", saved!.Body);
        Assert.Equal(key, saved.ConversationKey);
        Assert.Equal(h.Salma.Id, saved.SenderUserId);
        Assert.Equal("Salma", result.Value!.Sender.Name);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        h.Realtime.Verify(n => n.NotifyStaffChatAsync(
            StaffChatHarness.ClinicId,
            It.Is<IReadOnlyCollection<string>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { h.Owner.Id, h.Salma.Id }.OrderBy(x => x))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Cabinet_Message_Signals_Every_Account_Of_The_Cabinet()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Dentist);

        var result = await h.Send().Handle(new SendStaffMessageCommand { Conversation = "clinic", Body = "Réunion à 13h" }, default);

        Assert.True(result.IsSuccess);
        h.Realtime.Verify(n => n.NotifyStaffChatAsync(
            StaffChatHarness.ClinicId,
            It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 4),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public async Task A_Blank_Message_Is_Refused_And_Nothing_Is_Saved(string? body) // AC-2.3
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);

        var result = await h.Send().Handle(new SendStaffMessageCommand { Conversation = "clinic", Body = body }, default);

        Assert.Equal(StaffChatRefusals.EmptyCode, result.Code);
        h.Messages.Verify(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Message_Over_The_Limit_Is_Refused_With_Its_Own_Code() // AC-2.3
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);

        var result = await h.Send().Handle(
            new SendStaffMessageCommand { Conversation = "clinic", Body = new string('x', StaffMessage.BodyMaxLength + 1) }, default);

        Assert.Equal(StaffChatRefusals.TooLongCode, result.Code);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Thread_The_Caller_Does_Not_Belong_To_Reads_As_Not_Found() // FR-1: private to its two people
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Amel);
        var someoneElses = StaffConversation.Direct(h.Salma.Id, h.Owner.Id).Key;

        var result = await h.Send().Handle(new SendStaffMessageCommand { Conversation = someoneElses, Body = "?" }, default);

        Assert.Equal(StaffChatRefusals.ConversationNotFoundCode, result.Code);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("dm:nope")]
    [InlineData("desk:")]
    public async Task An_Unreadable_Key_Reads_As_Not_Found(string key)
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);

        var result = await h.Send().Handle(new SendStaffMessageCommand { Conversation = key, Body = "?" }, default);

        Assert.Equal(StaffChatRefusals.ConversationNotFoundCode, result.Code);
    }

    [Fact]
    public async Task Writing_To_A_Deactivated_Colleague_Is_Refused_As_Read_Only() // FR-9, EC-7
    {
        var h = new StaffChatHarness();
        h.Salma.Deactivate();
        h.ActAs(h.Owner);

        var result = await h.Send().Handle(
            new SendStaffMessageCommand { Conversation = StaffConversation.Direct(h.Owner.Id, h.Salma.Id).Key, Body = "Bonjour" }, default);

        Assert.Equal(StaffChatRefusals.RecipientInactiveCode, result.Code);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Signal_That_Fails_Never_Fails_A_Message_Already_Saved()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        h.Realtime.Setup(n => n.NotifyStaffChatAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        var result = await h.Send().Handle(new SendStaffMessageCommand { Conversation = "clinic", Body = "Bonjour" }, default);

        Assert.True(result.IsSuccess);
    }
}
