using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.StaffChat;

/// <summary>One account of the cabinet as the chat shows it.</summary>
public sealed record StaffChatColleague(
    string UserId, string Name, bool IsSecretary, bool IsDentist, bool IsActive, DateTime CreatedAt);

/// <summary>A practitioner of the cabinet, account or not — what an appointment names (FR-5).</summary>
public sealed record StaffChatDentist(Guid DoctorId, string Name, string? UserId, bool IsActive);

/// <summary>The caller and every account of their cabinet, loaded once per request (active or not, so history keeps names).</summary>
public sealed class StaffChatRoster
{
    public Guid ClinicId { get; }
    public StaffChatColleague Viewer { get; }
    public IReadOnlyList<StaffChatColleague> Colleagues { get; }
    public IReadOnlyCollection<StaffChatMember> Members { get; }
    public IReadOnlyList<StaffChatDentist> Dentists { get; }

    public StaffChatRoster(
        Guid clinicId, StaffChatColleague viewer, IReadOnlyList<StaffChatColleague> colleagues,
        IReadOnlyList<StaffChatDentist>? dentists = null)
    {
        ClinicId = clinicId;
        Viewer = viewer;
        Colleagues = colleagues;
        Dentists = dentists ?? Array.Empty<StaffChatDentist>();
        Members = colleagues.Select(c => new StaffChatMember(c.UserId, c.IsSecretary, c.IsActive)).ToList();
        // Order of arrival, inactive accounts included: a new colleague takes the next colour and changes nobody's.
        _ranks = colleagues
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.UserId, StringComparer.Ordinal)
            .Select((c, index) => (c.UserId, index))
            .ToDictionary(e => e.UserId, e => e.index, StringComparer.Ordinal);
    }

    private readonly Dictionary<string, int> _ranks;

    public StaffChatColleague? Find(string userId) => Colleagues.FirstOrDefault(c => c.UserId == userId);

    /// <summary>The account's place in the cabinet's order of arrival — its colour in the chat; null for an account that is gone.</summary>
    public int? ToneOf(string userId) => _ranks.TryGetValue(userId, out var rank) ? rank : null;

    public string NameOf(string userId) => Find(userId)?.Name ?? "Ancien compte";

    /// <summary>The caller's account, for reads that need no roster (the quick messages).</summary>
    public static async Task<Result<User>> ResolveCallerAsync(
        IClinicContext clinicContext, IUserRepository users, CancellationToken cancellationToken)
    {
        var userId = clinicContext.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result<User>.Failure(StaffChatRefusals.SessionInvalid);
        }

        var caller = await users.GetByAuth0SubAsync(userId, cancellationToken);
        return caller is null
            ? Result<User>.Failure(StaffChatRefusals.UserNotFound)
            : Result<User>.Success(caller);
    }

    public static async Task<Result<StaffChatRoster>> LoadForCallerAsync(
        IClinicContext clinicContext,
        IUserRepository users,
        IDoctorRepository doctors,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveCallerAsync(clinicContext, users, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<StaffChatRoster>.FailureFrom(resolved);
        }

        var caller = resolved.Value!;
        var accounts = await users.GetByClinicIdAsync(caller.ClinicId, null, null, cancellationToken);
        var clinicDoctors = (await doctors.GetByClinicIdAsync(caller.ClinicId, cancellationToken)).ToList();
        var dentistUserIds = clinicDoctors
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

        var dentists = clinicDoctors
            .Select(d => new StaffChatDentist(d.Id, DentistName(d.FullName), d.UserId, d.IsActive))
            .ToList();

        return Result<StaffChatRoster>.Success(new StaffChatRoster(caller.ClinicId, viewer, colleagues, dentists));
    }

    /// <summary>« Dr Salma Ben Youssef », once — a name already starting with « Dr » is kept as it is.</summary>
    public static string DentistName(string fullName)
    {
        var name = fullName.Trim();
        return name.StartsWith("Dr ", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Dr.", StringComparison.OrdinalIgnoreCase)
            ? name
            : $"Dr {name}";
    }

    private static string DisplayName(string? fullName, string? email) =>
        !string.IsNullOrWhiteSpace(fullName) ? fullName.Trim()
        : !string.IsNullOrWhiteSpace(email) ? email.Trim()
        : "Collègue";
}
