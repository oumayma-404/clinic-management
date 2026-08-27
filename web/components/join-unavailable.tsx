"use client"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { ShieldCheck } from "lucide-react"

/**
 * What `/join` shows on a deployment where self-registration is closed (`multi-tenant-cloud` US-3).
 *
 * ⚠️ **Not a 404, and not a hidden link** — § 0 of the device contract: a capability that is genuinely
 * unavailable says so and names the alternative. Joining by clinic code is a LAN-scale gate (six characters,
 * printed on a settings screen), so the hosted profile closes it and an admin creates the account instead. A
 * person who followed « Rejoindre la clinique » from the login page has to find out *here* what to do; sending
 * them to a dead route, or quietly deleting the link they were told to use, leaves them with no next step.
 *
 * Shared by the page's capability probe and the wizard's 404 fallback so the two cannot word it differently.
 */
/*
 * ⚠️ `justify-start` + `mx-auto`, never `justify-center` (§ 11). A flex item's `min-width: auto` floor beats
 * `max-w-md`, and when the content overflows a *centring* parent splits that overflow to BOTH sides — the
 * inline-start half landing outside the scrollable region, unreachable by any means. `app/join/page.tsx` annotates
 * the same structure costing ~24 px of a card's left edge. Here the title's « l'administrateur » (`&apos;` = U+0027,
 * no break opportunity) is ~171 px: fine at a plain 320 px, off-canvas at 320 px with the 200 % zoom § 0 requires.
 * An auto margin resolves to 0 when there is no free space, so it centres and then degrades to start-aligned.
 */
export default function JoinUnavailable() {
  return (
    <div className="min-h-dvh bg-background flex items-center justify-start p-4 sm:p-6">
      <div className="mx-auto w-full max-w-md">
        <Card className="border-primary/20 shadow-lg">
          <CardHeader className="space-y-4 text-center">
            <div className="mx-auto inline-flex size-16 items-center justify-center rounded-full bg-primary/10">
              <ShieldCheck className="size-8 text-primary" aria-hidden="true" />
            </div>
            <div>
              <CardTitle className="text-2xl break-words text-accent-foreground">
                Votre compte est créé par l&apos;administrateur
              </CardTitle>
              <CardDescription className="mt-2">
                Sur cette installation, on ne rejoint pas un cabinet avec un code : c&apos;est l&apos;administrateur
                du cabinet qui crée les comptes du personnel.
              </CardDescription>
            </div>
          </CardHeader>
          <CardContent className="space-y-6">
            <ol className="space-y-3 text-sm text-muted-foreground">
              <li className="flex gap-3">
                <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-muted text-xs font-medium text-foreground">
                  1
                </span>
                <span>
                  Demandez à l&apos;administrateur de votre cabinet de créer votre compte depuis
                  {" "}
                  <span className="font-medium text-foreground">« Utilisateurs »</span>.
                </span>
              </li>
              <li className="flex gap-3">
                <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-muted text-xs font-medium text-foreground">
                  2
                </span>
                <span>Il vous remettra un mot de passe temporaire.</span>
              </li>
              <li className="flex gap-3">
                <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-muted text-xs font-medium text-foreground">
                  3
                </span>
                <span>
                  Connectez-vous avec ce mot de passe : vous devrez en choisir un nouveau immédiatement.
                </span>
              </li>
            </ol>

            {/*
              `coarse:h-11` because `size="lg"` is `h-10` — 40 px, under the § 2 floor on a finger. Grown rather
              than given `.touch-target`: this is the page's only control and it is already full-width, so there
              is nothing for an invisible overlay to avoid disturbing, and a real 44 px target is the honest one.
            */}
            <Button asChild className="w-full coarse:h-11" size="lg">
              <a href="/login">Aller à la connexion</a>
            </Button>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
