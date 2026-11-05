using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// « Premiers pas » ticked by hand (<c>guided-first-days</c>, owner's call 2026-11-04): any step but the security code,
/// the row is created on first use, ticking twice changes nothing, and unticking takes it back.
/// </summary>
public class SetGettingStartedStepTickCommandTests
{
    private const string UserId = "local|admin-1";

    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUserDashboardPreferenceRepository> _preferences = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private UserDashboardPreference? _stored;
    private UserDashboardPreference? _added;

    public SetGettingStartedStepTickCommandTests()
    {
        _context.Setup(c => c.GetUserId()).Returns(UserId);
        _preferences.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _stored);
        _preferences.Setup(r => r.AddAsync(It.IsAny<UserDashboardPreference>(), It.IsAny<CancellationToken>()))
            .Callback<UserDashboardPreference, CancellationToken>((p, _) => _added = p)
            .Returns(Task.CompletedTask);
    }

    private Task<Result> Send(string? step, bool ticked = true) =>
        new SetGettingStartedStepTickCommandHandler(
                _context.Object, _preferences.Object, _unitOfWork.Object,
                NullLogger<SetGettingStartedStepTickCommandHandler>.Instance)
            .Handle(new SetGettingStartedStepTickCommand { Step = step, Ticked = ticked }, CancellationToken.None);

    [Fact]
    public async Task A_First_Tick_Creates_The_Row()
    {
        var result = await Send(GettingStartedKeys.PhoneApp);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(_added);
        Assert.Equal([GettingStartedKeys.PhoneApp], _added!.TickedSteps());
        Assert.Empty(_added.HiddenKpis());
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ticking_Twice_Keeps_One_Entry_And_The_Hidden_Blocks()
    {
        _stored = new UserDashboardPreference(UserId);
        _stored.SetHiddenKpis(["gettingStarted"]);
        _stored.TickStep(GettingStartedKeys.DentalRecord);

        var result = await Send(GettingStartedKeys.DentalRecord);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(_added);
        Assert.Equal([GettingStartedKeys.DentalRecord], _stored.TickedSteps());
        Assert.Equal(["gettingStarted"], _stored.HiddenKpis());
    }

    [Fact]
    public async Task Unticking_Takes_Only_That_Step_Back()
    {
        _stored = new UserDashboardPreference(UserId);
        _stored.TickStep(GettingStartedKeys.DentalRecord);
        _stored.TickStep(GettingStartedKeys.Letterhead);

        var result = await Send(GettingStartedKeys.DentalRecord, ticked: false);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([GettingStartedKeys.Letterhead], _stored.TickedSteps());
    }

    [Fact]
    public async Task Unticking_With_No_Row_Writes_Nothing()
    {
        var result = await Send(GettingStartedKeys.PhoneApp, ticked: false);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(_added);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // The security code is not a matter of understanding, and an unknown key is nobody's step.
    [Theory]
    [InlineData(GettingStartedKeys.SecurityCode)]
    [InlineData("nimporte")]
    [InlineData(null)]
    public async Task The_Security_Code_And_Unknown_Keys_Are_Refused(string? step)
    {
        var result = await Send(step);

        Assert.True(result.IsFailure);
        Assert.Equal(GettingStartedTickRefusals.NotTickableCode, result.Code);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
