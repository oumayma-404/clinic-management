using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using ClinicManagement.API.Middleware;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// [AC-1.6] A deactivated console account, a stale token version or a pending one-time password is refused on the
/// <b>next request</b> — not at token expiry.
///
/// <para><b>Why this middleware has to exist at all, and why the tests below are the only thing holding it.</b>
/// Console requests skip <c>AccountStateMiddleware</c> and <c>LocalAuthEnforcementMiddleware</c>, the product's
/// only two per-request readers of live account state, because a console principal has no <c>User</c> row for
/// either to resolve. Skipping them is correct and it leaves a hole: without this middleware the AC-8.5
/// deactivation command would leave a revoked account with full cross-cabinet read access until its token
/// expired. Nothing else in the build can see that — the deactivation still succeeds, the row still says
/// « désactivé », and the token still works.</para>
/// </summary>
public class PlatformAccountStateTests
{
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static PlatformAccount Account(int tokenVersion = 0, bool active = true, bool mustChange = false)
    {
        var account = PlatformAccount.Create("ops@editeur.tn", "Ops", "hash");
        // Create() starts at TokenVersion 0 with MustChangePassword true; SetPassword clears the flag and bumps.
        if (!mustChange)
        {
            account.SetPassword("hash", mustChangePassword: false);
        }

        while (account.TokenVersion < tokenVersion)
        {
            account.SetPassword("hash", mustChangePassword: mustChange);
        }

        if (!active)
        {
            account.Deactivate();
        }

        return account;
    }

    private static ClaimsPrincipal ConsolePrincipal(Guid accountId, int tokenVersion) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, accountId.ToString()),
            new Claim(LocalAuthClaims.TokenVersion, tokenVersion.ToString(CultureInfo.InvariantCulture)),
            new Claim(IPlatformSessionContext.TokenKindClaim, IPlatformSessionContext.PlatformTokenKind)
        }, "TestConsole"));

    /// <summary>
    /// A console request. <paramref name="schemeYields"/> is what <c>AuthenticateAsync(PlatformConsole)</c> hands
    /// back — the production path, since <c>UseAuthentication</c> populates only the clinic scheme. Every context
    /// carries the stub, so a test that sets no principal exercises the same call production makes.
    /// </summary>
    private static DefaultHttpContext ConsoleRequest(
        Guid? accountId,
        int tokenVersion,
        string path = "/api/platform/clinics",
        ClaimsPrincipal? schemeYields = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new StubAuthenticationService(schemeYields));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (accountId is not null)
        {
            context.User = ConsolePrincipal(accountId.Value, tokenVersion);
        }

        return context;
    }

    /// <summary>Answers one scheme and refuses to be asked about anything else.</summary>
    private sealed class StubAuthenticationService : IAuthenticationService
    {
        private readonly ClaimsPrincipal? _principal;

        public StubAuthenticationService(ClaimsPrincipal? principal) => _principal = principal;

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            Assert.Equal(PlatformConsoleScheme.Name, scheme);

            return Task.FromResult(_principal is null
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(_principal, scheme!)));
        }

        public Task ChallengeAsync(HttpContext c, string? s, AuthenticationProperties? p) =>
            throw new NotSupportedException();

        public Task ForbidAsync(HttpContext c, string? s, AuthenticationProperties? p) =>
            throw new NotSupportedException();

        public Task SignInAsync(HttpContext c, string? s, ClaimsPrincipal u, AuthenticationProperties? p) =>
            throw new NotSupportedException();

        public Task SignOutAsync(HttpContext c, string? s, AuthenticationProperties? p) =>
            throw new NotSupportedException();
    }

    private static async Task<(int Status, string? Code, bool Continued)> Invoke(
        DefaultHttpContext context, PlatformAccount? stored)
    {
        var accounts = new Mock<IPlatformAccountRepository>();
        accounts.Setup(a => a.GetForStateCheckAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var continued = false;
        var middleware = new PlatformAccountStateMiddleware(_ =>
        {
            continued = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, accounts.Object);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = new StreamReader(context.Response.Body).ReadToEnd();
        string? code = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            var json = JsonDocument.Parse(body);
            code = json.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
        }

        return (context.Response.StatusCode, code, continued);
    }

    [Fact]
    public async Task An_active_account_with_a_current_token_passes()
    {
        var account = Account(tokenVersion: 1);
        var result = await Invoke(ConsoleRequest(AccountId, account.TokenVersion), account);

        Assert.True(result.Continued);
        Assert.Equal(StatusCodes.Status200OK, result.Status);
    }

    // [AC-1.6] Deactivation takes effect on the NEXT request, with the token still perfectly valid by signature
    // and lifetime. 401 rather than 403 for the same reason the clinic side does it: the credential is no longer
    // valid, not merely insufficient.
    [Fact]
    public async Task A_deactivated_account_is_refused_401_on_its_very_next_request()
    {
        var account = Account(tokenVersion: 1);
        var tokenVersion = account.TokenVersion;
        account.Deactivate();

        var result = await Invoke(ConsoleRequest(AccountId, tokenVersion), account);

        Assert.False(result.Continued);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.Status);
    }

    [Fact]
    public async Task A_stale_token_version_is_refused_401_on_the_next_request()
    {
        var account = Account(tokenVersion: 1);
        var oldVersion = account.TokenVersion;
        account.SetPassword("new-hash", mustChangePassword: false);

        var result = await Invoke(ConsoleRequest(AccountId, oldVersion), account);

        Assert.False(result.Continued);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.Status);
    }

    // Fails closed on a token that cannot prove its version — the rule that retires any token minted before this
    // claim existed.
    [Fact]
    public async Task A_token_with_no_version_claim_is_refused()
    {
        var account = Account(tokenVersion: 1);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/platform/clinics";
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, AccountId.ToString()),
            new Claim(IPlatformSessionContext.TokenKindClaim, IPlatformSessionContext.PlatformTokenKind)
        }, "TestConsole"));

        var result = await Invoke(context, account);

        Assert.False(result.Continued);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.Status);
    }

    // [AC-8.1] The one-time password is one-time: every console route but the password change is refused while
    // the flag stands, which is what makes « the verb prints a one-time password » true of anything.
    [Fact]
    public async Task A_pending_one_time_password_blocks_every_route_but_the_password_change()
    {
        var account = Account(mustChange: true);

        var blocked = await Invoke(ConsoleRequest(AccountId, account.TokenVersion), account);
        Assert.False(blocked.Continued);
        Assert.Equal(StatusCodes.Status403Forbidden, blocked.Status);
        Assert.Equal("must_change_password", blocked.Code);

        var allowed = await Invoke(
            ConsoleRequest(AccountId, account.TokenVersion, PlatformAccountStateMiddleware.ChangePasswordPath),
            account);
        Assert.True(allowed.Continued);
    }

    // [AC-1.6] The case that was broken in production for six parts, and that every test above missed by setting
    // context.User itself: UseAuthentication populates only the CLINIC scheme, and a console token fails that one
    // by design (AC-1.4), so on a real request this middleware sees an unauthenticated principal. It must
    // authenticate the console scheme itself — otherwise it passes everything through and a deactivated account
    // keeps reading every cabinet. Verified over the wire before it was fixed: deactivating an account and
    // re-calling /api/platform/summary with the same token answered 200.
    [Fact]
    public async Task A_revoked_account_is_refused_even_though_only_the_console_scheme_can_authenticate_it()
    {
        var account = Account(tokenVersion: 1);
        var tokenVersion = account.TokenVersion;
        account.Deactivate();

        var context = ConsoleRequest(
            accountId: null,                                             // as UseAuthentication leaves it
            tokenVersion: 0,
            schemeYields: ConsolePrincipal(AccountId, tokenVersion));     // as the pinned scheme resolves it

        var result = await Invoke(context, account);

        Assert.False(result.Continued);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.Status);
    }

    // The same blindness applied to the forced password change, which is what made « one-time » true of nothing:
    // the bootstrap password stayed a working credential for the whole token lifetime.
    [Fact]
    public async Task A_pending_one_time_password_is_caught_on_a_scheme_authenticated_request()
    {
        var account = Account(mustChange: true);

        var context = ConsoleRequest(
            accountId: null,
            tokenVersion: 0,
            schemeYields: ConsolePrincipal(AccountId, account.TokenVersion));

        var result = await Invoke(context, account);

        Assert.False(result.Continued);
        Assert.Equal(StatusCodes.Status403Forbidden, result.Status);
        Assert.Equal("must_change_password", result.Code);
    }

    // A clinic request must not pay for any of this — it has no console principal, and re-reading one on every
    // call in the product for a population of two or three accounts would be a lookup nobody needs.
    [Fact]
    public async Task A_clinic_request_is_untouched()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/patients";
        context.Response.Body = new MemoryStream();

        var accounts = new Mock<IPlatformAccountRepository>();
        var continued = false;
        await new PlatformAccountStateMiddleware(_ => { continued = true; return Task.CompletedTask; })
            .InvokeAsync(context, accounts.Object);

        Assert.True(continued);
        accounts.Verify(
            a => a.GetForStateCheckAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The anonymous console routes (login, enrolment, recovery) carry no principal, so they pass through — the
    // policy is what refuses a non-console principal on an authenticated one.
    [Fact]
    public async Task An_anonymous_console_request_passes_through()
    {
        var result = await Invoke(ConsoleRequest(null, 0, "/api/platform/auth/login"), stored: null);

        Assert.True(result.Continued);
    }

    // ---------------------------------------------------------------- ordering, against Program.cs's own source

    /// <summary>
    /// ⚠️ <b>Ordering is asserted against the composition root's source, because a mis-ordered middleware is
    /// exactly as broken as an absent one and nothing else in the build can see it.</b> Registered before
    /// <c>UseAuthentication</c> there is no principal to read, so this middleware would pass every request
    /// through and the revocation would silently not exist. Same technique and same reason as
    /// <c>MigrationLockTests</c> and <c>AccountStateEnforcementTests</c>.
    /// </summary>
    [Fact]
    public void The_console_state_check_runs_after_authentication_and_the_scope_before_the_clinic_one()
    {
        var source = File.ReadAllText(ProgramPath());

        var authentication = source.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var consoleState = source.IndexOf(nameof(PlatformAccountStateMiddleware), StringComparison.Ordinal);
        var consoleScope = source.IndexOf(nameof(PlatformTenantScopeMiddleware), StringComparison.Ordinal);
        var clinicScope = source.IndexOf(nameof(TenantScopeMiddleware) + ">", StringComparison.Ordinal);

        Assert.True(authentication > 0 && consoleState > 0 && consoleScope > 0 && clinicScope > 0,
            "Program.cs no longer registers one of the middlewares this ordering depends on.");

        Assert.True(consoleState > authentication,
            "PlatformAccountStateMiddleware must run AFTER UseAuthentication — before it there is no principal, "
            + "so it would pass every console request through and AC-1.6's revocation would not exist.");

        Assert.True(consoleScope < clinicScope,
            "PlatformTenantScopeMiddleware must run BEFORE TenantScopeMiddleware: ITenantScope is "
            + "single-assignment, and a console request that reached a handler Unset reads ZERO ROWS with no "
            + "error — a portfolio indistinguishable from one where every cabinet is idle (EC-12).");
    }

    /// <summary>
    /// Found through <see cref="CallerFilePathAttribute"/>, not <c>AppContext.BaseDirectory</c>: this suite is
    /// routinely built to a scratch OutDir outside the repository (the Smart App Control workaround), so a
    /// path relative to the binaries resolves to nothing.
    /// </summary>
    private static string ProgramPath([CallerFilePath] string thisFile = "")
    {
        var api = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "..", "ClinicManagement.API", "Program.cs"));

        Assert.True(File.Exists(api), $"Program.cs not found at {api}");
        return api;
    }
}
