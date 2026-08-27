"use client"

import { useRouter } from "next/navigation"
import { ArrowLeft, Archive } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

/**
 * What a route renders once its screen has been withdrawn from the product.
 *
 * <p><b>Not `notFound()`.</b> There is no `app/not-found.tsx`, so Next's built-in 404 page is English, and no
 * English string may reach a user. It is also the wrong statement: the route exists and answers, the screen was
 * removed on purpose, and a reader arriving from a bookmark deserves that sentence rather than « not found ».</p>
 *
 * <p><b>Shared, like {@link AccessDeniedCard}.</b> Two withdrawn screens is already the count at which two
 * hand-written copies start to drift on wording, on the way back, and on the coarse-pointer floor.</p>
 *
 * <p>The way back is a parameter for the same reason it is there: a destination the reader can actually open.
 * `/appointments` is reachable by every role.</p>
 */
export function RetiredPageCard({
  title = "Page retirée",
  description,
  backHref = "/appointments",
  backLabel = "Retour à l'agenda",
}: {
  title?: string
  /** Says what was removed and where the same information lives now, in French. */
  description: string
  backHref?: string
  backLabel?: string
}) {
  const router = useRouter()

  return (
    // `min-h-full` centring resolves against `<main>`, so the caller passes `width="none"` on its `AppShell`.
    <div className="flex min-h-full items-center justify-center p-6">
      <Card className="w-full max-w-md">
        <CardHeader className="space-y-3 text-center">
          <div className="mx-auto flex size-14 items-center justify-center rounded-full bg-muted">
            <Archive className="size-7 text-muted-foreground" aria-hidden="true" />
          </div>
          <CardTitle>{title}</CardTitle>
          <CardDescription>{description}</CardDescription>
        </CardHeader>
        <CardContent>
          {/* `min-h-11` for the coarse-pointer floor — the only control on the screen. */}
          <Button variant="outline" className="min-h-11 w-full gap-2" onClick={() => router.push(backHref)}>
            <ArrowLeft className="size-4" aria-hidden="true" />
            {backLabel}
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}
