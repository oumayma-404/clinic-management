using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Authorization;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// [AC-P2.22 / adjacent defect A-12] Pins the role policy on the two destructive clinical endpoints that a
/// « Supprimer » button now makes reachable from the UI.
/// <para>
/// Both controllers carried a class-level <c>[Authorize]</c> only, so **any** authenticated clinic member — a
/// secretary included — could destroy a fiche de soins or an ordonnance. That was unreachable while no UI
/// offered the action; adding the button is what turns it into a real hole, which is why the gate and the
/// button had to land together.
/// </para>
/// <para>
/// <c>AdminOrDoctor</c> rather than <c>DoctorOnly</c>: an admin is the account that cleans up after a
/// mis-keyed record, and it is the class the repo already uses for reversing/altering a document another
/// aggregate depends on (invoice cancel, devis amend, mark/un-mark an act).
/// </para>
/// </summary>
public class ClinicalRecordDeletionAuthorizationTests
{
    // Deleting a fiche detaches invoice lines and returns devis acts to « prévu » — the same class of
    // consequence as amending the plan itself.
    [Fact]
    public void Deleting_A_Dental_Record_Requires_AdminOrDoctor() // [AC-P2.22]
    {
        AssertAdminOrDoctor(
            typeof(DentalRecordsController),
            nameof(DentalRecordsController.DeleteDentalRecord));
    }

    // A medical document is a signed clinical instrument issued in a practitioner's name.
    [Fact]
    public void Deleting_A_Medical_Document_Requires_AdminOrDoctor() // [AC-P2.22]
    {
        AssertAdminOrDoctor(
            typeof(MedicalDocumentsController),
            nameof(MedicalDocumentsController.DeleteDocument));
    }

    // The class-level gate still has to be there: the method policy authorizes the *role*, and without an
    // authenticated principal there is no role to read. In Local mode the fail-closed FallbackPolicy would
    // cover an omission; Cloud's fallback is null, so this is the only thing keeping Cloud honest.
    [Theory]
    [InlineData(typeof(DentalRecordsController))]
    [InlineData(typeof(MedicalDocumentsController))]
    public void Controller_Requires_Authentication_At_Class_Level(Type controller) // [AC-P2.22]
    {
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
    }

    private static void AssertAdminOrDoctor(Type controller, string action)
    {
        var method = controller.GetMethod(action)
            ?? throw new InvalidOperationException($"{controller.Name}.{action} no longer exists.");
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.AdminOrDoctor, authorize!.Policy);
    }
}
