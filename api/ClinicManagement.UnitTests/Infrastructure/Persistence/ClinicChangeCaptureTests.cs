using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// The change log's entry point (<c>clinic-pc-copy</c> D1, D2): which saves it looks at, on which deployments, and the
/// two refusals that keep a change from reaching the database unseen. The cursor advance and the clinic lookups are
/// SQL, so they belong to the CI <c>relay-copy</c> job; what is decided before any SQL is held here, over real tracked
/// entries on a model-only context.
/// </summary>
public class ClinicChangeCaptureTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static ApplicationDbContext Context(ClinicChangeCapture? capture = null) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none")
            .Options, null, null, capture);

    private static ClinicChangeCapture Capture(DeploymentKind kind) => new(DeploymentProfile.For(kind));

    private static Expense AnExpense() =>
        new(Guid.NewGuid(), ClinicId, new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc), "Loyer", 100m, PaymentMethod.Cash);

    // [D2] The cloud publishes, the PC records its own writes for the return; the LAN install does neither.
    [Theory]
    [InlineData(DeploymentKind.HostedMultiTenant, true)]
    [InlineData(DeploymentKind.ClinicRelay, true)]
    [InlineData(DeploymentKind.SelfHostedLan, false)]
    public void Capture_Looks_At_A_Save_Only_Where_A_Feed_Exists(DeploymentKind kind, bool expected)
    {
        var capture = Capture(kind);
        using var db = Context();
        db.Add(AnExpense());

        Assert.Equal(expected, capture.HasCandidates(db));
    }

    [Fact]
    public void A_Save_Of_Only_Excluded_Tables_Has_No_Candidates()
    {
        var capture = Capture(DeploymentKind.HostedMultiTenant);
        using var db = Context();
        db.Add(new SessionFamily("local|u", "hash", new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc)));

        Assert.False(capture.HasCandidates(db));
    }

    [Fact]
    public void An_Unchanged_Row_Is_Not_A_Candidate()
    {
        var capture = Capture(DeploymentKind.HostedMultiTenant);
        using var db = Context();
        db.Attach(AnExpense());

        Assert.False(capture.HasCandidates(db));
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public void A_Modified_Or_Deleted_Row_Is_A_Candidate(EntityState state)
    {
        var capture = Capture(DeploymentKind.HostedMultiTenant);
        using var db = Context();
        var expense = AnExpense();
        db.Attach(expense);
        db.Entry(expense).State = state;

        Assert.True(capture.HasCandidates(db));
    }

    // A composite-keyed child (who read which bell row) is carried too.
    [Fact]
    public void A_Composite_Keyed_Child_Is_A_Candidate()
    {
        var capture = Capture(DeploymentKind.HostedMultiTenant);
        using var db = Context();
        db.Add(new NotificationRead(Guid.NewGuid(), "local|u"));

        Assert.True(capture.HasCandidates(db));
    }

    // [D1] The log rides the save's transaction; appending outside one would commit a change with no seq.
    [Fact]
    public async Task Appending_Outside_A_Transaction_Is_Refused()
    {
        var capture = Capture(DeploymentKind.HostedMultiTenant);
        using var db = Context();
        db.Add(AnExpense());

        await Assert.ThrowsAsync<InvalidOperationException>(() => capture.AppendAsync(db, CancellationToken.None));
    }

    [Fact]
    public async Task Appending_On_A_Deployment_With_No_Feed_Does_Nothing()
    {
        var capture = Capture(DeploymentKind.SelfHostedLan);
        using var db = Context();
        db.Add(AnExpense());

        await capture.AppendAsync(db, CancellationToken.None);

        Assert.Empty(db.ChangeTracker.Entries<ClinicChange>());
    }

    // The synchronous save cannot take the cursor's lock, so a clinic write through it is refused rather than missed.
    [Fact]
    public void The_Synchronous_Save_Refuses_A_Captured_Write()
    {
        using var db = Context(Capture(DeploymentKind.HostedMultiTenant));
        db.Add(AnExpense());

        var refusal = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("SaveChangesAsync", refusal.Message);
    }
}
