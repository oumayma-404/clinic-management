using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Clinics;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Operator utility that creates clinic #N and its first administrator, printing a one-time password
/// (<c>multi-tenant-cloud</c> US-3).
///
/// <code>
///   ClinicManagement.API.exe provision-clinic --name "Cabinet Ben Salah" \
///       --admin-email owner@cabinet.tn --admin-name "Dr Ahmed Ben Salah" [--city Tunis] [--phone 71234567]
/// </code>
///
/// <para><b>Why a verb and not an endpoint.</b> The equivalent HTTP path, <c>POST /api/auth/setup</c>, is gated on
/// <c>LocalRequest.IsLoopback</c> — right for a clinic's own PC, impossible over the internet — and is additionally
/// a <b>one-time bootstrap</b> (<c>AnyUserExistsAsync</c>, AC-1.2a), so it can create the first clinic of an install
/// and never the second. Both remain true and unchanged in <see cref="DeploymentKind.SelfHostedLan"/>.</para>
///
/// <para><b>Hosted invocation.</b> <c>Program.cs</c> intercepts verbs before the web host boots, so in the hosted
/// topology this is
/// <c>docker exec clinic-api-prod dotnet ClinicManagement.API.dll provision-clinic …</c> — the container's
/// environment is inherited, so <c>AddInstallLayers()</c> resolves the same connection string as the running app.</para>
/// </summary>
public static class ProvisionClinicCommand
{
    public const string CommandName = "provision-clinic";

    /// <summary>Returns a process exit code: 0 on success, 1 on any failure.</summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var name = ReadOption(args, "--name");
            var adminEmail = ReadOption(args, "--admin-email");
            var adminName = ReadOption(args, "--admin-name");

            if (string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(adminEmail)
                || string.IsNullOrWhiteSpace(adminName))
            {
                Console.Error.WriteLine(
                    $"Usage: {CommandName} --name <clinic name> --admin-email <email> --admin-name <full name> "
                    + "[--city <city>] [--phone <phone>] [--address <address>]");
                return 1;
            }

            var configuration = InstallConfiguration.BuildForConsoleVerb();

            // Only the profiles whose accounts this product owns. In CloudBrowser the identity provider is Auth0,
            // so a password-backed admin created here would be an account nobody could ever log into.
            // ⚠️ Deliberately NOT gated on HasLocalDbTooling, unlike the backup and report verbs: that capability
            // is about pg_dump/pg_restore being on the box, and this verb needs only the connection string —
            // which is precisely the profile (HostedMultiTenant) it exists for.
            var profile = DeploymentProfile.Resolve(configuration);
            if (!profile.UsesLocalAccounts)
            {
                Console.Error.WriteLine(
                    $"This deployment does not own its accounts (deployment profile: {profile.Kind}). "
                    + "An Auth0 deployment creates users through Auth0.");
                return 1;
            }

            // The same gate its three read-only siblings use (review finding 31). Without it, no connection string
            // meant an infrastructure exception out of AddInfrastructure/DbContext resolution instead of the operator
            // sentence naming the environment variable — which is the defect MaintenanceDatabase was extracted to fix.
            if (!MaintenanceDatabase.HasConnectionString(configuration, "Provisioning a clinic"))
            {
                return 1;
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(configuration);

            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            // I6: a clinic and an admin credential created with no HTTP request behind them. Naming the verb makes
            // the audit rows read « Tâche automatique (provision-clinic) » rather than « unknown ».
            scope.ServiceProvider.GetRequiredService<IAuditActorProvider>().RunAs(CommandName);

            // US-2: one clinic, so UseClinic and not UseSystemWide — SystemWide switches the query-filter backstop
            // off for the whole scope, and this is the narrowest work there is. The id is minted here rather than
            // inside the provisioner so the scope can be declared BEFORE the writes it covers.
            var clinicId = Guid.NewGuid();
            scope.ServiceProvider.GetRequiredService<ITenantScope>().UseClinic(clinicId);

            var localAuth = scope.ServiceProvider.GetRequiredService<ILocalAuthService>();
            var temporaryPassword = localAuth.GenerateTemporaryPassword();

            var result = await LocalClinicProvisioning.ProvisionAsync(
                new LocalClinicRequest(
                    clinicId,
                    name,
                    adminEmail,
                    localAuth.HashPassword(temporaryPassword),
                    adminName,
                    // The operator has to read this password out to someone, so it must not remain valid after.
                    MustChangePassword: true,
                    ReadOption(args, "--address"),
                    ReadOption(args, "--phone"),
                    ReadOption(args, "--city")),
                scope.ServiceProvider.GetRequiredService<IClinicRepository>(),
                scope.ServiceProvider.GetRequiredService<IUserRepository>(),
                scope.ServiceProvider.GetRequiredService<IDoctorRepository>(),
                scope.ServiceProvider.GetRequiredService<IProcedureTypeRepository>(),
                // clinic-subscription FR-4: no door creates a cabinet without an entitlement. ⚠️ Both of these are
                // registered by `AddInfrastructure`, which is this verb's whole container — registering them in
                // `AddApplication` instead would resolve nowhere here and take the hosted deployment's only
                // clinic-creation door with it. The entitlement write is already inside the `UseClinic(clinicId)`
                // scope declared above, so there is nothing extra to declare.
                scope.ServiceProvider.GetRequiredService<IClinicSubscriptionRepository>(),
                scope.ServiceProvider.GetRequiredService<ISubscriptionPolicy>(),
                // vendor-whatsapp-messaging-quota FR-3: and none creates one without a WhatsApp reminder forfait.
                // Both are registered by `AddInfrastructure` for exactly the reason stated above.
                scope.ServiceProvider.GetRequiredService<IMessagingAllowanceRepository>(),
                scope.ServiceProvider.GetRequiredService<IMessagingAllowancePolicy>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<IClinicCatalogSeeder>(),
                scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(CommandName),
                cancellationToken);

            if (result.IsFailure)
            {
                Console.Error.WriteLine($"Clinic provisioning failed: {result.Error}");
                return 1;
            }

            var provisioned = result.Value!;
            Console.WriteLine();
            Console.WriteLine("Clinic provisioned successfully.");
            Console.WriteLine($"  Clinic:             {provisioned.Clinic.Name}");
            Console.WriteLine($"  Clinic id:          {provisioned.Clinic.Id}");

            // ⚠️ Labelled, not printed bare (review finding 31). In HostedMultiTenant — the profile this verb exists
            // for — AllowsSelfRegistration is false and POST /api/auth/register 404s, so a bare « Join code » beside
            // the one-time password reads as an alternative way in and leads nowhere.
            Console.WriteLine(profile.AllowsSelfRegistration
                ? $"  Join code:          {provisioned.Clinic.Code}"
                : $"  Clinic code:        {provisioned.Clinic.Code}  (reference only — self-registration is closed "
                  + "on this deployment)");

            Console.WriteLine($"  Administrator:      {provisioned.Admin.Email}");
            Console.WriteLine($"  Temporary password: {temporaryPassword}");
            Console.WriteLine();
            Console.WriteLine("Give this password to the administrator. They will be required to choose a new");
            Console.WriteLine("one the first time they log in, and can then create the rest of the staff accounts");
            Console.WriteLine("from « Utilisateurs ».");

            if (!provisioned.CatalogsSeeded)
            {
                Console.WriteLine();
                Console.WriteLine("⚠️  Catalogues non initialisés (CNAM, médicaments, actes dentaires) — ils seront");
                Console.WriteLine("    recréés au prochain démarrage de l'API. Voir le journal pour la cause.");
            }

            Console.WriteLine();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Clinic provisioning failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Reads <c>--flag value</c>, or null when absent. A flag given with no value returns null rather than
    /// swallowing the next flag, so <c>--name --admin-email x</c> fails the usage check instead of creating a
    /// clinic literally called « --admin-email ».
    /// </summary>
    /// <summary>This verb's own arguments, read by the shared <see cref="ConsoleArgs.ReadOption"/>.</summary>
    private static string? ReadOption(string[] args, string flag) => ConsoleArgs.ReadOption(args, flag);
}
