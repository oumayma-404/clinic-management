"use client"

import { useEffect } from "react"
import Link from "next/link"
import { Button } from "@/components/ui/button"

/**
 * Segment-level error boundary (App Router). Catches render/data throws in the page tree and shows a
 * French message instead of a blank screen (AC-3). `reset()` re-renders the segment.
 *
 * <p><b>Two ways out, not one.</b> « Réessayer » was the only control here, and it is the right *first* offer —
 * most throws are a transient read. But a deterministic render throw re-throws the instant the segment
 * re-renders, so a page that always fails leaves the user pressing the same button forever with no route back
 * into the app. « Retour au tableau de bord » is a full navigation to a different segment, which is the one
 * escape a boundary can guarantee, and it is deliberately secondary so it does not compete with the retry that
 * usually works.</p>
 *
 * <p><b>And something to quote.</b> `error.digest` is the hash Next.js logs server-side alongside the real stack
 * — in production the message itself is redacted, so the digest is the *only* handle that ties what the dentist
 * saw to what the operator can find in the logs. Showing it turns « ça a planté » into a supportable report.
 * Rendered only when it exists (it is absent for a client-side throw in dev), never as an empty « Référence : ».</p>
 */
export default function Error({
  error,
  reset,
}: {
  error: Error & { digest?: string }
  reset: () => void
}) {
  useEffect(() => {
    console.error(error)
  }, [error])

  return (
    <div className="flex min-h-[60dvh] flex-col items-center justify-center gap-4 p-6 text-center">
      <h2 className="text-xl font-semibold">Une erreur s&apos;est produite</h2>
      <p className="max-w-md text-sm text-muted-foreground">
        Une erreur inattendue est survenue lors de l&apos;affichage de cette page. Réessayez, ou revenez au
        tableau de bord.
      </p>
      <div className="flex flex-wrap items-center justify-center gap-2">
        <Button onClick={reset}>Réessayer</Button>
        <Button variant="outline" asChild>
          <Link href="/">Retour au tableau de bord</Link>
        </Button>
      </div>
      {error.digest && (
        <p className="text-xs text-muted-foreground">
          Référence : <span className="font-mono">{error.digest}</span>
        </p>
      )}
    </div>
  )
}
