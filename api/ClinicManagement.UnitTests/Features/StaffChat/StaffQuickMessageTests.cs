using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.StaffChat;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Application.Features.StaffChat.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.StaffChat;

/// <summary>Quick messages: per-person order (AC-2.2), the shared list's saves (US-6, AC-6.5) and sending one (AC-2.1, AC-6.4).</summary>
public class StaffQuickMessageTests
{
    private static readonly DateTime Nine = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    private static List<StaffQuickMessageInputDto> DefaultInputs() => StaffQuickMessageDefaults.Items()
        .Select(i => new StaffQuickMessageInputDto
        {
            Key = i.Key,
            Label = i.Label,
            For = StaffQuickMessageCatalog.AudienceKey(i.Audience),
            AsksForPatient = i.AsksForPatient,
            Urgent = i.IsUrgent,
        })
        .ToList();

    private static StaffQuickMessageList SavedList(Func<StaffQuickMessageInput, StaffQuickMessageInput>? edit = null) =>
        StaffQuickMessageList.Create(StaffChatHarness.ClinicId, StaffQuickMessageDefaults.Items()
            .Select(i => new StaffQuickMessageInput(i.Key, i.Label, i.Audience, i.AsksForPatient, i.IsUrgent))
            .Select(edit ?? (i => i)), Nine);

    private static void Usage(StaffChatHarness h, Dictionary<string, int> counts, Action<DateTime>? since = null) =>
        h.Messages.Setup(r => r.GetQuickMessageUsageAsync(StaffChatHarness.ClinicId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, DateTime, CancellationToken>((_, _, s, _) => since?.Invoke(s))
            .ReturnsAsync(counts);

    // ── Reading ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Cabinet_With_No_Saved_List_Reads_The_Seventeen_Defaults_At_Version_Zero() // FR-4
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);

        var result = await h.QuickMessages().Handle(new GetStaffQuickMessagesQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0u, result.Value!.Version);
        Assert.Equal(StaffQuickMessageDefaults.Items().Select(i => i.Key), result.Value.Items.Select(i => i.Key));
        Assert.Equal("dentist", result.Value.Items[0].For);
        Assert.True(result.Value.Items.Single(i => i.Key == StaffQuickMessageDefaults.ChairHelpKey).Urgent);
    }

    [Fact]
    public async Task What_The_Person_Sends_Most_Comes_First_Then_The_Cabinets_Order() // AC-2.2
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        Usage(h, new Dictionary<string, int> { ["reply-ok"] = 2, ["call-lab"] = 5, ["reply-coming"] = 2 });

        var items = (await h.QuickMessages().Handle(new GetStaffQuickMessagesQuery(), default)).Value!.Items;

        // Ties keep the cabinet's order: « J'arrive » follows « OK » in the list, so it follows it here too.
        Assert.Equal(new[] { "call-lab", "reply-ok", "reply-coming" }, items.Take(3).Select(i => i.Key));
        Assert.Equal(StaffQuickMessageDefaults.PatientArrivedKey, items[3].Key);
        Assert.Equal(StaffQuickMessageDefaults.Items().Single(i => i.Key == "call-lab").SortOrder, items[0].Order);
    }

    [Fact]
    public async Task The_Order_Counts_Only_The_Callers_Own_Sends_Of_The_Last_Sixty_Tunisian_Days() // AC-2.2
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Dentist);
        string? askedFor = null;
        DateTime since = default;
        h.Messages.Setup(r => r.GetQuickMessageUsageAsync(StaffChatHarness.ClinicId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, DateTime, CancellationToken>((_, user, s, _) => { askedFor = user; since = s; })
            .ReturnsAsync(new Dictionary<string, int>());
        var before = DateTime.UtcNow;

        await h.QuickMessages().Handle(new GetStaffQuickMessagesQuery(), default);

        Assert.Equal(h.Dentist.Id, askedFor);
        var expected = ClinicClock.StartOfLocalDayUtc(ClinicClock.ClinicToday(before).AddDays(-59));
        Assert.Equal(expected, since);
        Assert.Equal(DateTimeKind.Utc, since.Kind);
    }

    [Fact]
    public async Task A_Saved_List_Is_Read_In_Its_Own_Order_With_Its_Own_Labels() // US-6
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var list = SavedList(i => i.Key == "reply-ok" ? i with { Label = "D'accord" } : i);
        h.QuickMessageLists.Setup(r => r.GetByClinicIdAsync(StaffChatHarness.ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(list);

        var dto = (await h.QuickMessages().Handle(new GetStaffQuickMessagesQuery(), default)).Value!;

        Assert.Contains(dto.Items, i => i.Key == "reply-ok" && i.Label == "D'accord");
    }

    // ── Saving ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_First_Save_Creates_The_List_And_Signals_The_Whole_Cabinet() // AC-6.1
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        StaffQuickMessageList? added = null;
        h.QuickMessageLists.Setup(r => r.AddAsync(It.IsAny<StaffQuickMessageList>(), It.IsAny<CancellationToken>()))
            .Callback<StaffQuickMessageList, CancellationToken>((l, _) => added = l);
        var items = DefaultInputs();
        items.Add(new StaffQuickMessageInputDto { Label = "Stérilisation prête", For = "desk" });

        var result = await h.SaveQuickMessages().Handle(new SaveStaffQuickMessagesCommand { Version = 0, Items = items }, default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(18, added!.Items.Count);
        Assert.Contains(result.Value!.Items, i => i.Label == "Stérilisation prête" && i.For == "desk" && !i.BuiltIn);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        h.Realtime.Verify(n => n.NotifyStaffChatAsync(StaffChatHarness.ClinicId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Save_Of_A_Saved_List_Is_Checked_Against_The_Version_The_Editor_Opened() // AC-6.5
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var list = SavedList();
        h.QuickMessageLists.Setup(r => r.GetByClinicIdAsync(StaffChatHarness.ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(list);
        var items = DefaultInputs();
        items.Reverse();

        var result = await h.SaveQuickMessages().Handle(new SaveStaffQuickMessagesCommand { Version = 42, Items = items }, default);

        Assert.True(result.IsSuccess);
        h.UnitOfWork.Verify(u => u.SetExpectedVersion(list, 42u), Times.Once);
        Assert.Equal("reply-not-now", list.Items.Single(i => i.SortOrder == 0).Key);
    }

    [Fact]
    public async Task A_Stale_Save_Is_A_Conflict_Not_An_Overwrite() // AC-6.5
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        h.QuickMessageLists.Setup(r => r.GetByClinicIdAsync(StaffChatHarness.ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(SavedList());
        h.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new ConflictException(ErrorMessages.Conflict));

        await Assert.ThrowsAsync<ConflictException>(() =>
            h.SaveQuickMessages().Handle(new SaveStaffQuickMessagesCommand { Version = 7, Items = DefaultInputs() }, default));

        h.Realtime.Verify(n => n.NotifyStaffChatAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Saving_Over_The_Defaults_After_A_Colleague_Saved_The_First_List_Is_A_Conflict() // AC-6.5
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        h.QuickMessageLists.Setup(r => r.GetByClinicIdAsync(StaffChatHarness.ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(SavedList());

        await Assert.ThrowsAsync<ConflictException>(() =>
            h.SaveQuickMessages().Handle(new SaveStaffQuickMessagesCommand { Version = 0, Items = DefaultInputs() }, default));

        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Saving_A_List_A_Colleague_Has_Since_Reset_Is_A_Conflict() // AC-6.5
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);

        await Assert.ThrowsAsync<ConflictException>(() =>
            h.SaveQuickMessages().Handle(new SaveStaffQuickMessagesCommand { Version = 9, Items = DefaultInputs() }, default));

        h.QuickMessageLists.Verify(r => r.AddAsync(It.IsAny<StaffQuickMessageList>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Two_First_Saves_At_Once_Lose_On_The_Unique_Index_As_A_Conflict() // AC-6.5
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        h.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("duplicate", new FakeUniqueViolation()));

        await Assert.ThrowsAsync<ConflictException>(() =>
            h.SaveQuickMessages().Handle(new SaveStaffQuickMessagesCommand { Version = 0, Items = DefaultInputs() }, default));
    }

    [Fact]
    public async Task Removing_A_Built_In_Is_Refused_With_A_Code_And_Nothing_Is_Saved() // AC-6.2
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var items = DefaultInputs().Where(i => i.Key != StaffQuickMessageDefaults.PatientArrivedKey).ToList();

        var result = await h.SaveQuickMessages().Handle(new SaveStaffQuickMessagesCommand { Version = 0, Items = items }, default);

        Assert.True(result.IsFailure);
        Assert.Equal(StaffChatRefusals.InvalidQuickMessagesCode, result.Code);
        Assert.Contains("« Patient arrivé » ne peut pas être supprimé", result.Error);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Message_With_No_Recipient_Is_Refused_By_Name() // FR-4 — « for » required
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var items = DefaultInputs();
        items.Add(new StaffQuickMessageInputDto { Label = "Sans destinataire", For = "" });

        var result = await h.SaveQuickMessages().Handle(new SaveStaffQuickMessagesCommand { Version = 0, Items = items }, default);

        Assert.Equal(StaffChatRefusals.InvalidQuickMessagesCode, result.Code);
        Assert.Contains("« Sans destinataire »", result.Error);
    }

    // ── Resetting ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resetting_Removes_The_Saved_List_And_Returns_The_Defaults() // AC-6.3
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var list = SavedList(i => i.Key == "reply-ok" ? i with { Label = "D'accord" } : i);
        h.QuickMessageLists.Setup(r => r.GetByClinicIdAsync(StaffChatHarness.ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(list);

        var result = await h.ResetQuickMessages().Handle(new ResetStaffQuickMessagesCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0u, result.Value!.Version);
        Assert.Contains(result.Value.Items, i => i.Key == "reply-ok" && i.Label == "OK");
        h.QuickMessageLists.Verify(r => r.Remove(list), Times.Once);
        h.Realtime.Verify(n => n.NotifyStaffChatAsync(StaffChatHarness.ClinicId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resetting_The_Defaults_Saves_Nothing()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);

        var result = await h.ResetQuickMessages().Handle(new ResetStaffQuickMessagesCommand(), default);

        Assert.True(result.IsSuccess);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Sending one ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Quick_Message_Is_Sent_With_Its_Label_And_Urgency_As_They_Are_Now() // AC-2.1, AC-6.4
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        var list = SavedList(i => i.Key == StaffQuickMessageDefaults.ChairHelpKey ? i with { Label = "Au secours" } : i);
        h.QuickMessageLists.Setup(r => r.GetByClinicIdAsync(StaffChatHarness.ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(list);
        StaffMessage? saved = null;
        h.Messages.Setup(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()))
            .Callback<StaffMessage, CancellationToken>((m, _) => saved = m);

        var result = await h.Send().Handle(new SendStaffMessageCommand
        {
            Conversation = StaffConversation.Desk(h.Owner.Id).Key,
            QuickMessageKey = StaffQuickMessageDefaults.ChairHelpKey,
        }, default);

        Assert.True(result.IsSuccess);
        Assert.Null(saved!.Body);
        Assert.Equal("Au secours", saved.QuickMessageLabel);
        Assert.True(saved.IsUrgent);
        Assert.Equal("Au secours", result.Value!.QuickMessage!.Label);
        Assert.True(result.Value.QuickMessage.Urgent);
    }

    [Fact]
    public async Task A_Default_Quick_Message_Is_Sent_While_The_Cabinet_Has_No_Saved_List() // FR-4
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        StaffMessage? saved = null;
        h.Messages.Setup(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()))
            .Callback<StaffMessage, CancellationToken>((m, _) => saved = m);

        var result = await h.Send().Handle(new SendStaffMessageCommand
        {
            Conversation = StaffConversation.Direct(h.Salma.Id, h.Owner.Id).Key,
            QuickMessageKey = StaffQuickMessageDefaults.PatientArrivedKey,
        }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Patient arrivé", saved!.QuickMessageLabel);
        Assert.False(saved.IsUrgent);
    }

    [Fact]
    public async Task A_Quick_Message_Removed_From_The_List_Is_Refused_With_Its_Code() // US-6
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var withoutOk = StaffQuickMessageList.Create(StaffChatHarness.ClinicId, StaffQuickMessageDefaults.Items()
            .Where(i => i.Key != "reply-ok")
            .Select(i => new StaffQuickMessageInput(i.Key, i.Label, i.Audience, i.AsksForPatient, i.IsUrgent)), Nine);
        h.QuickMessageLists.Setup(r => r.GetByClinicIdAsync(StaffChatHarness.ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(withoutOk);

        var result = await h.Send().Handle(new SendStaffMessageCommand
        {
            Conversation = StaffConversation.Direct(h.Salma.Id, h.Owner.Id).Key,
            QuickMessageKey = "reply-ok",
        }, default);

        Assert.Equal(StaffChatRefusals.UnknownQuickMessageCode, result.Code);
        h.Messages.Verify(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Quick_Message_May_Carry_A_Text_Too()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        StaffMessage? saved = null;
        h.Messages.Setup(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()))
            .Callback<StaffMessage, CancellationToken>((m, _) => saved = m);

        await h.Send().Handle(new SendStaffMessageCommand
        {
            Conversation = StaffConversation.Direct(h.Salma.Id, h.Owner.Id).Key,
            QuickMessageKey = "patient-late",
            Body = "  M. Ben Ali  ",
        }, default);

        Assert.Equal("M. Ben Ali", saved!.Body);
        Assert.Equal("Patient en retard (10 min)", saved.QuickMessageLabel);
    }

    private sealed class FakeUniqueViolation : Exception
    {
        public string SqlState => "23505";
    }
}
