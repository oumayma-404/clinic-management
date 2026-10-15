using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.StaffChat;

/// <summary>One account of the cabinet as the chat shows it.</summary>
public sealed record StaffChatColleague(
    string UserId, string Name, bool IsSecretary, bool IsDentist, bool IsActive, DateTime CreatedAt);

/// <summary>The caller and every account of their cabinet, loaded once per request (active or not, so history keeps names).</summary>
public sealed class StaffChatRoster
{
    public Guid ClinicId { get; }
    public StaffChatColleague Viewer { get; }
    public IReadOnlyList<StaffChatColleague> Colleagues { get; }
    public IReadOnlyCollection<StaffChatMember> Members { get; }

    public StaffChatRoster(Guid clinicId, StaffChatColleague viewer, IReadOnlyList<StaffChatColleague> colleagues)
    {
        ClinicId = clinicId;
        Viewer = viewer;
        Colleagues = colleagues;
        Members = colleagues.Select(c => new StaffChatMember(c.UserId, c.IsSecretary, c.IsActive)).ToList();
    }

    public StaffChatColleague? Find(string userId) => Colleagues.FirstOrDefault(c => c.UserId == userId);

    public string NameOf(string userId) => Find(userId)?.Name ?? "Ancien compte";

    public static async Task<Result<StaffChatRoster>> LoadForCallerAsync(
        IClinicContext clinicContext,
        IUserRepository users,
        IDoctorRepository doctors,
        CancellationToken cancellationToken)
    {
        var userId = clinicContext.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result<StaffChatRoster>.Failure(StaffChatRefusals.SessionInvalid);
        }

        var caller = await users.GetByAuth0SubAsync(userId, cancellationToken);
        if (caller is null)
        {
            return Result<StaffChatRoster>.Failure(StaffChatRefusals.UserNotFound);
        }

        var accounts = await users.GetByClinicIdAsync(caller.ClinicId, null, null, cancellationToken);
        var dentistUserIds = (await doctors.GetByClinicIdAsync(caller.ClinicId, cancellationToken))
            .Where(d => d.IsActive && !string.IsNullOrEmpty(d.UserId))
            .Select(d => d.UserId!)
            .ToHashSet(StringComparer.Ordinal);

        var colleagues = accounts.Items
            .Select(u => new StaffChatColleague(
                u.Id,
                DisplayName(u.FullName, u.Email),
                u.IsSecretary(),
                dentistUserIds.Contains(u.Id),
                u.IsActive,
                u.CreatedAt))
            .ToList();

        var viewer = colleagues.FirstOrDefault(c => c.UserId == caller.Id);
        if (viewer is null)
        {
            return Result<StaffChatRoster>.Failure(StaffChatRefusals.UserNotFound);
        }

        return Result<StaffChatRoster>.Success(new StaffChatRoster(caller.ClinicId, viewer, colleagues));
    }

    private static string DisplayName(string? fullName, string? email) =>
        !string.IsNullOrWhiteSpace(fullName) ? fullName.Trim()
        : !string.IsNullOrWhiteSpace(email) ? email.Trim()
        : "Collègue";
}
