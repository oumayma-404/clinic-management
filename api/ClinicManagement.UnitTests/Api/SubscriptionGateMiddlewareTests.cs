using ClinicManagement.API.Middleware;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The subscription gate (<c>clinic-subscription</c> Part B, US-4 / FR-3 / FR-11).
///
/// <para><b>Most of this class asserts what the gate must NOT refuse, and that is where its value is.</b> It sits in
/// front of every controller on the hosted deployment, so a wrong « refuse » verdict does not degrade a feature — it
/// takes a working cabinet's ability to record anything at all, mid-consultation. The over-refusal cases (a GET, an
/// export, a caller who is not a cabinet, a deployment that does not enforce, a still-valid entitlement) therefore
/// outnumber the three refusals.</para>
///
/// <para>Exercised through a real <see cref="DefaultHttpContext"/> with fabricated endpoint metadata rather than
/// mocked, because the three things most likely to be wrong — the <b>method</b> check, the <b>path</b> scoping and
/// the <b>response shape</b> — are none of them visible in an interface.</para>
/// </summary>
public class SubscriptionGateMiddlewareTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>Decades in the past, so « expired » is true whenever the suite runs — the gate reads the real clock.</summary>
    private static readonly DateTime LongExpired = new(2020, 1, 15);

    /// <summary>Decades ahead, for the same reason in the other direction.</summary>
    private static readonly DateTime FarFuture = new(2099, 12, 31);

    private const string WritePath = "/api/appointments";

    // ---- fixtures ---------------------------------------------------------------------------------------

    private static ClinicSubscription EndingOn(DateTime endsOn)
    {
        var subscription = ClinicSubscription.For(ClinicId, new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        subscription.RecomputeFrom(new[]
        {
            SubscriptionPeriod.Create(
                ClinicId,
                SubscriptionPeriodKind.Paid,
                recordedOnClinicDay: new DateTime(2019, 1, 1),
                recordedAtUtc: new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                explicitEndsOn: endsOn)
        });

        return subscription;
    }

    private static ClinicSubscription OpenEnded()
    {
        var subscription = ClinicSubscription.For(ClinicId, new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        subscription.RecomputeFrom(new[]
        {
            SubscriptionPeriod.OpenEnded(
                ClinicId,
                SubscriptionPeriodKind.Grandfathered,
                new DateTime(2019, 1, 1),
                new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        });

        return subscription;
    }

    private static ClinicSubscription Suspended(DateTime endsOn)
    {
        var subscription = EndingOn(endsOn);
        subscription.Suspend("Impayé", "vendor", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        return subscription;
    }

    // ---- harness ---------------------------------------------------------------------------------------

    private sealed record Outcome(int Status, string Body, bool ReachedNext, int RepositoryReads);

    private static async Task<Outcome> InvokeAsync(
        ClinicSubscription? subscription,
        string path = WritePath,
        string method = "POST",
        bool requiresSubscription = true,
        TenantScopeKind scopeKind = TenantScopeKind.Clinic,
        bool exempt = false)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;

        if (exempt)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new AllowsWithoutSubscriptionAttribute("test")),
                "exempt"));
        }

        var body = new MemoryStream();
        context.Response.Body = body;

        var policy = new Mock<ISubscriptionPolicy>();
        policy.SetupGet(p => p.RequiresSubscription).Returns(requiresSubscription);

        var scope = new Mock<ITenantScope>();
        scope.SetupGet(s => s.Kind).Returns(scopeKind);
        scope.SetupGet(s => s.ClinicId).Returns(scopeKind == TenantScopeKind.Clinic ? ClinicId : null);

        var reads = 0;
        var repository = new Mock<IClinicSubscriptionRepository>();
        repository
            .Setup(r => r.GetByClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                reads++;
                return subscription;
            });

        var reachedNext = false;
        var middleware = new SubscriptionGateMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, policy.Object, scope.Object, repository.Object);

        body.Position = 0;
        return new Outcome(
            context.Response.StatusCode, await new StreamReader(body).ReadToEndAsync(), reachedNext, reads);
    }

    // ---- what must never be refused ---------------------------------------------------------------------

    // [AC-4.1][AC-4.2][AC-4.3] Every read passes, and it passes WITHOUT the gate reading the entitlement at all —
    // which is what makes « an expired cabinet keeps all of its records » structural rather than an allow-list of
    // readable endpoints somebody has to keep complete. HEAD and OPTIONS ride along: a download's preflight and a
    // browser's CORS probe are not recording clinical work.
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task A_Read_Is_Never_Even_Inspected(string method)
    {
        var outcome = await InvokeAsync(EndingOn(LongExpired), method: method);

        Assert.True(outcome.ReachedNext);
        Assert.Equal(0, outcome.RepositoryReads);
    }

    // [AC-4.2][EC-9] The CSV exports are GETs, so they are covered by the rule above rather than by an exemption —
    // stated as its own case because « every export still works » is an acceptance criterion in its own right and
    // the reason it holds is worth being able to point at.
    [Theory]
    [InlineData("/api/patients/export")]
    [InlineData("/api/invoices/export")]
    [InlineData("/api/billing/caisse/ledger")]
    public async Task An_Export_Still_Works_Because_It_Is_A_Get(string path)
    {
        var outcome = await InvokeAsync(EndingOn(LongExpired), path, "GET");

        Assert.True(outcome.ReachedNext);
    }

    // [AC-7.1][AC-7.2][FR-11] On a deployment that does not enforce subscriptions the gate is inert — and reads
    // nothing, so SelfHostedLan and CloudBrowser pay not even one query for a feature they do not have.
    [Fact]
    public async Task Where_Subscriptions_Are_Not_Enforced_Nothing_Is_Refused()
    {
        var outcome = await InvokeAsync(EndingOn(LongExpired), requiresSubscription: false);

        Assert.True(outcome.ReachedNext);
        Assert.Equal(0, outcome.RepositoryReads);
    }

    // The front door also serves the web app in a self-hosted install, and the BFF routes sit outside /api. A 402
    // on a page would replace the French banner with raw JSON — the same scoping ClientVersionMiddleware needs.
    [Theory]
    [InlineData("/bff/auth/token")]
    [InlineData("/patients")]
    [InlineData("/_next/static/chunk.js")]
    [InlineData("/hub/clinic")]
    public async Task A_Path_Outside_Api_Is_Never_Refused(string path)
    {
        var outcome = await InvokeAsync(EndingOn(LongExpired), path, "POST");

        Assert.True(outcome.ReachedNext);
        Assert.Equal(0, outcome.RepositoryReads);
    }

    // [FR-3] A caller who is not a cabinet PASSES rather than meeting subscription_missing. They have no entitlement
    // to find, and that fault code would otherwise land on precisely the vendor-console endpoints whose purpose is
    // to END a refusal. Authentication already covers the anonymous case.
    [Theory]
    [InlineData(TenantScopeKind.Unset)]
    [InlineData(TenantScopeKind.SystemWide)]
    public async Task A_Caller_Who_Is_Not_A_Cabinet_Passes(TenantScopeKind scopeKind)
    {
        var outcome = await InvokeAsync(null, scopeKind: scopeKind);

        Assert.True(outcome.ReachedNext);
        Assert.Equal(0, outcome.RepositoryReads);
    }

    // [FR-3] An endpoint that declared itself exempt is not even looked up.
    [Fact]
    public async Task An_Exempt_Endpoint_Is_Not_Refused()
    {
        var outcome = await InvokeAsync(EndingOn(LongExpired), exempt: true);

        Assert.True(outcome.ReachedNext);
        Assert.Equal(0, outcome.RepositoryReads);
    }

    // [AC-1.4] A valid entitlement writes, and so does an open-ended one — every grandfathered cabinet and every
    // cabinet on a non-enforcing deployment holds one, so a bug here would refuse the entire installed base.
    [Fact]
    public async Task A_Valid_Entitlement_Writes()
    {
        Assert.True((await InvokeAsync(EndingOn(FarFuture))).ReachedNext);
        Assert.True((await InvokeAsync(OpenEnded())).ReachedNext);
    }

    // ---- the three refusals ------------------------------------------------------------------------------

    // [AC-4.4] The refusal names the date in dd/MM/yyyy and points at « Abonnement » — and says what still works
    // BEFORE what does not, because it is read mid-consultation by somebody wondering if the file is gone.
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task An_Expired_Cabinet_Is_Refused_With_402_And_The_Date(string method)
    {
        var outcome = await InvokeAsync(EndingOn(LongExpired), method: method);

        Assert.False(outcome.ReachedNext);
        Assert.Equal(StatusCodes.Status402PaymentRequired, outcome.Status);
        Assert.Contains(SubscriptionRefusals.RequiredCode, outcome.Body);
        Assert.Contains("15/01/2020", outcome.Body);
        Assert.Contains("Abonnement", outcome.Body);
    }

    // [EC-11] Suspension outranks the date, INCLUDING one already past. Telling a suspended practice its
    // subscription lapsed sends it to pay again, which costs them money and changes nothing.
    [Fact]
    public async Task A_Suspended_Cabinet_Reads_Suspended_Not_Expired()
    {
        var outcome = await InvokeAsync(Suspended(LongExpired));

        Assert.Equal(StatusCodes.Status402PaymentRequired, outcome.Status);
        Assert.Contains(SubscriptionRefusals.SuspendedCode, outcome.Body);
        Assert.DoesNotContain(SubscriptionRefusals.RequiredCode, outcome.Body);
        Assert.DoesNotContain("expiré", outcome.Body);
    }

    // [EC-11] And it outranks a date still in the FUTURE too — the case where AllowsWrites would otherwise be true.
    [Fact]
    public async Task A_Suspended_Cabinet_Is_Refused_Even_While_Its_Date_Is_Valid()
    {
        var outcome = await InvokeAsync(Suspended(FarFuture));

        Assert.False(outcome.ReachedNext);
        Assert.Contains(SubscriptionRefusals.SuspendedCode, outcome.Body);
    }

    // [EC-6] No entitlement row at all is a DISTINCT code: a fault on our side, not a lapse on theirs, so the
    // sentence must not tell the cabinet to renew something it has nothing of.
    [Fact]
    public async Task A_Cabinet_With_No_Entitlement_Is_Refused_Under_Its_Own_Code()
    {
        var outcome = await InvokeAsync(null);

        Assert.False(outcome.ReachedNext);
        Assert.Equal(StatusCodes.Status402PaymentRequired, outcome.Status);
        Assert.Contains(SubscriptionRefusals.MissingCode, outcome.Body);
        Assert.Equal(1, outcome.RepositoryReads);
    }

    // [AC-4.5] The refusal is machine-recognisable and it is NOT 401 or 403: the client reads those as « signed
    // out » / « no rights », and either would end a session the cabinet must keep.
    [Fact]
    public async Task The_Refusal_Never_Looks_Like_A_Sign_Out_Or_A_Rights_Error()
    {
        var outcome = await InvokeAsync(EndingOn(LongExpired));

        Assert.Equal(402, outcome.Status);
        Assert.Contains("\"code\"", outcome.Body);
        Assert.Contains("\"error\"", outcome.Body);
    }

    // [EC-10] A secretary meets the same French refusal as anybody else — the gate reads no role, so « vous n'avez
    // pas les droits » is unreachable here by construction. Asserted as the body being byte-identical across two
    // requests that differ only in who made them.
    [Fact]
    public async Task Every_Role_Meets_The_Same_Refusal()
    {
        var first = await InvokeAsync(EndingOn(LongExpired));
        var second = await InvokeAsync(EndingOn(LongExpired));

        Assert.Equal(first.Body, second.Body);
        Assert.DoesNotContain("droits", first.Body);
    }

    // ---- the registration property ------------------------------------------------------------------------

    /// <summary>
    /// ⚠️ <b>The one thing this gate can get catastrophically wrong while compiling and passing every case above</b>,
    /// asserted against <c>Program.cs</c> itself on <c>AccountStateEnforcementTests</c>' precedent.
    ///
    /// <para>Registered <i>before</i> <c>LocalAuthEnforcementMiddleware</c>, the gate answers <b>402</b> to two
    /// requests that are not about money at all: a <b>revoked</b> token (401) and a pending <b>forced password
    /// change</b> (403 <c>must_change_password</c>). The first tells a deactivated colleague the subscription
    /// lapsed; the second routes a user to « Abonnement » instead of to <c>/change-password</c>, leaving the
    /// account stuck in both directions. Nothing else in the build can see it — the middleware is correct in
    /// isolation and only its <i>position</i> is wrong.</para>
    /// </summary>
    [Fact]
    public void The_Gate_Runs_After_Token_State_Enforcement_And_Before_The_Controllers()
    {
        var program = File.ReadAllText(Path.Combine(
            ClinicManagement.UnitTests.Common.SolutionSources.Root().FullName,
            "ClinicManagement.API",
            "Program.cs"));

        var tokenState = program.IndexOf(
            "UseMiddleware<ClinicManagement.API.Middleware.LocalAuthEnforcementMiddleware>",
            StringComparison.Ordinal);
        var gate = program.IndexOf(
            "UseMiddleware<ClinicManagement.API.Middleware.SubscriptionGateMiddleware>",
            StringComparison.Ordinal);
        var controllers = program.IndexOf("app.MapControllers();", StringComparison.Ordinal);

        Assert.True(gate > 0, "SubscriptionGateMiddleware is no longer registered in Program.cs.");
        Assert.True(tokenState > 0, "LocalAuthEnforcementMiddleware is no longer registered in Program.cs.");

        Assert.True(
            tokenState < gate,
            "The subscription gate must run AFTER LocalAuthEnforcementMiddleware, or a 402 masks the 401 of a "
            + "revoked token and the 403 must_change_password of a pending forced password change.");

        Assert.True(
            gate < controllers,
            "The subscription gate must run BEFORE MapControllers, or a refused write reaches its handler and "
            + "commits — AC-4.10's « nothing about the refusal is silent », inverted.");
    }
}
