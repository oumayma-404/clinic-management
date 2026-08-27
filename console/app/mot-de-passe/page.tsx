import { fetchAuthMeta } from "@/lib/api/platform";
import { readSessionToken } from "@/lib/session";
import { ChangePasswordForm } from "./change-password-form";

/**
 * AC-8.6 — a signed-in console account changes its **own** password, and nothing else about any account.
 *
 * It is also the one screen a freshly-bootstrapped account can reach: `PlatformAccountStateMiddleware` refuses
 * every other console route while the one-time password the verb printed is still in place, which is what makes
 * « one-time » true of it.
 *
 * ⚠️ **The password floor is read here, server-side, and passed down** (`hosted-security-hardening` FR-1.9) —
 * the form used to print « Au moins 8 caractères. » as a literal, i.e. a second authority that would have gone
 * on stating 8 the moment the server's floor moved. A **failed** read passes `null` and the form simply says
 * nothing and pre-checks nothing: the server refuses a short password with its own sentence, so a metadata
 * failure must not stand between an operator and the one screen a bootstrapped account can open.
 */
export default async function ChangePasswordPage() {
  const token = await readSessionToken();

  let passwordMinLength: number | null = null;
  if (token) {
    try {
      passwordMinLength = (await fetchAuthMeta(token)).passwordMinLength;
    } catch {
      // Stated above: the floor is a courtesy, the server is the guard.
    }
  }

  return (
    <main className="mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center gap-6 px-4 py-10">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Changer le mot de passe</h1>
        <p className="text-sm text-muted-foreground">
          Vous serez déconnecté ensuite : toutes les sessions ouvertes sont invalidées.
        </p>
      </header>

      <ChangePasswordForm passwordMinLength={passwordMinLength} />
    </main>
  );
}
