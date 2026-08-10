import { ChangePasswordForm } from "./change-password-form";

/**
 * AC-8.6 — a signed-in console account changes its **own** password, and nothing else about any account.
 *
 * It is also the one screen a freshly-bootstrapped account can reach: `PlatformAccountStateMiddleware` refuses
 * every other console route while the one-time password the verb printed is still in place, which is what makes
 * « one-time » true of it.
 */
export default function ChangePasswordPage() {
  return (
    <main className="mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center gap-6 px-4 py-10">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Changer le mot de passe</h1>
        <p className="text-sm text-muted-foreground">
          Vous serez déconnecté ensuite : toutes les sessions ouvertes sont invalidées.
        </p>
      </header>

      <ChangePasswordForm />
    </main>
  );
}
