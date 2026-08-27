"use client"

import { useRouter } from "next/navigation"
import { ArrowLeft, Lock } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

/**
 * What a screen renders when the caller's role cannot open it.
 *
 * <p><b>Why it is shared.</b> The three admin catalog pages each grew their own copy of this card, and I3 needed
 * three more (« Factures », « Caisse », « Créances »). Six hand-written copies of an access refusal is the
 * defect shape this codebase keeps finding: the wording drifts, one of them keeps a `red-*` literal instead of
 * the destructive tokens, and the sixth forgets the way back. One component, one refusal.</p>
 *
 * <p><b>It is not the gate.</b> The server refuses these routes — the money screens are `AdminOrDoctor`, the
 * catalogs' writes are `AdminOnly`. This exists so a bookmarked URL, or a role changed while a tab was open,
 * lands on a sentence rather than on a blank page or a French error toast about a 403. The rail already hides
 * the entry (`lib/nav.ts`); this covers every other way of arriving.</p>
 *
 * <p><b>The way back is a real destination, chosen by the caller.</b> A secretary refused « Caisse » must not be
 * sent to « Tableau de bord », which refuses them too — two dead ends in a row is how a user concludes the
 * software is broken rather than restricted. Hence `backHref`/`backLabel` rather than a hardcoded `/`.</p>
 */
export function AccessDeniedCard({
  title = "Accès restreint",
  description,
  backHref = "/appointments",
  backLabel = "Retour à l'agenda",
}: {
  title?: string
  /** Says *who* may open it, in French. A refusal that does not name the holder cannot be acted on. */
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
          {/* Destructive tokens, not `red-*` literals — the token carries its own dark value, so there is no
              hand-maintained `dark:` twin to drift. */}
          <div className="mx-auto flex size-14 items-center justify-center rounded-full bg-destructive-wash">
            <Lock className="size-7 text-destructive" aria-hidden="true" />
          </div>
          <CardTitle>{title}</CardTitle>
          <CardDescription>{description}</CardDescription>
        </CardHeader>
        <CardContent>
          {/* `min-h-11` for the coarse-pointer floor: this is the only control on the screen, and a reception
              tablet is the most likely place to meet it. */}
          <Button variant="outline" className="min-h-11 w-full gap-2" onClick={() => router.push(backHref)}>
            <ArrowLeft className="size-4" aria-hidden="true" />
            {backLabel}
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}
