using System.Security.Claims;
using System.Text.RegularExpressions;
using ClinicManagement.API.Middleware;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Authorization.Handlers;
using ClinicManagement.Application.Common.Authorization.Requirements;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.UnitTests.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// Account state and role are read from the <b>database</b>, on every profile
/// (<c>SECURITY_REVIEW_2026-08</c>, findings B and C).
///
/// <para>
/// Both defects shared one cause: <c>CloudBrowser</c> had no per-request reader of live account state, so the JWT
/// was the only source of truth for « is this account active » and « what role does it hold ». Deactivating a user
/// therefore did nothing, and a demoted admin kept <c>AdminOnly</c> for ever — while the UI reported both actions
/// as successful.
/// </para>
/// </summary>
public class AccountStateEnforcementTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // ---- AccountStateMiddleware ------------------------------------------------------------------

    private static (HttpContext Context, Mock<IUserRepository> Users) Request(User? account)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var users = new Mock<IUserRepository>();
        if (account is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, account.Id) }, "test"));
            users.Setup(r => r.GetByAuth0SubAsync(account.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(account);
        }

        return (context, users);
    }

    private static User Active(string role) =>
        User.CreateLocalUser(ClinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    [Fact]
    public async Task A_Deactivated_Account_Is_Refused()
    {
        var account = Active(User.RoleDoctor);
        account.Deactivate();
        var (context, users) = Request(account);

        var nextCalled = false;
        var middleware = new AccountStateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, users.Object);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task An_Active_Account_Passes_And_Publishes_Its_Database_Role()
    {
        var (context, users) = Request(Active(User.RoleSecretary));

        var nextCalled = false;
        var middleware = new AccountStateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, users.Object);

        Assert.True(nextCalled);
        Assert.Equal(User.RoleSecretary, context.Items[EffectiveRole.HttpContextItemKey]);
    }

    /// <summary>
    /// A principal with no <c>User</c> row is the ordinary state of Cloud onboarding — <c>POST /clinics</c> and
    /// <c>/clinics/join</c> are reached in exactly that state — so it must pass, publishing no role.
    /// </summary>
    [Fact]
    public async Task A_Caller_With_No_Account_Row_Passes_Unjudged()
    {
        var (context, users) = Request(null);

        var nextCalled = false;
        var middleware = new AccountStateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, users.Object);

        Assert.True(nextCalled);
        Assert.False(context.Items.ContainsKey(EffectiveRole.HttpContextItemKey));
    }

    // ---- RoleAuthorizationHandler ----------------------------------------------------------------

    private static AuthorizationHandlerContext Authorize(string[] allowed, string? claimRole, string? dbRole)
    {
        var identity = claimRole is null
            ? new ClaimsIdentity("test")
            : new ClaimsIdentity(new[] { new Claim("https://clinic-management.com/role", claimRole) }, "test");

        var httpContext = new DefaultHttpContext();
        if (dbRole is not null)
        {
            httpContext.Items[EffectiveRole.HttpContextItemKey] = dbRole;
        }

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var principal = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            new[] { new RoleRequirement(allowed) }, principal, resource: null);

        new RoleAuthorizationHandler(accessor.Object)
            .HandleAsync(context).GetAwaiter().GetResult();

        return context;
    }

    /// <summary>
    /// The defect this reverses: the token still said <c>admin</c> because Auth0's <c>app_metadata</c> was never
    /// updated, so a demoted user passed <c>AdminOnly</c> on every request — new tokens included.
    /// </summary>
    [Fact]
    public void A_Demoted_Admin_Is_Refused_Even_While_The_Token_Still_Says_Admin()
    {
        var context = Authorize(
            allowed: new[] { User.RoleAdmin },
            claimRole: User.RoleAdmin,
            dbRole: User.RoleSecretary);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public void A_Promoted_User_Is_Granted_Before_Their_Token_Catches_Up()
    {
        var context = Authorize(
            allowed: new[] { User.RoleAdmin },
            claimRole: User.RoleSecretary,
            dbRole: User.RoleAdmin);

        Assert.True(context.HasSucceeded);
    }

    /// <summary>
    /// The claim fall-back is required rather than lenient: Cloud onboarding reaches policies with no row yet.
    /// </summary>
    [Fact]
    public void The_Claim_Is_Used_When_The_Caller_Has_No_Account_Row()
    {
        var context = Authorize(
            allowed: new[] { User.RoleAdmin },
            claimRole: User.RoleAdmin,
            dbRole: null);

        Assert.True(context.HasSucceeded);
    }

    // ---- The registration property -------------------------------------------------------------

    /// <summary>
    /// ⚠️ <b>The two properties that make the fix real</b>, asserted against <c>Program.cs</c> itself: the
    /// middleware is registered with <b>no deployment-capability gate</b>, and it runs <b>before</b>
    /// <c>UseAuthorization</c> — otherwise the role it publishes is set too late for the handler to read, and the
    /// whole thing silently reverts to trusting the claim.
    /// </summary>
    [Fact]
    public void The_Account_State_Gate_Is_Unconditional_And_Runs_Before_Authorization()
    {
        var program = File.ReadAllText(
            Path.Combine(SolutionSources.Root().FullName, "ClinicManagement.API", "Program.cs"));

        var registration = program.IndexOf("UseMiddleware<ClinicManagement.API.Middleware.AccountStateMiddleware>", StringComparison.Ordinal);
        var authorization = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);

        Assert.True(registration > 0, "AccountStateMiddleware is no longer registered in Program.cs.");
        Assert.True(
            registration < authorization,
            "AccountStateMiddleware must run BEFORE UseAuthorization, or RoleAuthorizationHandler reads a role "
            + "that has not been published yet and silently falls back to the JWT claim.");

        // No `if (profile.…)` may sit between the preceding statement and this registration.
        var precedingBlock = program[..registration];
        var lastBrace = precedingBlock.LastIndexOf('{');
        var guardedByCapability = Regex.IsMatch(
            precedingBlock[Math.Max(0, lastBrace - 200)..], @"if\s*\(\s*profile\.\w+\s*\)\s*\{\s*$");

        Assert.False(
            guardedByCapability,
            "AccountStateMiddleware is gated on a deployment capability. « A deactivated account cannot use the "
            + "API » is not a property of a topology — that gate is exactly how CloudBrowser ended up with no "
            + "account-state enforcement at all.");
    }
}
