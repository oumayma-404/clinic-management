using System.Reflection;
using ClinicManagement.API.Middleware;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Clinics;
using ClinicManagement.Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// « Mode discret »'s server half: <see cref="MoneyMaskGateMiddleware"/> refuses a marked read while the cabinet has
/// hidden its money, and the set of marked reads is the one the spec names — no write, and no patient-scoped read.
/// </summary>
public class MoneyMaskGateMiddlewareTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed record Outcome(int Status, string Body, bool ReachedNext, int Reads);

    private static async Task<Outcome> InvokeAsync(
        bool moneyHidden,
        HiddenWhenMoneyMaskedAttribute? marker = null,
        string? query = null,
        TenantScopeKind scopeKind = TenantScopeKind.Clinic)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/billing/caisse";
        if (query is not null)
        {
            context.Request.QueryString = new QueryString(query);
        }

        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            marker is null ? EndpointMetadataCollection.Empty : new EndpointMetadataCollection(marker),
            "test"));

        var body = new MemoryStream();
        context.Response.Body = body;

        var scope = new Mock<ITenantScope>();
        scope.SetupGet(s => s.Kind).Returns(scopeKind);
        scope.SetupGet(s => s.ClinicId).Returns(scopeKind == TenantScopeKind.Clinic ? ClinicId : null);

        var reads = 0;
        var clinics = new Mock<IClinicRepository>();
        clinics.Setup(r => r.IsMoneyHiddenAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                reads++;
                return moneyHidden;
            });

        var reachedNext = false;
        var middleware = new MoneyMaskGateMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, scope.Object, clinics.Object);

        body.Position = 0;
        return new Outcome(context.Response.StatusCode, await new StreamReader(body).ReadToEndAsync(), reachedNext, reads);
    }

    // [AC-2] A marked read is refused with the code the browser branches on.
    [Fact]
    public async Task A_Marked_Read_Is_Refused_While_Hidden()
    {
        var outcome = await InvokeAsync(moneyHidden: true, new HiddenWhenMoneyMaskedAttribute());

        Assert.False(outcome.ReachedNext);
        Assert.Equal(StatusCodes.Status403Forbidden, outcome.Status);
        Assert.Contains($"\"code\":\"{ClinicMoneyMask.HiddenCode}\"", outcome.Body);
    }

    // [AC-4] Shown: the marked read goes through.
    [Fact]
    public async Task A_Marked_Read_Passes_While_Shown()
    {
        var outcome = await InvokeAsync(moneyHidden: false, new HiddenWhenMoneyMaskedAttribute());

        Assert.True(outcome.ReachedNext);
        Assert.Equal(1, outcome.Reads);
    }

    // Every other endpoint pays nothing — not even the one-column read.
    [Fact]
    public async Task An_Unmarked_Endpoint_Is_Never_Inspected()
    {
        var outcome = await InvokeAsync(moneyHidden: true);

        Assert.True(outcome.ReachedNext);
        Assert.Equal(0, outcome.Reads);
    }

    // [AC-2] `GET /api/invoices?patientId=` is one patient's file, and stays open.
    [Fact]
    public async Task A_Patient_Scoped_Read_Passes_While_Hidden()
    {
        var outcome = await InvokeAsync(
            moneyHidden: true, new HiddenWhenMoneyMaskedAttribute { UnlessQuery = "patientId" }, "?patientId=abc");

        Assert.True(outcome.ReachedNext);
        Assert.Equal(0, outcome.Reads);
    }

    // An empty `patientId=` is the clinic-wide list, not a patient's.
    [Fact]
    public async Task An_Empty_Patient_Filter_Is_Still_Clinic_Wide()
    {
        var outcome = await InvokeAsync(
            moneyHidden: true, new HiddenWhenMoneyMaskedAttribute { UnlessQuery = "patientId" }, "?patientId=");

        Assert.False(outcome.ReachedNext);
        Assert.Equal(StatusCodes.Status403Forbidden, outcome.Status);
    }

    // A caller that is not a cabinet has no « Mode discret » to obey.
    [Theory]
    [InlineData(TenantScopeKind.Unset)]
    [InlineData(TenantScopeKind.SystemWide)]
    public async Task A_Non_Clinic_Scope_Is_Never_Refused(TenantScopeKind kind)
    {
        var outcome = await InvokeAsync(moneyHidden: true, new HiddenWhenMoneyMaskedAttribute(), scopeKind: kind);

        Assert.True(outcome.ReachedNext);
        Assert.Equal(0, outcome.Reads);
    }

    // ---- which endpoints carry the marker ---------------------------------------------------------------

    /// <summary>The spec's « Refused while hidden » list, keyed <c>Controller.Action</c>.</summary>
    private static readonly HashSet<string> ExpectedMarked = new()
    {
        "Billing.ExportReceivables",
        "Billing.ExportCaisseLedger",
        "Billing.GetReceivables",
        "Billing.GetResteAPayer",
        "Billing.GetCaisseSummary",
        "Billing.GetCaisseLedger",
        "Billing.GetChequesDue",
        "Billing.ExportChequesDue",
        "Expenses.ExportExpenses",
        "Expenses.GetExpenses",
        "Expenses.GetRecurringExpenses",
        "Invoices.ExportInvoices",
        "Invoices.GetInvoices",
        "Invoices.GetRevenue",
    };

    private static IEnumerable<(string Key, MethodInfo Action)> Actions() =>
        typeof(ClinicManagement.API.Controllers.AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null)
                .Select(m => ($"{c.Name.Replace("Controller", string.Empty)}.{m.Name}", m)));

    private static IEnumerable<string> GetTemplates(MethodInfo action) =>
        action.GetCustomAttributes<HttpGetAttribute>(inherit: true).Select(a => a.Template ?? string.Empty);

    [Fact]
    public void The_Marked_Reads_Are_Exactly_The_Specified_Ones()
    {
        var marked = Actions()
            .Where(a => a.Action.GetCustomAttribute<HiddenWhenMoneyMaskedAttribute>() is not null)
            .Select(a => a.Key)
            .ToHashSet();

        Assert.Empty(marked.Except(ExpectedMarked).OrderBy(x => x));
        Assert.Empty(ExpectedMarked.Except(marked).OrderBy(x => x));
    }

    // « Changes what is displayed only »: a write is never refused, so recording a payment keeps working.
    [Fact]
    public void Every_Marked_Endpoint_Is_A_Get()
    {
        var notGet = Actions()
            .Where(a => a.Action.GetCustomAttribute<HiddenWhenMoneyMaskedAttribute>() is not null)
            .Where(a => !GetTemplates(a.Action).Any() || a.Action.GetCustomAttributes<HttpMethodAttribute>(true)
                .SelectMany(h => h.HttpMethods).Any(m => m != "GET"))
            .Select(a => a.Key)
            .ToList();

        Assert.Empty(notGet);
    }

    // Derived from the routes, so a new clinic-wide read under `billing/` or `expenses` cannot ship unguarded.
    [Fact]
    public void Every_Clinic_Wide_Billing_And_Expense_Read_Is_Marked()
    {
        var unmarked = Actions()
            .Where(a => a.Key.StartsWith("Expenses.", StringComparison.Ordinal)
                        || (a.Key.StartsWith("Billing.", StringComparison.Ordinal)
                            && GetTemplates(a.Action).Any(t => t.StartsWith("billing/", StringComparison.Ordinal))))
            .Where(a => GetTemplates(a.Action).Any())
            .Where(a => a.Action.GetCustomAttribute<HiddenWhenMoneyMaskedAttribute>() is null)
            .Select(a => a.Key)
            .ToList();

        Assert.Empty(unmarked);
    }
}
