using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Authorization;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The act-catalogue and VLC write endpoints are per-clinic reference-data mutations and must be admin-only
/// (FR-5.3/5.4). This pins the <c>[Authorize(Policy = AdminOnly)]</c> attribute on each write action so a
/// future refactor cannot silently open them to any authenticated user. Reads deliberately carry no
/// admin policy (any authenticated user may read the catalogue/VLC/estimate).
///
/// <para>The VLC actions moved onto <c>DentalActsController</c> with feature single-act-catalogue; the
/// guarantee is unchanged, and the class it points at is now the only catalogue controller for acts.</para>
/// </summary>
public class DentalActCatalogAuthorizationTests
{
    private static readonly string[] AdminOnlyActions =
    {
        nameof(DentalActsController.CreateAct),
        nameof(DentalActsController.UpdateAct),
        nameof(DentalActsController.DeactivateAct),
        nameof(DentalActsController.ReactivateAct),
        nameof(DentalActsController.ConfirmData),
        nameof(DentalActsController.UpdateLetterValue),
    };

    private static readonly string[] AnyAuthenticatedReadActions =
    {
        nameof(DentalActsController.GetDentalActs),
        nameof(DentalActsController.GetLetterValues),
        nameof(DentalActsController.GetReimbursementEstimate),
        nameof(DentalActsController.GetReimbursementEstimates),
    };

    [Theory]
    [MemberData(nameof(AdminOnlyActionData))]
    public void Write_Endpoints_Require_AdminOnly(string action) // [FR-5.3/5.4]
    {
        var method = typeof(DentalActsController).GetMethod(action)!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.AdminOnly, authorize!.Policy);
    }

    [Theory]
    [MemberData(nameof(ReadActionData))]
    public void Read_Endpoints_Carry_No_Method_Level_Admin_Policy(string action) // [FR-5.3]
    {
        var method = typeof(DentalActsController).GetMethod(action)!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        // Reads inherit the class-level [Authorize] (any authenticated user); no per-method AdminOnly.
        Assert.True(authorize is null || authorize.Policy != AuthorizationPolicies.AdminOnly);
    }

    public static IEnumerable<object[]> AdminOnlyActionData() => AdminOnlyActions.Select(a => new object[] { a });
    public static IEnumerable<object[]> ReadActionData() => AnyAuthenticatedReadActions.Select(a => new object[] { a });
}
