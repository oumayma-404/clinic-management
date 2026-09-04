import Link from "next/link"
import { Compass } from "lucide-react"

import { Button } from "@/components/ui/button"

/**
 * The French not-found page.
 *
 * <p>⚠️ <b>Without this file Next serves its own, in English.</b> `/dashboard` — a natural guess, since the home
 * is `/` — answered « 404 · This page could not be found. » in an otherwise fully French application, and the
 * rule that no English string reaches a user has no exception for a mistyped URL. It is also the one screen a
 * reader arrives at already lost, so it must name the way back rather than only stating the failure.</p>
 *
 * <p>A <b>server</b> component with no shell: this renders for routes outside the authenticated area too (and
 * before any session is known), so mounting `AppShell` here would put a rail and a bottom bar around a page
 * that may be met while signed out. `ui/retired-page-card` is the sibling for the opposite case — a route that
 * exists and whose screen was deliberately withdrawn.</p>
 */
export default function NotFound() {
  return (
    <main className="flex min-h-dvh items-center justify-center p-6">
      <div className="my-auto w-full max-w-md space-y-4 text-center">
        <span className="mx-auto flex size-12 items-center justify-center rounded-xl bg-muted text-muted-foreground">
          <Compass className="size-6" aria-hidden="true" />
        </span>
        <h1 className="text-xl font-semibold">Cette page n&apos;existe pas</h1>
        <p className="text-sm text-muted-foreground">
          L&apos;adresse demandée est introuvable. Elle a peut-être changé, ou le lien est incomplet.
        </p>
        <div className="flex flex-col gap-2 sm:flex-row sm:justify-center">
          <Button asChild className="coarse:h-11">
            <Link href="/">Tableau de bord</Link>
          </Button>
          <Button asChild variant="outline" className="coarse:h-11">
            <Link href="/appointments">Agenda</Link>
          </Button>
        </div>
      </div>
    </main>
  )
}
