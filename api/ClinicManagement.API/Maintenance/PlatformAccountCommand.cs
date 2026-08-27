using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Operator utility that creates, deactivates and re-secrets the vendor's console accounts
/// (<c>platform-console</c> AC-8.1, AC-8.2, AC-8.5).
///
/// <code>
///   ClinicManagement.API.exe platform-account create --email ops@editeur.tn --name "Nom Prénom"
///   ClinicManagement.API.exe platform-account --deactivate --email ops@editeur.tn
///   ClinicManagement.API.exe platform-account --reset-totp      --email ops@editeur.tn
///   ClinicManagement.API.exe platform-account --reset-password  --email ops@editeur.tn
/// </code>
///
/// <para><b>Why a verb and not an endpoint, and why there is no MediatR command behind it.</b> AC-8.5 is explicit:
/// there is no console screen that lists, creates or deactivates an account. A handler reachable through the
/// mediator would be one attribute away from being callable over HTTP, and the account this creates can read every
/// cabinet in the deployment. The shared logic therefore lives in
/// <see cref="PlatformAccountProvisioning"/> — an Application static, like <c>LocalClinicProvisioning</c> — and
/// this verb is its only caller.</para>
///
/// <para><b>⚠️ Gated on a configured connection string, not on a deployment capability</b> (amendment M3's
/// reasoning, unchanged). It runs no PostgreSQL binary, so <c>HasLocalDbTooling</c> would be the wrong question —
/// and gating on <c>ServesPlatformConsole</c> would be worse than wrong: an operator must be able to bootstrap the
/// first account <i>before</i> switching the listener on, and a locked-out vendor must be able to re-secret one on
/// a deployment where the console has just been turned off.</para>
///
/// <para><b>Hosted invocation</b>, as its siblings:
/// <c>docker exec clinic-api-prod dotnet ClinicManagement.API.dll platform-account create …</c> — the container's
/// environment is inherited, so <c>AddInstallLayers()</c> resolves the running app's connection string.</para>
/// </summary>
public static class PlatformAccountCommand
{
    public const string CommandName = "platform-account";

    /// <summary>Returns a process exit code: 0 on success, 1 on any failure.</summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var email = ConsoleArgs.ReadOption(args, "--email");
            var deactivate = args.Contains("--deactivate", StringComparer.OrdinalIgnoreCase);
            var resetTotp = args.Contains("--reset-totp", StringComparer.OrdinalIgnoreCase);
            var resetPassword = args.Contains("--reset-password", StringComparer.OrdinalIgnoreCase);
            var create = args.Contains("create", StringComparer.OrdinalIgnoreCase);

            // ⚠️ Still exactly one, now of four. The mutual exclusion is what stops `--reset-totp --reset-password`
            // silently doing one of them: an operator who asked for both and got one would believe both had
            // happened, and would go on to tell somebody a factor was re-issued when it was not.
            if (string.IsNullOrWhiteSpace(email) || CountTrue(create, deactivate, resetTotp, resetPassword) != 1)
            {
                return Usage();
            }

            var name = ConsoleArgs.ReadOption(args, "--name");
            if (create && string.IsNullOrWhiteSpace(name))
            {
                return Usage();
            }

            var configuration = InstallConfiguration.BuildForConsoleVerb();

            if (!MaintenanceDatabase.HasConnectionString(configuration, "Managing a console account"))
            {
                return 1;
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(configuration);

            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var sp = scope.ServiceProvider;

            // Named so any row this writes reads as « Tâche automatique (platform-account) » rather than
            // « unknown ». The PlatformAccount tables are themselves excluded from the ledger, so in practice
            // this declares an actor for nothing — which is the correct state, not a reason to omit it.
            sp.GetRequiredService<IAuditActorProvider>().RunAs(CommandName);

            // A console account belongs to no cabinet, so there is no UseClinic to make. Declaring system-wide is
            // what stops the query filters returning nothing if a future read here touches a filtered table.
            sp.GetRequiredService<ITenantScope>().UseSystemWide(PlatformTenantScope.Reason);

            var accounts = sp.GetRequiredService<IPlatformAccountRepository>();
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var result = create
                ? await PlatformAccountProvisioning.CreateAsync(
                    email, name!, accounts,
                    sp.GetRequiredService<IPlatformAuthService>(),
                    sp.GetRequiredService<ITotpService>(),
                    sp.GetRequiredService<IPlatformSecretProtector>(),
                    unitOfWork, cancellationToken)
                : deactivate
                    ? await PlatformAccountProvisioning.DeactivateAsync(email, accounts, unitOfWork, cancellationToken)
                    : resetPassword
                        ? await PlatformAccountProvisioning.ResetPasswordAsync(
                            email, accounts,
                            sp.GetRequiredService<IPlatformAuthService>(),
                            unitOfWork, cancellationToken)
                        : await PlatformAccountProvisioning.ResetTotpAsync(
                            email, accounts,
                            sp.GetRequiredService<ITotpService>(),
                            sp.GetRequiredService<IPlatformSecretProtector>(),
                            unitOfWork, cancellationToken);

            if (result.IsFailure)
            {
                Console.Error.WriteLine($"Console account operation failed: {result.Error}");
                return 1;
            }

            Report(
                result.Value!,
                create ? Operation.Create
                    : deactivate ? Operation.Deactivate
                    : resetPassword ? Operation.ResetPassword
                    : Operation.ResetTotp);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Console account operation failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Which of the four operations ran.
    ///
    /// <para>⚠️ <b>Passed in rather than inferred from which fields of <see cref="PlatformAccountProvisioned"/> are
    /// null.</b> <see cref="Report"/> used to read « a temporary password and no secret » as « created », which was
    /// sound while only three operations existed and stopped being sound the moment a fourth returned exactly that
    /// shape: a password reset would have printed « Console account created » and the enrolment instructions for a
    /// secret it never minted.</para>
    /// </summary>
    private enum Operation
    {
        Create,
        Deactivate,
        ResetTotp,
        ResetPassword
    }

    private static void Report(PlatformAccountProvisioned provisioned, Operation operation)
    {
        var account = provisioned.Account;

        Console.WriteLine();

        if (operation == Operation.Deactivate)
        {
            Console.WriteLine("Console account deactivated.");
            Console.WriteLine($"  Account:  {account.Email}");
            Console.WriteLine();
            Console.WriteLine("Its live sessions are refused on their NEXT request, not at token expiry.");
            Console.WriteLine();
            return;
        }

        if (operation == Operation.ResetPassword)
        {
            Console.WriteLine("Console account password reset.");
            Console.WriteLine($"  Account:            {account.Email}");
            Console.WriteLine($"  Name:               {account.FullName}");
            Console.WriteLine($"  Temporary password: {provisioned.TemporaryPassword}");
            Console.WriteLine();
            Console.WriteLine("The password above is one-time and is shown ONCE: every console route but");
            Console.WriteLine("« changer le mot de passe » is refused until it is changed. The account's live");
            Console.WriteLine("sessions are revoked.");
            Console.WriteLine();
            Console.WriteLine("The authenticator and the recovery codes are UNCHANGED — this resets the password");
            Console.WriteLine("only. An account that has also lost its second factor needs --reset-totp as well.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine(operation == Operation.ResetTotp
            ? "Console account second factor re-issued."
            : "Console account created.");
        Console.WriteLine($"  Account:            {account.Email}");
        Console.WriteLine($"  Name:               {account.FullName}");

        if (provisioned.TemporaryPassword is not null)
        {
            Console.WriteLine($"  Temporary password: {provisioned.TemporaryPassword}");
        }

        Console.WriteLine($"  Enrolment secret:   {provisioned.EnrolmentSecret}");
        Console.WriteLine();
        Console.WriteLine("Store the secret in an authenticator app now — it is shown ONCE and nothing can print");
        Console.WriteLine("it again. Then complete enrolment from the console's own sign-in screen, which asks for");
        Console.WriteLine("the password and a generated code, and returns the recovery codes (also shown once).");

        if (provisioned.TemporaryPassword is not null)
        {
            Console.WriteLine();
            Console.WriteLine("The password above is one-time: every console route but « changer le mot de passe »");
            Console.WriteLine("is refused until it is changed.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("⚠️  The previous authenticator and every recovery code are now invalid, and the");
            Console.WriteLine("    account's live sessions are revoked. That is what makes a lost factor safe to");
            Console.WriteLine("    replace rather than merely joined by a second one.");
        }

        Console.WriteLine();
    }

    private static int CountTrue(params bool[] flags) => flags.Count(f => f);

    private static int Usage()
    {
        Console.Error.WriteLine(
            $"Usage: {CommandName} create --email <email> --name <full name>\n"
            + $"       {CommandName} --deactivate --email <email>\n"
            + $"       {CommandName} --reset-totp  --email <email>\n"
            + "Exactly one of create / --deactivate / --reset-totp, and --email is always required.");
        return 1;
    }
}
