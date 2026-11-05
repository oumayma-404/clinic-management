using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Demo;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Demo;

/// <summary>
/// demo-cabinet FR-7 / EC-2: the nightly rebuild removes a cabinet only once no session can still be using it, and
/// removes it the console's way — rows in one transaction, files after the commit.
/// </summary>
public class DemoCabinetRetirementTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 2, 5, 0, DateTimeKind.Utc);

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ISessionFamilyRepository> _families = new();

    private static User Member(Clinic clinic, string email = "monica.geller+20261103@demo.apexa.tn") =>
        User.CreateLocalUser(clinic.Id, User.RoleAdmin, email, "HASH", "Monica Geller");

    private Clinic Cabinet(User[]? members = null, bool someoneInside = false, string name = DemoScenario.CabinetName)
    {
        var clinic = new Clinic(Guid.NewGuid(), name);
        var staff = members ?? new[] { Member(clinic) };
        _users.Setup(u => u.GetByClinicIdAsync(clinic.Id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<User>.Unpaged(staff));
        foreach (var member in staff)
        {
            var live = someoneInside
                ? new List<SessionFamily> { new(member.Id, "hash", Now.AddMinutes(-30), "Démo") }
                : new List<SessionFamily>();
            _families.Setup(f => f.GetLiveForUserAsync(member.Id, Now, It.IsAny<CancellationToken>())).ReturnsAsync(live);
        }

        return clinic;
    }

    private Task<IReadOnlyList<Guid>> Retirable(Guid current, params Clinic[] all) =>
        RetirableWhileBuilding(current, _ => false, all);

    private Task<IReadOnlyList<Guid>> RetirableWhileBuilding(Guid current, Func<Guid, bool> building, params Clinic[] all)
    {
        _clinics.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(all);
        return new DemoCabinetRetirement(_clinics.Object, _users.Object, _families.Object)
            .RetirableAsync(current, building, Now, CancellationToken.None);
    }

    [Fact] // [FR-7] yesterday's cabinet, empty now, goes; tonight's stays
    public async Task An_Empty_Old_Cabinet_Is_Retired_And_The_Current_One_Kept()
    {
        var yesterday = Cabinet();
        var tonight = Cabinet();

        Assert.Equal(new[] { yesterday.Id }, await Retirable(tonight.Id, yesterday, tonight));
    }

    [Fact] // [EC-2] a visitor still inside yesterday's cabinet keeps walking it until their session ends
    public async Task A_Cabinet_Someone_Is_Still_Inside_Is_Kept()
    {
        var yesterday = Cabinet(someoneInside: true);
        var tonight = Cabinet();

        Assert.Empty(await Retirable(tonight.Id, yesterday, tonight));
    }

    [Fact] // a second rebuild running in this process is not torn down under itself
    public async Task A_Cabinet_Being_Built_Is_Kept()
    {
        var building = Cabinet();
        var tonight = Cabinet();

        Assert.Empty(await RetirableWhileBuilding(tonight.Id, id => id == building.Id, building, tonight));
    }

    [Fact] // a demo profile pointed at a real database must never delete a real practice
    public async Task A_Practice_That_Is_Not_The_Scenarios_Is_Never_Retired()
    {
        var realName = Cabinet(name: "Cabinet Dr Ben Salah");
        var withRealStaff = Cabinet(members: new[] { Member(new Clinic(Guid.NewGuid(), DemoScenario.CabinetName), "secretariat@cabinet.tn") });
        var tonight = Cabinet();

        Assert.Empty(await Retirable(tonight.Id, realName, withRealStaff, tonight));
    }

    [Fact]
    public void A_Cabinet_With_No_Accounts_Is_Not_Recognised_As_The_Demos()
    {
        Assert.False(DemoCabinetRetirement.IsDemoCabinet(new Clinic(Guid.NewGuid(), DemoScenario.CabinetName), Array.Empty<User>()));
    }

    [Fact] // the console's order: transaction, purge, save, commit — and only then the files
    public async Task A_Cabinet_Is_Purged_In_One_Transaction_And_Its_Files_After_The_Commit()
    {
        var steps = new List<string>();
        var (purge, clinicId) = Purge(steps);

        await purge.PurgeAsync(clinicId, CancellationToken.None);

        Assert.Equal(new[] { "begin", "purge", "save", "commit", "files" }, steps);
    }

    [Fact] // rows that did not go take their files with them nowhere: nothing outside the transaction runs
    public async Task A_Failed_Purge_Rolls_Back_And_Leaves_The_Files()
    {
        var steps = new List<string>();
        var (purge, clinicId) = Purge(steps, purgeFails: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => purge.PurgeAsync(clinicId, CancellationToken.None));

        Assert.Equal(new[] { "begin", "purge", "rollback" }, steps);
    }

    [Fact] // the rows are gone; a blob left behind is wasted bytes, not a reason to fail the rebuild
    public async Task Files_That_Cannot_Be_Cleared_Do_Not_Fail_The_Purge()
    {
        var steps = new List<string>();
        var (purge, clinicId) = Purge(steps, filesFail: true);

        await purge.PurgeAsync(clinicId, CancellationToken.None);

        Assert.Equal("files", steps[^1]);
    }

    private (DemoCabinetPurge Purge, Guid ClinicId) Purge(List<string> steps, bool purgeFails = false, bool filesFail = false)
    {
        var clinic = Cabinet();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => steps.Add("begin")).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => steps.Add("save")).ReturnsAsync(0);
        unitOfWork.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => steps.Add("commit")).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => steps.Add("rollback")).Returns(Task.CompletedTask);

        var clinicPurge = new Mock<IClinicPurge>();
        var purgeCall = clinicPurge.Setup(p => p.PurgeAsync(clinic.Id, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => steps.Add("purge"));
        if (purgeFails)
            purgeCall.ThrowsAsync(new InvalidOperationException("purge failed"));
        else
            purgeCall.ReturnsAsync(ClinicPurgeCensus.Empty);

        var storage = new Mock<IFileStorage>();
        var filesCall = storage.Setup(s => s.DeleteByClinicAsync(clinic.Id, It.IsAny<CancellationToken>())).Callback(() => steps.Add("files"));
        if (filesFail)
            filesCall.ThrowsAsync(new IOException("store down"));
        else
            filesCall.ReturnsAsync(0);

        return (new DemoCabinetPurge(unitOfWork.Object, clinicPurge.Object, _users.Object, storage.Object,
            NullLogger<DemoCabinetPurge>.Instance), clinic.Id);
    }
}
