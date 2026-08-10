import { SignOutButton } from "./sign-out-button";

/**
 * The portfolio — **a shell in Part 1**, filled by Part 2.
 *
 * It is deliberately an explicit « not built yet » state rather than an empty table: EC-12 asks that « I could
 * not read » and « there are no cabinets » never look the same, and a table with no rows would claim the second
 * while meaning neither. The same reasoning applies to a screen whose read does not exist yet.
 */
export default function CabinetsPage() {
  return (
    <main className="mx-auto w-full max-w-5xl px-4 py-8">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">Cabinets</h1>
          <p className="text-sm text-muted-foreground">Portefeuille des cabinets et de leur abonnement.</p>
        </div>
        <SignOutButton />
      </header>

      <section className="mt-8 rounded-lg border border-border bg-card p-6" role="status">
        <h2 className="text-base font-semibold">Portefeuille indisponible pour le moment</h2>
        <p className="mt-2 max-w-prose text-sm text-muted-foreground">
          La liste des cabinets et leurs compteurs d&apos;activité arrivent avec la prochaine étape de cette
          fonctionnalité. La connexion, elle, fonctionne : votre session est active.
        </p>
      </section>
    </main>
  );
}
