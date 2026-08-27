"use client"

import { useEffect, useState } from "react"
import { ArrowUpCircle, ExternalLink } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { isClientRefusedAsTooOld, onClientTooOld } from "@/lib/api/client"
import { metaApi, type ClientRequirementsDto } from "@/lib/api/meta"
import { PRODUCT_NAME } from "@/lib/brand"

/**
 * The screen a native shell gets when the server has refused it as too old (AC-33, mid-session half).
 *
 * <p><b>It is a takeover, not a toast.</b> Below the floor <i>every</i> `/api` route 426s, so without this the
 * app is a working-looking shell whose every panel fails one after another — a stack of French errors that never
 * says the one thing the user can act on. And it must not read as a sign-out: the account is fine, and a login
 * screen the app can never get past is strictly worse than the refusal itself.</p>
 *
 * <p><b>Mounted in `layout.tsx`, above the session provider, and inert in a browser.</b> A browser sends no
 * version header at all, so no 426 can arrive and this renders `null` for its whole life — which is why it is
 * safe at the root. It is also above the providers on purpose: the update state must survive a session context
 * that cannot load, because the refusal reaches the token exchange too.</p>
 *
 * <p><b>Where the store link comes from is the point of AC-29.</b> Everything shown below is read from
 * `GET /api/meta/client-requirements` — the one route deliberately exempt from the floor. If it were not exempt,
 * the single endpoint that says how to fix this would be unreachable by exactly the clients that need it.</p>
 */
export function ClientVersionGate() {
  const [refused, setRefused] = useState(false)
  const [requirements, setRequirements] = useState<ClientRequirementsDto | null>(null)

  useEffect(() => {
    // A refusal can land before this mounts (the token exchange fires first), so ask as well as listen.
    if (isClientRefusedAsTooOld()) setRefused(true)
    return onClientTooOld(() => setRefused(true))
  }, [])

  useEffect(() => {
    if (!refused) return
    let cancelled = false
    // No `.catch(() => …)` that renders a default: a failed read leaves `requirements` null and the card below
    // falls back to its generic wording, which is still true and still actionable.
    metaApi
      .clientRequirements()
      .then((value) => {
        if (!cancelled) setRequirements(value)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [refused])

  if (!refused) return null

  const platform = typeof window !== "undefined" ? window.__clinicShell?.platform : undefined
  const stores = [
    { key: "android", label: "Google Play", href: requirements?.storeUrls.android },
    { key: "ios", label: "App Store", href: requirements?.storeUrls.ios },
  ].filter((store) => store.href && (platform === undefined || store.key === platform))

  return (
    /*
     * `h-dvh`, so the box is exactly the *dynamic* viewport and scrolls inside itself when a phone's chrome or
     * keyboard shrinks it. `z-50` clears the toaster — the refusal that put this here also raised a toast, and a
     * toast over a takeover is noise.
     *
     * ⚠️ Centred with `my-auto` on the card, NOT `items-center` on the scroller. This is § 11's clipping trap on
     * the vertical axis: `align-items: center` pushes overflow to *both* ends and the top overflow is outside
     * the scrollable region, so on a landscape phone the card's title and icon were unreachable by any means
     * (measured: 354 px of card in a 260 px box, `scrollHeight` 323 — 63 px with no way to reach them). An auto
     * margin resolves to 0 when there is no free space, so it centres and then degrades to top-aligned.
     */
    <div
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="client-version-gate-title"
      className="fixed inset-0 z-50 flex h-dvh items-start justify-center overflow-y-auto bg-background p-4"
    >
      <Card className="my-auto w-full max-w-md">
        <CardHeader className="space-y-3 text-center">
          <div className="mx-auto flex size-14 items-center justify-center rounded-full bg-primary/10">
            <ArrowUpCircle className="size-7 text-primary" aria-hidden="true" />
          </div>
          <CardTitle id="client-version-gate-title">Mise à jour requise</CardTitle>
          <CardDescription>
            Cette version de {PRODUCT_NAME} n&apos;est plus prise en charge par le serveur de votre cabinet.
            Installez la dernière version pour continuer.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {stores.length > 0 ? (
            // `min-h-11` is the coarse-pointer floor, and this is the only control on the screen.
            <div className="space-y-2">
              {stores.map((store) => (
                <Button key={store.key} asChild className="min-h-11 w-full gap-2">
                  <a href={store.href} target="_blank" rel="noreferrer">
                    <ExternalLink className="size-4" aria-hidden="true" />
                    Mettre à jour sur {store.label}
                  </a>
                </Button>
              ))}
            </div>
          ) : (
            // No listing configured yet, or the requirements read failed. Say what to do instead of showing a
            // dead button — « no capability removed by a layout decision » cuts both ways.
            <p className="text-center text-sm text-muted-foreground">
              Contactez le responsable de votre cabinet pour obtenir la dernière version de l&apos;application.
            </p>
          )}

          {requirements?.currentShellVersion && (
            <p className="text-center text-xs text-muted-foreground">
              Version requise : {requirements.currentShellVersion}
              {typeof window !== "undefined" && window.__clinicShell?.version
                ? ` · version installée : ${window.__clinicShell.version}`
                : ""}
            </p>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
