using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Relay;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>
/// The lease's net (<c>clinic-pc-copy</c> D15): what each side still writes for a cabinet whose saves it does not hold.
/// The PC's half is decided before any SQL and is held here over real tracked entries on a model-only context; the
/// cloud's half reads the current relay on the save's own transaction (SQL — the rig and the CI job), and its rule is
/// <c>ClinicWriteLease</c>'s, tested with it. <see cref="ClinicWriteFence"/> is the same answer for the jobs.
/// </summary>
public class RelayFenceTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none")
            .Options);

    private static ClinicChangeCapture Capture(DeploymentKind kind, bool holding = false, bool retired = false) =>
        new(DeploymentProfile.For(kind), null, new FixedLocal(holding, retired));

    private static Expense AnExpense() =>
        new(Guid.NewGuid(), ClinicId, new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc), "Loyer", 100m, PaymentMethod.Cash);

    private static User AUser() => User.CreateLocalUser(ClinicId, User.RoleAdmin, "a@cabinet.tn", "hash", "Salma B.");

    // ---- the PC de secours ----------------------------------------------------------------------------------------

    // [D15] A PC that does not hold the cabinet's saves writes nothing of the cabinet's — whatever path the save came by.
    [Fact]
    public void A_Following_Pc_Refuses_A_Cabinet_Write()
    {
        using var db = Context();
        db.Add(AnExpense());

        var refused = Assert.Throws<ClinicFencedException>(() => Capture(DeploymentKind.ClinicRelay).EnsureThisSideMayWrite(db));

        Assert.Equal(RelayRefusals.StandbyCode, refused.Code);
        Assert.Equal(RelayRefusals.Standby, refused.Message);
    }

    [Fact]
    public void A_Retired_Pc_Says_It_Is_Retired()
    {
        using var db = Context();
        db.Add(AnExpense());

        var refused = Assert.Throws<ClinicFencedException>(
            () => Capture(DeploymentKind.ClinicRelay, retired: true).EnsureThisSideMayWrite(db));

        Assert.Equal(RelayRefusals.RetiredCode, refused.Code);
    }

    // Signing in on a following PC is the one write it keeps: its traces are each side's own.
    [Fact]
    public void A_Sign_In_On_A_Following_Pc_Is_Accepted()
    {
        using var db = Context();
        var user = AUser();
        db.Attach(user);
        user.RecordFailedLogin();
        user.RecordSuccessfulLogin();

        Capture(DeploymentKind.ClinicRelay).EnsureThisSideMayWrite(db);
    }

    // …and only its traces: an account change on the same row is not a sign-in.
    [Fact]
    public void An_Account_Change_On_A_Following_Pc_Is_Refused()
    {
        using var db = Context();
        var user = AUser();
        db.Attach(user);
        user.RecordSuccessfulLogin();
        user.Deactivate();

        Assert.Throws<ClinicFencedException>(() => Capture(DeploymentKind.ClinicRelay).EnsureThisSideMayWrite(db));
    }

    [Fact]
    public void A_Pc_In_Charge_Writes_As_Usual()
    {
        using var db = Context();
        db.Add(AnExpense());

        Capture(DeploymentKind.ClinicRelay, holding: true).EnsureThisSideMayWrite(db);
    }

    // A PC that cannot tell whether it holds the saves must not write over the copy.
    [Fact]
    public void A_Pc_With_No_Local_Status_Counts_As_Not_Holding()
    {
        using var db = Context();
        db.Add(AnExpense());

        Assert.Throws<ClinicFencedException>(
            () => new ClinicChangeCapture(DeploymentProfile.For(DeploymentKind.ClinicRelay)).EnsureThisSideMayWrite(db));
    }

    // The cloud's half needs the relay row (SQL, in the save's transaction); the LAN server has no fence at all.
    [Theory]
    [InlineData(DeploymentKind.HostedMultiTenant)]
    [InlineData(DeploymentKind.SelfHostedLan)]
    public void The_Pc_Half_Never_Refuses_Anywhere_Else(DeploymentKind kind)
    {
        using var db = Context();
        db.Add(AnExpense());

        Capture(kind).EnsureThisSideMayWrite(db);
    }

    // ---- the FR-11 table set -----------------------------------------------------------------------------------------

    // Every table the fenced cloud keeps writing is a table the PC carries — an entry naming anything else is a typo
    // that would exempt nothing.
    [Fact]
    public void Every_Table_The_Fenced_Cloud_Keeps_Writing_Is_A_Carried_Table()
    {
        using var db = Context();
        var plan = ClinicRelayScope.For(db.Model);

        foreach (var (table, reason) in RelayFence.AllowedOnFencedCloud)
        {
            Assert.True(plan.Find(table) is not null, $"{table} ({reason}) is not carried: the exemption exempts nothing.");
            Assert.True(reason.Length >= 20, $"{table}: the reason is too thin to review.");
        }
    }

    // No clinical record and no money: the fenced cloud must never be able to write what the PC is recording.
    [Theory]
    [InlineData(nameof(Patient))]
    [InlineData(nameof(Appointment))]
    [InlineData(nameof(DentalRecord))]
    [InlineData(nameof(Invoice))]
    [InlineData(nameof(Payment))]
    [InlineData(nameof(TreatmentPlan))]
    [InlineData(nameof(Expense))]
    [InlineData(nameof(Notification))]
    [InlineData(nameof(Clinic))]
    public void A_Fenced_Cloud_Never_Writes_The_Cabinets_Record(string table)
    {
        Assert.False(RelayFence.AllowedOnFencedCloud.ContainsKey(table));
    }

    // ---- the jobs' question ------------------------------------------------------------------------------------------

    private static ClinicRelay ArmedSilentRelay(TimeSpan ago)
    {
        var sent = DateTime.UtcNow - ago;
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", sent.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, null, sent.AddDays(-1));
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, sent), armed: true);
        return relay;
    }

    [Theory]
    [InlineData(75, true)]
    [InlineData(20, false)]
    public async Task The_Cloud_Fences_A_Cabinet_Whose_Armed_Pc_Fell_Silent(int secondsAgo, bool expected)
    {
        var relays = new Mock<IClinicRelayRepository>();
        relays.Setup(r => r.GetCurrentForClinicAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArmedSilentRelay(TimeSpan.FromSeconds(secondsAgo)));
        var fence = new ClinicWriteFence(DeploymentProfile.For(DeploymentKind.HostedMultiTenant), relays.Object, new FixedLocal(false, false));

        Assert.Equal(expected, await fence.RefusesAsync(ClinicId));
        Assert.Equal(expected, await fence.RefusesAsync(ClinicId));
        relays.Verify(r => r.GetCurrentForClinicAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_Pc_De_Secours_Refuses_Unless_It_Holds_The_Saves(bool holding, bool expected)
    {
        var relays = new Mock<IClinicRelayRepository>(MockBehavior.Strict);
        var fence = new ClinicWriteFence(DeploymentProfile.For(DeploymentKind.ClinicRelay), relays.Object, new FixedLocal(holding, false));

        Assert.Equal(expected, await fence.RefusesAsync(ClinicId));
    }

    [Fact]
    public async Task A_Lan_Server_Fences_Nothing_And_Reads_Nothing()
    {
        var relays = new Mock<IClinicRelayRepository>(MockBehavior.Strict);
        var fence = new ClinicWriteFence(DeploymentProfile.For(DeploymentKind.SelfHostedLan), relays.Object, new FixedLocal(false, false));

        Assert.False(await fence.RefusesAsync(ClinicId));
    }

    // [Deviation 88] A job registered on the PC de secours writes into the cut's own log, so it must ask the fence:
    // otherwise it would write while the copy follows the cloud, and the feed would overwrite it (or the net refuse it).
    [Fact]
    public void Every_Job_The_Pc_Runs_During_A_Cut_Asks_The_Write_Fence()
    {
        var api = Path.Combine(ClinicManagement.UnitTests.Common.SolutionSources.Root().FullName, "ClinicManagement.API");
        var program = File.ReadAllText(Path.Combine(api, "Program.cs"));
        var jobs = System.Text.RegularExpressions.Regex.Matches(program,
                @"if \([^)]*profile\.RunsCutJobs[^)]*\)\s*\{\s*RecurringJob\.AddOrUpdate<ClinicManagement\.API\.BackgroundJobs\.(\w+)>")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.Equal(new[] { "AppointmentProgressJob", "MonthlyExpenseJob" }, jobs.Order());
        foreach (var job in jobs)
        {
            var source = File.ReadAllText(Path.Combine(api, "BackgroundJobs", job + ".cs"));
            Assert.True(source.Contains("_fence.RefusesAsync("), $"{job} runs on the PC de secours and never asks IClinicWriteFence.");
        }
    }

    private sealed class FixedLocal(bool holding, bool retired) : IRelayLocalStatus
    {
        public bool IsRetired => retired;
        public DateTime? RetiredAtUtc => null;
        public bool IsHolding => holding;
    }
}
