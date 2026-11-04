using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Demo;

/// <summary>
/// Which demo cabinets the nightly rebuild may remove (<c>demo-cabinet</c> FR-7): every one but the current cabinet,
/// one still being built, and one a visitor can still be inside — a live session family on any of its accounts.
/// </summary>
/// <remarks>Reads <c>Clinics</c>, <c>Users</c> and <c>SessionFamilies</c> — none filtered by clinic — so it needs no tenant scope.</remarks>
public sealed class DemoCabinetRetirement
{
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly ISessionFamilyRepository _families;

    public DemoCabinetRetirement(IClinicRepository clinics, IUserRepository users, ISessionFamilyRepository families)
    {
        _clinics = clinics;
        _users = users;
        _families = families;
    }

    public async Task<IReadOnlyList<Guid>> RetirableAsync(
        Guid currentClinicId, Func<Guid, bool> isBeingBuilt, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var retirable = new List<Guid>();
        foreach (var clinic in await _clinics.GetAllAsync(cancellationToken))
        {
            if (clinic.Id == currentClinicId || isBeingBuilt(clinic.Id))
            {
                continue;
            }

            var members = (await _users.GetByClinicIdAsync(clinic.Id, null, null, cancellationToken)).Items;
            if (!IsDemoCabinet(clinic, members) || await AnyoneInsideAsync(members, nowUtc, cancellationToken))
            {
                continue;
            }

            retirable.Add(clinic.Id);
        }

        return retirable;
    }

    // A demo profile pointed at a real database must never delete a real practice: only the scenario's own cabinets go.
    public static bool IsDemoCabinet(Clinic clinic, IReadOnlyCollection<User> members) =>
        clinic.Name == DemoScenario.CabinetName
        && members.Count > 0
        && members.All(u => u.Email?.EndsWith("@" + DemoScenario.EmailDomain, StringComparison.OrdinalIgnoreCase) == true);

    private async Task<bool> AnyoneInsideAsync(IEnumerable<User> members, DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var member in members)
        {
            if ((await _families.GetLiveForUserAsync(member.Id, nowUtc, cancellationToken)).Count > 0)
            {
                return true;
            }
        }

        return false;
    }
}
