using System.Security.Claims;
using ClinicManagement.API.Middleware;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The middleware that makes US-2's inversion work (multi-tenant-cloud review finding 32 — it had **no** test at
/// all, on the single point of the request path the whole tenant-scope design hangs from).
///
/// <para><b>Why each case matters, since none of them can fail loudly in production.</b> The query filters now
/// return <b>nothing</b> for an unset scope, so every defect here is silent: a scope taken from the JWT claim
/// instead of the database gives zero rows and no error the moment a token is stale (amendment C3′ — in Cloud the
/// claim is written by an Auth0 Action that does not live in this repository); and a middleware that *refused* an
/// unresolvable caller instead of leaving the scope <c>Unset</c> would break onboarding outright, because a Cloud
/// principal has no <c>User</c> row until they join a clinic.</para>
///
/// <para><c>RequestAccount</c> is exercised through this middleware rather than directly — it is
/// <c>internal</c>, and its contract (« resolved once, cached even when null ») is only meaningful as the
/// behaviour two middlewares share.</para>
/// </summary>
public class TenantScopeMiddlewareTests
{
    private static readonly Guid ClinicFromDatabase = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicFromStaleClaim = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string Subject = "local|11111111-1111-1111-1111-111111111111";

    private readonly Mock<IUserRepository> _users = new();

    private static HttpContext Anonymous() => new DefaultHttpContext();

    /// <summary>An authenticated caller. The clinic claim is deliberately set to a DIFFERENT clinic.</summary>
    private static HttpContext Authenticated(string subject = Subject)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, subject),
                new Claim("https://clinic-management.com/clinic_id", ClinicFromStaleClaim.ToString()),
            },
            authenticationType: "Test"));
        return context;
    }

    private static User Account(Guid clinicId) =>
        User.CreateLocalUser(clinicId, User.RoleSecretary, "amel@cabinet.tn", "hash", "Amel Ben Salah");

    private static TenantScope Scope() => new(NullLogger<TenantScope>.Instance);

    private async Task<TenantScope> RunAsync(HttpContext context)
    {
        var scope = Scope();
        var middleware = new TenantScopeMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, scope, _users.Object);
        return scope;
    }

    private void GivenAccount(User? account) =>
        _users.Setup(r => r.GetByAuth0SubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

    /// <summary>
    /// [C3′] The load-bearing assertion of the whole class: the scope is the clinic on the **User row**, not the one
    /// in the token. The claim here names a different clinic, so a claim-reading implementation passes every other
    /// test in this file and fails only this one.
    /// </summary>
    [Fact]
    public async Task The_scope_is_the_db_resolved_clinic_and_never_the_jwt_claim()
    {
        GivenAccount(Account(ClinicFromDatabase));

        var scope = await RunAsync(Authenticated());

        Assert.Equal(TenantScopeKind.Clinic, scope.Kind);
        Assert.Equal(ClinicFromDatabase, scope.ClinicId);
        Assert.NotEqual(ClinicFromStaleClaim, scope.ClinicId);
    }

    [Fact]
    public async Task An_authenticated_caller_with_no_user_row_leaves_the_scope_unset()
    {
        // The ordinary state of a Cloud principal who has not joined a clinic yet. Refusing here would break
        // POST /clinics and /clinics/join, which work only because User and Clinic carry no query filter.
        GivenAccount(null);

        var scope = await RunAsync(Authenticated());

        Assert.Equal(TenantScopeKind.Unset, scope.Kind);
        Assert.Null(scope.ClinicId);
    }

    [Fact]
    public async Task An_anonymous_request_leaves_the_scope_unset_without_touching_the_repository()
    {
        var scope = await RunAsync(Anonymous());

        Assert.Equal(TenantScopeKind.Unset, scope.Kind);
        // Not merely tidiness: this middleware runs on every request including the proxied web pages, so a lookup
        // on the anonymous path would be a database round trip per page asset.
        _users.Verify(
            r => r.GetByAuth0SubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task An_authenticated_caller_with_no_subject_claim_leaves_the_scope_unset()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), authenticationType: "Test"));

        var scope = await RunAsync(context);

        Assert.Equal(TenantScopeKind.Unset, scope.Kind);
        _users.Verify(
            r => r.GetByAuth0SubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// [RequestAccount] « Resolved once, cached even when null » — what lets this middleware and
    /// <c>LocalAuthEnforcementMiddleware</c> each read the account without assuming which one runs first, at the
    /// cost of one query rather than two on every authenticated request.
    /// </summary>
    [Fact]
    public async Task Two_resolutions_in_one_request_issue_one_query()
    {
        GivenAccount(Account(ClinicFromDatabase));
        var context = Authenticated();

        var scope = Scope();
        var middleware = new TenantScopeMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, scope, _users.Object);
        // A second pass over the same HttpContext stands in for the sibling middleware reading the same account.
        await middleware.InvokeAsync(context, Scope(), _users.Object);

        _users.Verify(
            r => r.GetByAuth0SubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_null_account_is_cached_too_so_the_miss_is_not_repeated()
    {
        GivenAccount(null);
        var context = Authenticated();

        var middleware = new TenantScopeMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, Scope(), _users.Object);
        await middleware.InvokeAsync(context, Scope(), _users.Object);

        _users.Verify(
            r => r.GetByAuth0SubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_pipeline_continues_in_every_case()
    {
        GivenAccount(null);
        var reached = false;
        var middleware = new TenantScopeMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(Anonymous(), Scope(), _users.Object);

        Assert.True(reached);
    }
}
