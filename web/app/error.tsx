"use client"

import { useEffect } from "react"
import { Button } from "@/components/ui/button"

/**
 * Segment-level error boundary (App Router). Catches render/data throws in the page tree and shows a
 * French message + "Réessayer" instead of a blank screen (AC-3). `reset()` re-renders the segment.
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
        Une erreur inattendue est survenue lors de l&apos;affichage de cette page. Veuillez réessayer.
      </p>
      <Button onClick={reset}>Réessayer</Button>
    </div>
  )
}
