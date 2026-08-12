using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// <c>reset-user-totp --email &lt;address&gt;</c> — the vendor removes a clinic account's second factor
/// (<c>hosted-security-hardening</c> FR-1.4), way back #3 of three.
///
/// <para><b>Why a verb and not an endpoint.</b> The other two ways back both need somebody at the practice: a
/// recovery code the user still has, or a second administrator to press the button. This one is for when neither
/// exists — a single-dentist cabinet whose owner lost their phone — and it is reachable only by whoever can run a
/// command inside the container. An HTTP route for it would be a route that disarms any account, and a handler
/// is one attribute away from being one, which is why there is no MediatR command behind this either.</para>
///
/// <para>⚠️ <b>It re-issues nothing and enrols nothing.</b> It clears the secret and every recovery code and
/// bumps <c>TokenVersion</c>; the user then enrols afresh from the login screen, proving possession of the new
/// authenticator themselves. Printing a new secret here would put a live credential in an operator's terminal
/// scrollback and in whatever captured it.</para>
///
/// <para>Gated on a configured connection string, never on a capability (amendment M3): it runs no PostgreSQL
/// binary, and the deployment it exists for above all is the hosted one, which has no local DB tooling.</para>
/// </summary>
internal static class ResetUserTotpConsoleCommand
{
    public const string CommandName = "reset-user-totp";

    private const int Success = 0;
    private const int Failed = 1;

    public static async Task<int> RunAsync(string[] args)
    {
        var email = ConsoleArgs.ReadOption(args, "--email");
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine($"Usage: {CommandName} --email <address>");
            return Failed;
        }

        var configuration = InstallConfiguration.BuildForConsoleVerb();
        if (!MaintenanceDatabase.HasConnectionString(configuration, "reset a user's second factor"))
        {
            return Failed;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Recorded as `job|reset-user-totp` rather than as an unattributable mutation.
        scope.ServiceProvider.GetRequiredService<IAuditActorProvider>().RunAs(CommandName);

        // The account is found by e-mail across the deployment, so the scope is system-wide: this verb exists
        // precisely because nobody at the cabinet can act, so there is no clinic to narrow to up front.
        scope.ServiceProvider.GetRequiredService<ITenantScope>()
            .UseSystemWide($"{CommandName}: resets one account's second factor, located by e-mail");

        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = await users.GetByEmailAsync(email.Trim());
        if (user is null)
        {
            Console.Error.WriteLine($"Aucun compte ne correspond à « {email.Trim()} ».");
            return Failed;
        }

        if (!user.IsTotpEnrolled)
        {
            // Not a failure of the command so much as nothing to do — but it exits non-zero so a script that
            // expected to disarm an account does not read « rien à faire » as « c'est fait ».
            Console.Error.WriteLine("Ce compte n'a pas de second facteur enrôlé.");
            return Failed;
        }

        user.DisableTotp();
        users.Update(user);
        await unitOfWork.SaveChangesAsync();

        Console.WriteLine($"Second facteur réinitialisé pour « {user.Email} ».");
        Console.WriteLine("Ce compte doit en enrôler un nouveau à sa prochaine connexion.");
        Console.WriteLine("Ses sessions ouvertes ont été fermées.");
        return Success;
    }
}
