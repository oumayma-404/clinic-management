using System.Data.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// The caller's account is read once per request: a second ask returns the tracked row only when it carries both
/// includes (<c>features/performance-caching</c> 1.2). No database is ever reached — an attempt throws.
/// </summary>
public class UserRepositoryTrackedCallerTests
{
    private sealed class DatabaseTouchedException : Exception;

    private sealed class RefuseToConnect : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            throw new DatabaseTouchedException();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw new DatabaseTouchedException();
    }

    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=never_connected;Username=none;Password=none")
            .AddInterceptors(new RefuseToConnect())
            .Options);

    private static User Track(ApplicationDbContext db, bool clinicLoaded = true, bool codesLoaded = true)
    {
        var user = User.CreateLocalUser(Guid.NewGuid(), User.RoleAdmin, "admin@cabinet.tn", "HASH", "Admin Cabinet");
        var entry = db.Users.Attach(user);
        entry.Reference(u => u.Clinic).IsLoaded = clinicLoaded;
        entry.Collection(u => u.RecoveryCodes).IsLoaded = codesLoaded;
        return user;
    }

    private static async Task AssertReachesTheDatabase(Func<Task> read)
    {
        var thrown = await Assert.ThrowsAnyAsync<Exception>(read);

        for (Exception? ex = thrown; ex is not null; ex = ex.InnerException)
        {
            if (ex is DatabaseTouchedException)
            {
                return;
            }
        }

        Assert.Fail($"Expected a database read, got {thrown.GetType().Name}: {thrown.Message}");
    }

    [Fact]
    public async Task A_Caller_Tracked_With_Both_Includes_Is_Returned_Without_A_Query()
    {
        await using var db = Context();
        var user = Track(db);

        var found = await new UserRepository(db).GetByAuth0SubAsync(user.Id);

        Assert.Same(user, found);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_Caller_Tracked_Without_One_Include_Is_Read_Again(bool clinicLoaded, bool codesLoaded)
    {
        await using var db = Context();
        var user = Track(db, clinicLoaded, codesLoaded);

        await AssertReachesTheDatabase(() => new UserRepository(db).GetByAuth0SubAsync(user.Id));
    }

    [Fact]
    public async Task A_Caller_Marked_For_Deletion_Is_Not_Handed_Back()
    {
        await using var db = Context();
        var user = Track(db);
        db.Users.Remove(user);

        await AssertReachesTheDatabase(() => new UserRepository(db).GetByAuth0SubAsync(user.Id));
    }

    [Fact]
    public async Task An_Untracked_Caller_Is_Read_From_The_Database()
    {
        await using var db = Context();

        await AssertReachesTheDatabase(() => new UserRepository(db).GetByAuth0SubAsync("local|nobody"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Change_Detection_Is_Left_As_The_Caller_Set_It(bool autoDetect)
    {
        await using var db = Context();
        db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        var user = Track(db);

        await new UserRepository(db).GetByAuth0SubAsync(user.Id);

        Assert.Equal(autoDetect, db.ChangeTracker.AutoDetectChangesEnabled);
    }
}
