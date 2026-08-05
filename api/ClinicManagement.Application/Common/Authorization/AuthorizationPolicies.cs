using ClinicManagement.Application.Common.Authorization.Requirements;
using ClinicManagement.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// The product's whole authorization vocabulary — four policies, each applied somewhere.
///
/// <para><b>Why only four, and why these.</b> The set used to be five, three of which
/// (<c>DoctorOnly</c>, <c>SecretaryOnly</c>, <c>DoctorOrSecretary</c>) had <b>zero usages</b> for the entire
/// life of the product while 33 endpoints carried a bare <c>[Authorize]</c> — any authenticated user, any role.
/// They stayed green because the guard test only asserted that a policy *existed*. The replacement guard
/// asserts the defined set equals the <b>applied</b> set in both directions, which is only satisfiable with no
/// exemption list if every policy here is genuinely used — so an unapplied policy is deleted rather than
/// parked. A policy nobody applies is not a capability, it is a comment that compiles.</para>
///
/// <para><b>The two distinctions that carry the vocabulary</b> are both <see cref="AdminOrDoctor"/> vs
/// <see cref="AnyClinicRole"/>:</para>
/// <list type="bullet">
///   <item><b>Money — per-patient yes, clinic-wide no.</b> A secretary must be able to take a payment and read
///   <em>one patient's</em> balance; that is reception's job. They must not read the clinic's aggregates — la
///   caisse et son extrait, les créances, le chiffre d'affaires, le tableau de bord — nor perform the corrective
///   operations (annuler une note, annuler un paiement, émettre un avoir).</item>
///   <item><b>The clinical record — write yes, erase no.</b> Reception reads and records the patient file
///   (fiches de soins, odontogramme, antécédents, documents médicaux) and cannot delete from it.</item>
/// </list>
///
/// <para>⚠️ <b>The second one is a reversal, and it is worth knowing why.</b> The clinical record used to sit
/// entirely behind <see cref="AdminOrDoctor"/> under the heading « clinical authorship and clinical free text ».
/// That description was never true of the code around it: <c>PUT /api/patients/{id}</c> is
/// <see cref="AnyClinicRole"/> and writes <c>Allergies</c>, <c>MedicalHistory</c>, <c>Notes</c> and
/// <c>ImportantNotes</c>, and <c>POST /api/patients</c> inserts <c>PatientMedicalHistory</c> rows outright — so a
/// secretary could always type a patient's medical history through « Modifier » while being refused a <em>read</em>
/// of the same text one tab over. The boundary did not protect the data; it only chose which door reception had to
/// use, and it made « Dossiers médicaux » a wall of « Vous n'avez pas les droits » on the screen reception spends
/// its day in. Practice decided it: in a Tunisian cabinet the assistant(e) fills much of the record in.</para>
///
/// <para>Two things make the wider write surface accountable rather than merely wider. Every mutation is
/// attributed — <c>AuditSaveChangesInterceptor</c> stamps the actor on each mutated aggregate root, readable at
/// <c>GET /api/audit</c> — and <em>clinical credit</em> is resolved by <c>PractitionerAttribution</c>, which puts
/// the caller <b>last</b>, so a secretary recording a dentist's work never credits themselves.</para>
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Authenticated, with <b>no role required</b> — the onboarding surface, and nothing else.
    ///
    /// <para>This is not a fig leaf for "we did not decide". In Cloud mode the role lives in the DB and reaches
    /// the JWT only once Auth0's <c>app_metadata</c> has been written, which happens <em>after</em> the user
    /// joins a clinic. So <c>user-status</c> (the call whose entire purpose is to <em>discover</em> the role),
    /// <c>POST /clinics</c> and <c>POST /clinics/join</c> are reached by a genuinely role-less principal, and
    /// any <see cref="RoleRequirement"/> would refuse them — breaking Cloud onboarding outright. Naming that
    /// state is what lets the coverage guard demand an explicit policy on every action without lying.</para>
    /// </summary>
    public const string Authenticated = "Authenticated";

    /// <summary>
    /// Any member of the clinic — admin, doctor or secretary. Reception's job, per-patient money, <b>the patient's
    /// clinical record (read and record, never delete)</b>, and the shared reads every role needs (the
    /// notification bell, « Mon profil », the catalog lookups the pickers read).
    ///
    /// <para><b>It includes <c>admin</c> deliberately, and that is the whole reason it exists</b> rather than
    /// the old <c>DoctorOrSecretary</c>. <c>CreateClinicCommand</c> makes a clinic's creator an <b>admin</b> and,
    /// for the « cabinet à un seul dentiste » case, links a <c>Doctor</c> record to that same admin account. So
    /// in the common Tunisian practice the owner-dentist's role is <c>admin</c>, not <c>doctor</c> — and a
    /// literal <c>{doctor, secretary}</c> policy on the agenda, the patient list or the till would lock the
    /// owner out of their own practice. There is no implicit admin in
    /// <c>RoleAuthorizationHandler</c>; a policy admits exactly the roles it names.</para>
    /// </summary>
    public const string AnyClinicRole = "AnyClinicRole";

    /// <summary>
    /// Admin or doctor — clinic-wide money and irreversible clinical loss:
    /// <list type="bullet">
    ///   <item><b>Clinic-wide money</b>: le tableau de bord, la caisse et son extrait, les créances, le chiffre
    ///   d'affaires, les chèques détenus, and every money CSV export.</item>
    ///   <item><b>The corrective money operations</b>: annuler une note, annuler un paiement, émettre un avoir,
    ///   amender un devis accepté.</item>
    ///   <item><b>Deleting from the clinical record</b>: a fiche de soins, an antécédent (this is where an
    ///   <em>allergy</em> lives), a medical document and its blob, a patient file. Recording is
    ///   <see cref="AnyClinicRole"/>; erasing is here.</item>
    ///   <item><b>Bulk and lifecycle operations on patients</b>: the CSV import, archiver / désarchiver.</item>
    /// </list>
    ///
    /// <para>⚠️ It no longer means « clinical authorship and clinical free text » — see the reversal recorded on
    /// the class. If you are reaching for this policy on a clinical <em>write</em>, that is the charter you are
    /// arguing with, and <c>ClinicalRecordAccessTests</c> will fail.</para>
    /// </summary>
    public const string AdminOrDoctor = "AdminOrDoctor";

    /// <summary>
    /// Admin only — user management, clinic configuration, the integration secrets, and the destructive
    /// operations whose effect cannot be read off any screen afterwards: deleting a patient, deleting an
    /// expense (which silently raises the reported Net) and deleting a stock article (which wipes its whole
    /// movement history).
    /// </summary>
    public const string AdminOnly = "AdminOnly";

    /// <param name="isLocalMode">
    /// When true (Local/offline mode — FR-E3 release gate) a <see cref="AuthorizationOptions.FallbackPolicy"/>
    /// of <c>RequireAuthenticatedUser()</c> is installed so every endpoint lacking an explicit
    /// <c>[AllowAnonymous]</c> fails <em>closed</em> (401) — covering the anonymous-by-omission controllers
    /// and any future controller that forgets <c>[Authorize]</c>. In Cloud mode the fallback stays null
    /// (named policies only) so Cloud behavior is byte-for-byte unchanged.
    /// </param>
    public static void ConfigurePolicies(AuthorizationOptions options, bool isLocalMode = false)
    {
        // Onboarding: authenticated but deliberately role-less (see the constant's remarks).
        options.AddPolicy(Authenticated, policy => policy.RequireAuthenticatedUser());

        // Reception's job + per-patient money + the clinical record + the reads every role shares.
        options.AddPolicy(AnyClinicRole, policy =>
            policy.Requirements.Add(new RoleRequirement(
                User.RoleAdmin, User.RoleDoctor, User.RoleSecretary)));

        // Clinic-wide money, the corrective money operations, and deleting from the clinical record.
        options.AddPolicy(AdminOrDoctor, policy =>
            policy.Requirements.Add(new RoleRequirement(User.RoleAdmin, User.RoleDoctor)));

        // Users, clinic configuration, and the irreversible deletes.
        options.AddPolicy(AdminOnly, policy =>
            policy.Requirements.Add(new RoleRequirement(User.RoleAdmin)));

        if (isLocalMode)
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        }
    }
}
