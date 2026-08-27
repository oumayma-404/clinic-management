using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Authorization;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The medication catalog write endpoints are global reference-data mutations and must be admin-only. This
/// pins the <c>[Authorize(Policy = AdminOnly)]</c> attribute on each write action so a future refactor cannot
/// silently open them to any authenticated user. Reads carry no admin policy (any authenticated user may
/// read the catalog for the ordonnance picker), and nothing on this controller is anonymous.
/// </summary>
public class MedicationsControllerAuthorizationTests
{
    private static readonly string[] AdminOnlyActions =
    {
        nameof(MedicationsController.CreateMedication),
        nameof(MedicationsController.UpdateMedication),
        nameof(MedicationsController.DeactivateMedication),
        nameof(MedicationsController.ConfirmData),
    };

    private static readonly string[] AnyAuthenticatedReadActions =
    {
        nameof(MedicationsController.GetMedications),
    };

    [Theory]
    [MemberData(nameof(AdminOnlyActionData))]
    public void Write_Endpoints_Require_AdminOnly(string action) // [AC-3]
    {
        var method = typeof(MedicationsController).GetMethod(action)!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.AdminOnly, authorize!.Policy);
    }

    [Theory]
    [MemberData(nameof(ReadActionData))]
    public void Read_Endpoints_Carry_No_Method_Level_Admin_Policy(string action) // [AC-3]
    {
        var method = typeof(MedicationsController).GetMethod(action)!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        // Reads inherit the class-level [Authorize] (any authenticated user); no per-method AdminOnly.
        Assert.True(authorize is null || authorize.Policy != AuthorizationPolicies.AdminOnly);
    }

    [Fact]
    public void Controller_Requires_Authentication_At_Class_Level() // [AC-3]
    {
        var authorize = typeof(MedicationsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
    }

    [Fact]
    public void No_Endpoint_Is_Anonymous() // [AC-3] the anonymous surface is unchanged
    {
        var anonymous = typeof(MedicationsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(anonymous);
    }

    public static IEnumerable<object[]> AdminOnlyActionData() => AdminOnlyActions.Select(a => new object[] { a });
    public static IEnumerable<object[]> ReadActionData() => AnyAuthenticatedReadActions.Select(a => new object[] { a });
}
