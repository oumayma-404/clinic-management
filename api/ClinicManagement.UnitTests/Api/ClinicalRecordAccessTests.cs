using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Authorization;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The clinical record's access charter: <b>every role reads and records it; only admin or doctor deletes from
/// it.</b> Five controllers — fiches de soins, odontogramme, antécédents médicaux, antécédents familiaux and the
/// medical documents (ordonnance, certificat, lettre de liaison, bulletin CNAM, arrêt de travail).
///
/// <para><b>Why this reverses a recorded decision.</b> These controllers were <c>AdminOrDoctor</c> outright, so
/// « Dossiers médicaux » and « Documents » returned 403 for a secretary before they had touched anything — the
/// <em>reads</em> included. `adoption-qa-i` recorded that choice deliberately. It was reversed on a practising
/// dentist's account of who fills the record in, and because the old boundary was never true in the code it was
/// drawn around: <c>PUT /api/patients/{id}</c> is <c>AnyClinicRole</c> and writes <c>Allergies</c>,
/// <c>MedicalHistory</c>, <c>Notes</c> and <c>ImportantNotes</c>, while <c>POST /api/patients</c> inserts
/// <c>PatientMedicalHistory</c> rows outright. Reception could always type a patient's medical history through
/// « Modifier » and was refused reading it one tab over.</para>
///
/// <para><b>Why this test exists rather than trusting the attributes.</b> `ControllerAuthorizationCoverageTests`
/// asserts every action resolves to *some* named policy and that the defined set equals the applied set — it
/// cannot fail on an action resolving to the *wrong* one. The predecessor of that guard stayed green for the
/// product's whole life while 33 endpoints carried a bare <c>[Authorize]</c>, because it only asserted a policy
/// existed. This class states the charter as data, and
/// <see cref="Every_Action_Of_These_Controllers_Is_Classified_By_This_Test"/> fails on a new action nobody has
/// decided about — the shape `TreatmentPlansControllerAuthorizationTests` established.</para>
/// </summary>
public class ClinicalRecordAccessTests
{
    /// <summary>
    /// Reading and recording the patient's clinical file. Effective policy must be <c>AnyClinicRole</c> — either
    /// inherited from the class or stated on the action.
    /// </summary>
    private static readonly (Type Controller, string Action)[] AnyClinicRoleActions =
    {
        // Fiches de soins. The GET is listed first on purpose: it is the one `adoption-qa-i` explicitly forked on
        // and decided the other way, and it is what made the whole tab a refusal rather than a read-only view.
        (typeof(DentalRecordsController), nameof(DentalRecordsController.GetDentalRecords)),
        (typeof(DentalRecordsController), nameof(DentalRecordsController.CreateDentalRecord)),
        (typeof(DentalRecordsController), nameof(DentalRecordsController.UpdateDentalRecord)),

        // The odontogram. RemoveCondition is here rather than with the deletes: it removes a charted *diagnosis*
        // — charting's own undo, for the tooth someone just mis-clicked — and cannot touch a treatment entry,
        // which is edited through the fiche that produced it.
        (typeof(OdontogramController), nameof(OdontogramController.GetOdontogram)),
        (typeof(OdontogramController), nameof(OdontogramController.DiagnoseTooth)),
        (typeof(OdontogramController), nameof(OdontogramController.RemoveCondition)),

        (typeof(PatientMedicalHistoryController), nameof(PatientMedicalHistoryController.GetMedicalHistory)),
        (typeof(PatientMedicalHistoryController), nameof(PatientMedicalHistoryController.CreateMedicalHistory)),
        (typeof(PatientMedicalHistoryController), nameof(PatientMedicalHistoryController.UpdateMedicalHistory)),

        (typeof(PatientFamilyHistoryController), nameof(PatientFamilyHistoryController.GetFamilyHistory)),
        (typeof(PatientFamilyHistoryController), nameof(PatientFamilyHistoryController.CreateFamilyHistory)),
        (typeof(PatientFamilyHistoryController), nameof(PatientFamilyHistoryController.UpdateFamilyHistory)),

        // Medical documents — authoring included. Safe only because the cachet + n° d'ordre are resolved from the
        // practitioner the editor **named** (`IssuingDoctorId`), not from the caller: before that, a document
        // authored by anyone with no Doctor record rendered with no practitioner identity at all, silently.
        (typeof(MedicalDocumentsController), nameof(MedicalDocumentsController.GetDocuments)),
        (typeof(MedicalDocumentsController), nameof(MedicalDocumentsController.GetDocument)),
        (typeof(MedicalDocumentsController), nameof(MedicalDocumentsController.CreateDocument)),
        (typeof(MedicalDocumentsController), nameof(MedicalDocumentsController.UpdateDocument)),
        (typeof(MedicalDocumentsController), nameof(MedicalDocumentsController.GeneratePdf)),
        (typeof(MedicalDocumentsController), nameof(MedicalDocumentsController.GeneratePdfForDownload)),
    };

    /// <summary>
    /// Deleting from the clinical file. Pinned in detail by
    /// <see cref="ClinicalRecordDeletionAuthorizationTests"/>; listed here so the drift guard below sees a
    /// complete classification of every action on these five controllers.
    /// </summary>
    private static readonly (Type Controller, string Action)[] AdminOrDoctorActions =
    {
        (typeof(DentalRecordsController), nameof(DentalRecordsController.DeleteDentalRecord)),
        (typeof(MedicalDocumentsController), nameof(MedicalDocumentsController.DeleteDocument)),
        (typeof(PatientMedicalHistoryController), nameof(PatientMedicalHistoryController.DeleteMedicalHistory)),
        (typeof(PatientFamilyHistoryController), nameof(PatientFamilyHistoryController.DeleteFamilyHistory)),
    };

    private static readonly Type[] ClinicalControllers =
    {
        typeof(DentalRecordsController),
        typeof(OdontogramController),
        typeof(PatientMedicalHistoryController),
        typeof(PatientFamilyHistoryController),
        typeof(MedicalDocumentsController),
    };

    [Fact]
    public void Reading_And_Recording_The_Clinical_Record_Admits_Every_Clinic_Role()
    {
        foreach (var (controller, action) in AnyClinicRoleActions)
        {
            Assert.Equal(
                AuthorizationPolicies.AnyClinicRole,
                EffectivePolicy(controller, action));
        }
    }

    [Fact]
    public void Deleting_From_The_Clinical_Record_Requires_AdminOrDoctor()
    {
        foreach (var (controller, action) in AdminOrDoctorActions)
        {
            Assert.Equal(
                AuthorizationPolicies.AdminOrDoctor,
                EffectivePolicy(controller, action));
        }
    }

    /// <summary>
    /// Every one of the five controllers carries <c>AnyClinicRole</c> at class level, so a new action is open by
    /// default and has to be tightened on purpose. Stated separately from the per-action assertions because it is
    /// the thing that makes the delete attributes the *only* gate — and therefore the thing whose removal is the
    /// silent failure.
    /// </summary>
    [Theory]
    [InlineData(typeof(DentalRecordsController))]
    [InlineData(typeof(OdontogramController))]
    [InlineData(typeof(PatientMedicalHistoryController))]
    [InlineData(typeof(PatientFamilyHistoryController))]
    [InlineData(typeof(MedicalDocumentsController))]
    public void Controller_Carries_AnyClinicRole_At_Class_Level(Type controller)
    {
        var authorize = controller.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.AnyClinicRole, authorize!.Policy);
    }

    /// <summary>
    /// The drift guard: a new action on any of these controllers must appear in one of the two tables above.
    /// Without it this class only fails on rows somebody remembered to write, which is exactly how the original
    /// coverage test stayed green through 33 unprotected endpoints.
    /// </summary>
    [Fact]
    public void Every_Action_Of_These_Controllers_Is_Classified_By_This_Test()
    {
        var classified = AnyClinicRoleActions.Concat(AdminOrDoctorActions)
            .Select(entry => $"{entry.Controller.Name}.{entry.Action}")
            .ToHashSet();

        var declared = ClinicalControllers
            .SelectMany(controller => Actions(controller).Select(a => $"{controller.Name}.{a.Name}"))
            .ToHashSet();

        Assert.Empty(declared.Except(classified));

        // And the reverse, so a renamed or deleted action cannot leave a stale row silently asserting nothing.
        Assert.Empty(classified.Except(declared));
    }

    /// <summary>
    /// No action of the clinical surface may be anonymous. `ControllerAuthorizationCoverageTests` owns this
    /// solution-wide, but these five controllers serve a patient's PHI and the assertion is cheap here.
    /// </summary>
    [Fact]
    public void No_Clinical_Endpoint_Is_Anonymous()
    {
        foreach (var controller in ClinicalControllers)
        {
            foreach (var action in Actions(controller))
            {
                Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            }
        }
    }

    /// <summary>The action's own non-empty policy if it states one, else the controller's.</summary>
    private static string? EffectivePolicy(Type controller, string action)
    {
        var method = controller.GetMethod(action)
            ?? throw new InvalidOperationException($"{controller.Name}.{action} no longer exists.");

        var onAction = method.GetCustomAttribute<AuthorizeAttribute>();
        if (onAction != null && !string.IsNullOrEmpty(onAction.Policy))
        {
            return onAction.Policy;
        }

        return controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy;
    }

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() == null);
}
