"use client"

import { useMemo } from "react"
import { useRouter } from "next/navigation"
import { addDays, format } from "date-fns"
import { fr } from "date-fns/locale"
import { ArrowLeft, MessageSquareText } from "lucide-react"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { useUserStatus } from "@/lib/hooks/use-user-status"

/**
 * « Rappels » is parked until the SMS provider is contracted — and it now says what is coming
 * (guided-first-days D, design S16), instead of only « pas encore branchés ».
 *
 * <p>⚠️ <b>Only what is decided</b> (`features/sms-rappels-tunisie-sms`, memory `sms-rappels-decisions`): Tunisian
 * mobiles, no night sending, a patient may refuse, a monthly allowance. <b>No date, no price, no count</b> — the
 * work waits on the provider's answers, and a promise on this screen would be read as a commitment.</p>
 *
 * <p><b>Nothing was removed</b> — the delivery log lives verbatim in `rappels-screen.tsx` beside this file, unrouted
 * but still compiled. Bringing it back is importing `RappelsScreen` and returning it here. `nav.ts` keeps the entry:
 * a secretary looking for the reminders deserves this page rather than a missing link.</p>
 */
const STEPS = [
  { title: "Vous l'activez", line: "Dans Paramètres. Vous choisissez quand : 24 h avant, ou deux moments au choix." },
  {
    title: "APEXA envoie le SMS",
    line: "Au mobile tunisien du patient, jamais entre 21 h et 8 h. Le texte est modifiable.",
  },
  { title: "Vous suivez tout ici", line: "Chaque rappel envoyé, et s'il est bien parti." },
] as const

const FACTS = ["Mobiles tunisiens", "Pas la nuit", "Un patient peut refuser", "Forfait mensuel de messages"] as const

export default function RappelsPage() {
  const router = useRouter()
  const { data: status } = useUserStatus()
  const clinicName = status?.clinic?.name ?? status?.clinicName ?? "votre cabinet"

  // The example is tomorrow at 10:30 — « la veille », which is what the SMS is for.
  const example = useMemo(() => {
    const day = format(addDays(new Date(), 1), "EEEE d MMMM", { locale: fr })
    return `Rappel : Ross Geller, vous avez un rendez-vous le ${day} à 10:30 chez ${clinicName}.`
  }, [clinicName])

  return (
    <ClinicGuard>
      <AppShell>
        <div className="mx-auto grid w-full max-w-4xl gap-8 py-4 lg:grid-cols-[minmax(0,1fr)_18rem] lg:items-start">
          <section aria-labelledby="rappels-title" className="min-w-0 space-y-6">
            <div className="space-y-3">
              <span className="inline-flex rounded-full bg-warning-wash px-2.5 py-0.5 text-xs font-medium text-warning-ink">
                Bientôt disponible
              </span>
              <h1 id="rappels-title" className="text-2xl font-semibold leading-tight sm:text-3xl">
                Rappels par SMS
              </h1>
              <p className="text-base text-muted-foreground">
                La veille du rendez-vous, vos patients reçoivent un SMS. Moins d&apos;oublis, moins de fauteuils vides.
              </p>
            </div>

            <ol className="space-y-4">
              {STEPS.map((step, index) => (
                <li key={step.title} className="flex gap-3">
                  <span
                    aria-hidden
                    className="inline-flex size-7 shrink-0 items-center justify-center rounded-full bg-primary text-xs font-semibold text-primary-foreground"
                  >
                    {index + 1}
                  </span>
                  <div className="min-w-0 space-y-0.5">
                    <p className="text-sm font-semibold">{step.title}</p>
                    <p className="text-sm text-muted-foreground">{step.line}</p>
                  </div>
                </li>
              ))}
            </ol>

            <ul className="flex flex-wrap gap-2" aria-label="Ce qui est prévu">
              {FACTS.map((fact) => (
                <li key={fact} className="rounded-full border bg-card px-3 py-1 text-xs font-medium">
                  {fact}
                </li>
              ))}
            </ul>

            <Button variant="outline" className="min-h-11 gap-2" onClick={() => router.push("/appointments")}>
              <ArrowLeft className="size-4" aria-hidden="true" />
              Retour à l&apos;agenda
            </Button>
          </section>

          {/* The example SMS, drawn as a phone screen. Its patient is the « Friends » cast the site uses. */}
          <figure className="mx-auto w-full max-w-72 rounded-[2rem] border-4 border-foreground/80 bg-card p-3 shadow-sm">
            <figcaption className="sr-only">Exemple de SMS de rappel</figcaption>
            <div className="flex items-center justify-between px-2 pb-3 text-2xs text-muted-foreground">
              <span>18:30</span>
              <span>4G</span>
            </div>
            <div className="rounded-2xl bg-muted/60 p-3">
              <p className="mb-1.5 flex items-center gap-1.5 text-xs font-semibold">
                <MessageSquareText aria-hidden className="size-3.5 text-primary" />
                APEXA
              </p>
              <p className="text-sm leading-snug">{example}</p>
            </div>
          </figure>
        </div>
      </AppShell>
    </ClinicGuard>
  )
}
