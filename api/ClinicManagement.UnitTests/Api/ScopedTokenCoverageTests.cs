using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using ClinicManagement.API.Authorization;
using ClinicManagement.API.Controllers;
using ClinicManagement.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// <b>A scope-narrowed token opens exactly one endpoint, and adding a controller cannot widen that.</b>
///
/// <para><b>What this holds.</b> <c>POST /api/backup/archive-grants/token</c> is anonymous by necessity — an
/// unattended workstation's device secret is the credential — and it used to return an ordinary 30-minute
/// <b>clinic-admin token with the whole API surface</b>. A PC authorised only to fetch a nightly archive could
/// read every patient record, issue invoices and manage accounts. It now mints a token carrying
/// <c>clinic_scope</c>, and <c>ScopedTokenFilter</c> refuses it everywhere the scope is not named.</para>
///
/// <para>⚠️ <b>The value of this class is the fail-closed direction, and only a derived check can hold it.</b>
/// An <c>[InlineData]</c> table of today's endpoints stays green for ever no matter how many new ones are
/// written; what has to stay true is that the set of actions accepting a scoped token is <b>exactly</b> the
/// reviewed one — asserted in both directions, so an unused allowance is caught as loudly as a new hole.</para>
/// </summary>
public class ScopedTokenCoverageTests
{
    /// <summary>
    /// Every action that may be reached by a scope-narrowed token, and the scope it accepts. Asserted equal to
    /// the compiled controllers in both directions — the house style, and the half that stops an allowance
    /// outliving the endpoint it was written for.
    /// </summary>
    private static readonly Dictionary<string, string> ReviewedScopedActions = new(StringComparer.Ordinal)
    {
        // The four endpoints an unattended workstation's shell actually calls — verified against
        // desktop/ClinicManagement.DesktopShell's own request URLs, not guessed. Naming only the archive
        // download would have silently stopped the file mirror and the coffre report on the day this shipped.
        ["Backup.DownloadArchive"] = LocalAuthScopes.ClinicArchive,
        ["Backup.GetFileManifest"] = LocalAuthScopes.ClinicArchive,
        ["Backup.ReportVaultCopy"] = LocalAuthScopes.ClinicArchive,
        ["PatientFiles.DownloadFile"] = LocalAuthScopes.ClinicArchive,
    };

    [Fact]
    public void Exactly_The_Reviewed_Actions_Accept_A_Scoped_Token()
    {
        var declared = ScopedActions()
            .ToDictionary(a => a.Name, a => string.Join(",", a.Scopes), StringComparer.Ordinal);

        Assert.Equal(
            ReviewedScopedActions.OrderBy(e => e.Key, StringComparer.Ordinal).ToList(),
            declared.OrderBy(e => e.Key, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Non-vacuity: reflection fails <b>open</b>, and a renamed attribute or a moved namespace would leave this
    /// class green for ever while enumerating nothing — how <c>SystemWideCallerCoverageTests</c>' console-verb
    /// branch matched nothing for two whole features.
    /// </summary>
    [Fact]
    public void The_Scan_Still_Sees_The_Controllers()
    {
        Assert.True(ControllerActions().Count > 100, "The controller scan has stopped seeing the API assembly.");
        Assert.NotEmpty(ScopedActions());
    }

    /// <summary>
    /// ⚠️ <b>The load-bearing case.</b> The filter must refuse an action that has <i>not</i> named the scope,
    /// with no attribute and no decision from that action's author — that is what makes a controller written
    /// next month unreachable rather than accidentally open.
    /// </summary>
    [Fact]
    public void An_Endpoint_That_Names_No_Scope_Refuses_A_Scoped_Token()
    {
        var context = Authorizing(
            scope: LocalAuthScopes.ClinicArchive,
            action: typeof(ProbeController).GetMethod(nameof(ProbeController.NamesNothing))!);

        new ScopedTokenFilter().OnAuthorization(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    /// <summary>An action naming a <i>different</i> scope is not a loophole either.</summary>
    [Fact]
    public void An_Endpoint_Naming_Another_Scope_Refuses_This_One()
    {
        var context = Authorizing(
            scope: LocalAuthScopes.ClinicArchive,
            action: typeof(ProbeController).GetMethod(nameof(ProbeController.NamesAnother))!);

        new ScopedTokenFilter().OnAuthorization(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public void The_Endpoint_That_Names_The_Scope_Lets_It_Through()
    {
        var context = Authorizing(
            scope: LocalAuthScopes.ClinicArchive,
            action: typeof(ProbeController).GetMethod(nameof(ProbeController.NamesTheArchive))!);

        new ScopedTokenFilter().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    /// <summary>
    /// And the other half, which matters just as much: an <b>ordinary</b> token carries no scope claim at all
    /// and this filter must be invisible to it. A filter that refused unscoped tokens would take every screen
    /// in the product off the air, which is a far worse failure than the one it exists to prevent.
    /// </summary>
    [Fact]
    public void An_Ordinary_Token_Is_Untouched_Everywhere()
    {
        foreach (var method in new[]
                 {
                     typeof(ProbeController).GetMethod(nameof(ProbeController.NamesNothing))!,
                     typeof(ProbeController).GetMethod(nameof(ProbeController.NamesAnother))!,
                 })
        {
            var context = Authorizing(scope: null, action: method);

            new ScopedTokenFilter().OnAuthorization(context);

            Assert.Null(context.Result);
        }
    }

    private static AuthorizationFilterContext Authorizing(string? scope, MethodInfo action)
    {
        var claims = scope is null
            ? Array.Empty<Claim>()
            : new[] { new Claim(LocalAuthClaims.Scope, scope) };

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
        };

        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = action,
            ControllerName = "Probe",
            ActionName = action.Name,
        };

        return new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), descriptor),
            new List<IFilterMetadata>());
    }

    private static List<(string Name, IReadOnlyList<string> Scopes)> ScopedActions() =>
        ControllerActions()
            .Select(a => (
                Name: $"{Strip(a.DeclaringType!.Name)}.{a.Name}",
                Attribute: a.GetCustomAttribute<AcceptsScopedTokenAttribute>(inherit: true)))
            .Where(a => a.Attribute is not null)
            .Select(a => (a.Name, a.Attribute!.Scopes))
            .ToList();

    private static List<MethodInfo> ControllerActions() =>
        typeof(BackupController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && t is { IsAbstract: false, IsPublic: true })
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName)
            .ToList();

    private static string Strip(string controllerTypeName) =>
        controllerTypeName.EndsWith("Controller", StringComparison.Ordinal)
            ? controllerTypeName[..^"Controller".Length]
            : controllerTypeName;

    /// <summary>
    /// A stand-in for « any controller ». Deliberately <b>not</b> a real one: the point of the three cases above
    /// is the behaviour an action gets for free by writing nothing, and a real controller could pass them for
    /// reasons of its own.
    /// </summary>
    private sealed class ProbeController : ControllerBase
    {
        public IActionResult NamesNothing() => Ok();

        [AcceptsScopedToken("some-other-purpose")]
        public IActionResult NamesAnother() => Ok();

        [AcceptsScopedToken(LocalAuthScopes.ClinicArchive)]
        public IActionResult NamesTheArchive() => Ok();
    }
}
