using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Features.StaffChat;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Application.Features.StaffChat.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.StaffChat;

/// <summary>The shared secretariat: « Je m'en occupe » (FR-6, AC-5.1/5.2, EC-4) and « Vu par … » (AC-5.4).</summary>
public class StaffDeskTests
{
    private static readonly DateTime Nine = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    private static StaffMessage OwnerRequest(StaffChatHarness h)
    {
        var message = StaffChatHarness.Message(StaffConversation.Desk(h.Owner.Id), h.Owner, Nine, "Faites entrer le patient suivant");
        h.Messages.Setup(r => r.GetByIdAsync(message.Id, It.IsAny<CancellationToken>())).ReturnsAsync(message);
        return message;
    }

    // ── « Je m'en occupe » ───────────────────────────────────────────────────

    [Fact]
    public async Task A_Secretary_Takes_The_Dentists_Request_For_The_Whole_Secretariat() // AC-5.1
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var message = OwnerRequest(h);

        var result = await h.Claim().Handle(new ClaimStaffMessageCommand { MessageId = message.Id }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(h.Salma.Id, message.ClaimedByUserId);
        Assert.Equal("Salma", result.Value!.ClaimedBy!.Name);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        h.Realtime.Verify(n => n.NotifyStaffChatAsync(
            StaffChatHarness.ClinicId,
            It.Is<IReadOnlyCollection<string>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { h.Owner.Id, h.Salma.Id, h.Amel.Id }.OrderBy(x => x))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Second_Secretary_Is_Told_Who_Has_It() // AC-5.2, EC-4
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Amel);
        var message = OwnerRequest(h);
        message.Claim(h.Salma.Id, Nine);

        var result = await h.Claim().Handle(new ClaimStaffMessageCommand { MessageId = message.Id }, default);

        Assert.Equal(StaffChatRefusals.AlreadyClaimedCode, result.Code);
        Assert.Equal("Déjà pris par Salma.", result.Error);
        Assert.Equal(h.Salma.Id, message.ClaimedByUserId);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Two_Secretaries_At_Once_The_Loser_Is_Told_Who_Won_Not_That_Something_Failed() // EC-4
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Amel);
        var message = OwnerRequest(h);
        h.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException(ErrorMessages.Conflict));
        h.Messages.Setup(r => r.GetClaimedByAsync(StaffChatHarness.ClinicId, message.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(h.Salma.Id);

        var result = await h.Claim().Handle(new ClaimStaffMessageCommand { MessageId = message.Id }, default);

        Assert.Equal(StaffChatRefusals.AlreadyClaimedCode, result.Code);
        Assert.Equal("Déjà pris par Salma.", result.Error);
        h.Realtime.Verify(n => n.NotifyStaffChatAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Taking_It_Twice_Is_Not_A_Refusal()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var message = OwnerRequest(h);
        message.Claim(h.Salma.Id, Nine);

        var result = await h.Claim().Handle(new ClaimStaffMessageCommand { MessageId = message.Id }, default);

        Assert.True(result.IsSuccess);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Dentist_Cannot_Take_Their_Own_Request() // FR-6
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        var message = OwnerRequest(h);

        var result = await h.Claim().Handle(new ClaimStaffMessageCommand { MessageId = message.Id }, default);

        Assert.Equal(StaffChatRefusals.NotClaimableCode, result.Code);
        Assert.Null(message.ClaimedByUserId);
    }

    [Fact]
    public async Task A_Secretarys_Own_Reply_Cannot_Be_Taken() // FR-6
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Amel);
        var reply = StaffChatHarness.Message(StaffConversation.Desk(h.Owner.Id), h.Salma, Nine, "C'est fait");
        h.Messages.Setup(r => r.GetByIdAsync(reply.Id, It.IsAny<CancellationToken>())).ReturnsAsync(reply);

        var result = await h.Claim().Handle(new ClaimStaffMessageCommand { MessageId = reply.Id }, default);

        Assert.Equal(StaffChatRefusals.NotClaimableCode, result.Code);
    }

    [Fact]
    public async Task A_Two_Person_Message_Cannot_Be_Taken()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var message = StaffChatHarness.Message(StaffConversation.Direct(h.Owner.Id, h.Salma.Id), h.Owner, Nine);
        h.Messages.Setup(r => r.GetByIdAsync(message.Id, It.IsAny<CancellationToken>())).ReturnsAsync(message);

        var result = await h.Claim().Handle(new ClaimStaffMessageCommand { MessageId = message.Id }, default);

        Assert.Equal(StaffChatRefusals.NotClaimableCode, result.Code);
    }

    [Fact]
    public async Task Another_Dentists_Secretariat_Is_Not_Found_For_Them() // FR-1
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Dentist);
        var message = OwnerRequest(h);

        var result = await h.Claim().Handle(new ClaimStaffMessageCommand { MessageId = message.Id }, default);

        Assert.Equal(StaffChatRefusals.MessageNotFoundCode, result.Code);
    }

    [Fact]
    public async Task Another_Cabinets_Message_Is_Not_Found()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var foreign = StaffChatHarness.Message(StaffConversation.Desk(h.Owner.Id), h.Owner, Nine, clinicId: StaffChatHarness.OtherClinicId);
        h.Messages.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await h.Claim().Handle(new ClaimStaffMessageCommand { MessageId = foreign.Id }, default);

        Assert.Equal(StaffChatRefusals.MessageNotFoundCode, result.Code);
    }

    // ── « Je ne peux plus » ──────────────────────────────────────────────────

    [Fact]
    public async Task The_Taker_Hands_It_Back_And_The_Thread_Is_Told() // AC-5.2
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var message = OwnerRequest(h);
        message.Claim(h.Salma.Id, Nine);

        var result = await h.Release().Handle(new ReleaseStaffMessageClaimCommand { MessageId = message.Id }, default);

        Assert.True(result.IsSuccess);
        Assert.Null(message.ClaimedByUserId);
        Assert.Null(result.Value!.ClaimedBy);
        h.Realtime.Verify(n => n.NotifyStaffChatAsync(StaffChatHarness.ClinicId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Someone_Else_Cannot_Hand_It_Back() // AC-5.2
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Amel);
        var message = OwnerRequest(h);
        message.Claim(h.Salma.Id, Nine);

        var result = await h.Release().Handle(new ReleaseStaffMessageClaimCommand { MessageId = message.Id }, default);

        Assert.Equal(StaffChatRefusals.NotYourClaimCode, result.Code);
        Assert.Equal(h.Salma.Id, message.ClaimedByUserId);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── « Vu par … » ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Vu_Par_Names_The_Other_Members_Whose_Marker_Reaches_The_Message() // AC-5.4
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        var desk = StaffConversation.Desk(h.Owner.Id);
        var earlier = StaffChatHarness.Message(desk, h.Owner, Nine, "Premier");
        var latest = StaffChatHarness.Message(desk, h.Owner, Nine.AddMinutes(10), "Second");
        h.Messages.Setup(r => r.GetPageAsync(StaffChatHarness.ClinicId, desk.Key, null, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { latest, earlier });
        h.Messages.Setup(r => r.GetMarkersAsync(StaffChatHarness.ClinicId, desk.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new StaffChatReadMarker(StaffChatHarness.ClinicId, h.Salma.Id, desk.Key, Nine.AddMinutes(10)),
                new StaffChatReadMarker(StaffChatHarness.ClinicId, h.Amel.Id, desk.Key, Nine.AddMinutes(5)),
                new StaffChatReadMarker(StaffChatHarness.ClinicId, h.Owner.Id, desk.Key, Nine.AddMinutes(10)),
                new StaffChatReadMarker(StaffChatHarness.ClinicId, h.Dentist.Id, desk.Key, Nine.AddMinutes(10)),
            });

        var items = (await h.Thread().Handle(new GetStaffMessagesQuery { Conversation = desk.Key }, default)).Value!.Items;

        // The sender never sees themself; a non-member (another dentist) never appears, whatever a stray marker says.
        Assert.Equal(new[] { "Salma" }, items[0].SeenBy.Select(s => s.Name));
        Assert.Equal(new[] { "Amel", "Salma" }, items[1].SeenBy.Select(s => s.Name));
    }

    [Fact]
    public async Task A_Read_In_A_Two_Person_Thread_Wakes_Both_People_For_Vu() // AC-5.4
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var direct = StaffConversation.Direct(h.Owner.Id, h.Salma.Id);
        var message = StaffChatHarness.Message(direct, h.Owner, Nine);
        h.Messages.Setup(r => r.GetByIdAsync(message.Id, It.IsAny<CancellationToken>())).ReturnsAsync(message);

        var result = await h.MarkRead().Handle(new MarkStaffConversationReadCommand { Conversation = direct.Key, UpToMessageId = message.Id }, default);

        Assert.True(result.IsSuccess);
        h.Realtime.Verify(n => n.NotifyStaffChatAsync(
            StaffChatHarness.ClinicId,
            It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 2 && ids.Contains(h.Owner.Id) && ids.Contains(h.Salma.Id)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Read_In_The_Cabinet_Thread_Wakes_Only_The_Readers_Tabs() // EC-9 — the cabinet shows no « Vu »
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var message = StaffChatHarness.Message(StaffConversation.Clinic, h.Owner, Nine);
        h.Messages.Setup(r => r.GetByIdAsync(message.Id, It.IsAny<CancellationToken>())).ReturnsAsync(message);

        await h.MarkRead().Handle(new MarkStaffConversationReadCommand { Conversation = "clinic", UpToMessageId = message.Id }, default);

        h.Realtime.Verify(n => n.NotifyStaffChatAsync(
            StaffChatHarness.ClinicId,
            It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1 && ids.Contains(h.Salma.Id)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
