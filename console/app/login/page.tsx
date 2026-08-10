import { SignInForm } from "./sign-in-form";

/**
 * The console's sign-in screen.
 *
 * ⚠️ **It must work fully at 320 px**, which is the likeliest phone use: the vendor is away from the desk and a
 * cabinet has just paid. So the card has no fixed width, the gutter survives, and every control is full-width
 * rather than laid out in columns that would collapse.
 */
export default function LoginPage() {
  return (
    <main className="mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center gap-6 px-4 py-10">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Console éditeur</h1>
        <p className="text-sm text-muted-foreground">
          Mot de passe et code de vérification requis.
        </p>
      </header>

      <SignInForm />

      <p className="text-xs text-muted-foreground">
        Cette console n&apos;accède à aucun dossier patient. Elle lit des comptages, des dates, l&apos;état
        d&apos;abonnement de chaque cabinet et le total encaissé par le cabinet sur le mois.
      </p>
    </main>
  );
}
