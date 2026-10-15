using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Application.Features.StaffChat.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Features.StaffChat;

/// <summary>
/// A group practice: an owner-dentist (admin, linked dentist), a second dentist, two secretaries — plus an account
/// of another clinic. Every collection read is stubbed empty, since Moq's default for one is null.
/// </summary>
internal sealed class StaffChatHarness
{
    public static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public User Owner { get; } = User.CreateLocalUser(ClinicId, "admin", "amine@cabinet.tn", "HASH", "Dr Amine");
    public User Dentist { get; } = User.CreateLocalUser(ClinicId, "doctor", "sami@cabinet.tn", "HASH", "Dr Sami");
    public User Salma { get; } = User.CreateLocalUser(ClinicId, "secretary", "salma@cabinet.tn", "HASH", "Salma");
    public User Amel { get; } = User.CreateLocalUser(ClinicId, "secretary", "amel@cabinet.tn", "HASH", "Amel");
    public User Outsider { get; } = User.CreateLocalUser(OtherClinicId, "secretary", "x@ailleurs.tn", "HASH", "Ailleurs");

    public Mock<IStaffMessageRepository> Messages { get; } = new();
    public Mock<IStaffQuickMessageListRepository> QuickMessageLists { get; } = new();
    public Mock<IUserRepository> Users { get; } = new();
    public Mock<IDoctorRepository> Doctors { get; } = new();
    public Mock<IClinicContext> Context { get; } = new();
    public Mock<IUnitOfWork> UnitOfWork { get; } = new();
    public Mock<IRealtimeNotifier> Realtime { get; } = new();
    public Mock<IStaffPresence> Presence { get; } = new();

    public List<User> ClinicUsers { get; }

    public StaffChatHarness(IEnumerable<User>? clinicUsers = null)
    {
        ClinicUsers = (clinicUsers ?? new[] { Owner, Dentist, Salma, Amel }).ToList();

        foreach (var user in ClinicUsers.Append(Outsider))
        {
            Users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        }

        Users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PagedResult<User>.Unpaged(ClinicUsers));
        Users.Setup(r => r.GetByClinicIdAsync(OtherClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PagedResult<User>.Unpaged(new[] { Outsider }));

        var ownerDoctor = new Doctor(Guid.NewGuid(), ClinicId, "Amine", "Ben Salah", "General");
        ownerDoctor.LinkToUser(Owner.Id);
        var secondDoctor = new Doctor(Guid.NewGuid(), ClinicId, "Sami", "Trabelsi", "General");
        secondDoctor.LinkToUser(Dentist.Id);
        Doctors.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ownerDoctor, secondDoctor });
        Doctors.Setup(r => r.GetByClinicIdAsync(OtherClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Doctor>());

        Messages.Setup(r => r.GetLatestPerConversationAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StaffMessage>());
        Messages.Setup(r => r.GetUnreadCountsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StaffUnreadCount>());
        Messages.Setup(r => r.GetRecentUnreadAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StaffMessage>());
        Messages.Setup(r => r.GetPageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StaffMessage>());

        Messages.Setup(r => r.GetQuickMessageUsageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int>());

        UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    public StaffChatHarness ActAs(User user)
    {
        Context.Setup(c => c.GetUserId()).Returns(user.Id);
        return this;
    }

    public static StaffMessage Message(StaffConversation conversation, User sender, DateTime sentAt, string body = "Bonjour", Guid? clinicId = null) =>
        StaffMessage.Create(clinicId ?? ClinicId, conversation, sender.Id, body, null, null, null, sentAt);

    public SendStaffMessageCommandHandler Send() => new(
        Messages.Object, QuickMessageLists.Object, Users.Object, Doctors.Object, Context.Object, UnitOfWork.Object, Realtime.Object,
        NullLogger<SendStaffMessageCommandHandler>.Instance);

    public MarkStaffConversationReadCommandHandler MarkRead() => new(
        Messages.Object, Users.Object, Doctors.Object, Context.Object, UnitOfWork.Object, Realtime.Object,
        NullLogger<MarkStaffConversationReadCommandHandler>.Instance);

    public GetStaffConversationsQueryHandler Conversations() => new(
        Messages.Object, Users.Object, Doctors.Object, Context.Object, Presence.Object);

    public GetStaffChatUnreadQueryHandler Unread() => new(
        Messages.Object, Users.Object, Doctors.Object, Context.Object);

    public GetStaffMessagesQueryHandler Thread() => new(
        Messages.Object, Users.Object, Doctors.Object, Context.Object);

    public GetStaffColleaguesQueryHandler Colleagues() => new(
        Users.Object, Doctors.Object, Context.Object, Presence.Object);

    public GetStaffQuickMessagesQueryHandler QuickMessages() => new(
        QuickMessageLists.Object, Messages.Object, Users.Object, Context.Object);

    public SaveStaffQuickMessagesCommandHandler SaveQuickMessages() => new(
        QuickMessageLists.Object, Messages.Object, Users.Object, Context.Object, UnitOfWork.Object, Realtime.Object,
        NullLogger<SaveStaffQuickMessagesCommandHandler>.Instance);

    public ResetStaffQuickMessagesCommandHandler ResetQuickMessages() => new(
        QuickMessageLists.Object, Messages.Object, Users.Object, Context.Object, UnitOfWork.Object, Realtime.Object,
        NullLogger<ResetStaffQuickMessagesCommandHandler>.Instance);
}
