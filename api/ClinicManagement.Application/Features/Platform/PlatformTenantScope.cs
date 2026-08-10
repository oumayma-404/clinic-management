using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>
/// Declares a console request's tenant scope: <b>system-wide</b>, explicitly and with a reason
/// (<c>platform-console</c> EC-12, risk R-4).
///
/// <para>⚠️ <b>It lives in Application, not beside the middleware that calls <see cref="Declare"/>.</b> The
/// backstop below is meant to run at « the console handlers' entry point », and those handlers are here — a copy
/// in the API layer would be unreachable from them, and a second copy is how a guard keeps passing while the
/// thing it guards moves.</para>
///
/// <para><b>Why a console request cannot simply fall through <c>TenantScopeMiddleware</c>.</b>
/// That middleware resolves the clinic from the caller's own <c>User</c> row. A console principal has none, so
/// the scope would land <c>Unset</c> — and since <c>multi-tenant-cloud</c> US-2 an unset scope makes every
/// filtered table return <b>zero rows, with no error</b>. Every cabinet would then read as empty, which is
/// indistinguishable from a genuinely idle portfolio: EC-8 says a cabinet with no activity is a real answer, and
/// that is exactly what makes the silent version dangerous.</para>
///
/// <para>⚠️ <b><see cref="EnsureDeclared"/> throws rather than repairing.</b> A console read that reached a
/// handler on an <c>Unset</c> scope is a fault in the pipeline, not a condition to recover from — and the visible
/// outcome of the throw is « je n'ai pas pu lire », which is precisely what EC-12 asks for and precisely what an
/// empty portfolio is not.</para>
/// </summary>
public static class PlatformTenantScope
{
    /// <summary>The reason recorded on every cross-cabinet read the console makes. One string, so the log reads consistently.</summary>
    public const string Reason = "platform console";

    /// <summary>
    /// Declares the scope for a console request. Idempotent — <c>ITenantScope.UseSystemWide</c> returns silently
    /// when the scope is already system-wide, so ordering against anything else that declares is not a hazard.
    /// </summary>
    public static void Declare(ITenantScope scope) => scope.UseSystemWide(Reason);

    /// <summary>
    /// Throws if a console read is about to run without a declared scope. Called by the console handlers'
    /// entry point rather than trusted from the middleware, because « the middleware ran » is the assumption
    /// that fails silently.
    /// </summary>
    public static void EnsureDeclared(ITenantScope scope)
    {
        if (scope.Kind == TenantScopeKind.SystemWide)
        {
            return;
        }

        throw new InvalidOperationException(
            "Une lecture de la console éditeur s'exécute sans portée inter-cliniques déclarée "
            + $"(portée actuelle : {scope.Kind}). Elle renverrait zéro ligne sans erreur, ce qui est "
            + "indiscernable d'un portefeuille vide. Vérifiez que PlatformTenantScopeMiddleware est bien "
            + "enregistré avant les contrôleurs de la console.");
    }
}
