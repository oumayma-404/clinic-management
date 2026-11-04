"use client"

import type React from "react"
import { Suspense, useEffect, useState } from "react"
import { useRouter, useSearchParams } from "next/navigation"
import { ArrowRight, Loader2 } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card"
import { useDemo } from "@/lib/demo/demo-context"
import { useSession } from "@/lib/auth/session"
import { PRODUCT_NAME } from "@/lib/brand"

/**
 * The public demo's front door (`demo-cabinet` US-1): one button, no password. Also where a demo session lands when
 * it ends (`?expired=1`), and what a visitor meets while no cabinet is ready yet.
 */
export default function DemoEntryPage() {
  return (
    <Suspense fallback={<Shell><Waiting /></Shell>}>
      <DemoEntry />
    </Suspense>
  )
}

function DemoEntry() {
  const router = useRouter()
  const params = useSearchParams()
  const { isDemo, signupUrl } = useDemo()
  const { user, isLoading } = useSession()
  const [entering, setEntering] = useState(false)
  const [refusal, setRefusal] = useState<string | null>(null)
  const expired = params.get("expired") === "1"

  // AC-1.6: a visitor already inside goes straight back to the cabinet.
  useEffect(() => {
    if (isDemo && !isLoading && user) router.replace("/")
  }, [isDemo, isLoading, user, router])

  const enter = async () => {
    if (entering) return
    setEntering(true)
    setRefusal(null)
    try {
      const res = await fetch("/bff/auth/demo-session", { method: "POST" })
      if (res.ok) {
        // A full navigation, like a sign-in: the session provider must start from the new cookie.
        window.location.href = "/"
        return
      }
      const body = await res.json().catch(() => null)
      setRefusal(
        typeof body?.error === "string" && body.error
          ? body.error
          : "La démo est momentanément indisponible. Réessayez dans un instant.",
      )
    } catch {
      setRefusal("Impossible de joindre la démo. Vérifiez votre connexion et réessayez.")
    }
    setEntering(false)
  }

  if (isDemo === null) {
    return <Shell><Waiting /></Shell>
  }

  if (!isDemo) {
    return (
      <Shell>
        <Card className="border-primary/20 shadow-lg">
          <CardHeader className="space-y-2 text-center">
            <h1 className="text-2xl leading-none font-semibold text-accent-foreground">Démonstration non disponible ici</h1>
            <CardDescription>Cette adresse est celle d&apos;un cabinet, pas de la démonstration APEXA.</CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild className="w-full coarse:h-11">
              <a href="/login">Aller à la connexion</a>
            </Button>
          </CardContent>
        </Card>
      </Shell>
    )
  }

  return (
    <Shell>
      <Card className="border-primary/20 shadow-lg">
        <CardHeader className="space-y-3 text-center">
          <img
            src="/apexa-lockup.svg"
            alt={PRODUCT_NAME}
            width={199}
            height={60}
            className="mx-auto mb-1 h-14 w-auto dark:hidden"
          />
          <img
            src="/apexa-lockup-dark.svg"
            alt=""
            width={199}
            height={60}
            className="mx-auto mb-1 hidden h-14 w-auto dark:block"
          />
          <h1 className="text-2xl leading-none font-bold text-accent-foreground">Cabinet de démonstration</h1>
          <CardDescription className="text-pretty">
            Visitez un cabinet dentaire déjà rempli : agenda, dossiers patients, odontogramme, devis, caisse et
            documents. Rien de ce que vous faites n&apos;est enregistré.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          {expired && (
            <p role="status" className="rounded-md bg-muted px-3 py-2 text-center text-sm text-muted-foreground">
              La démo a expiré. Entrez à nouveau pour continuer.
            </p>
          )}
          {refusal && (
            <p role="alert" className="rounded-md bg-warning-wash px-3 py-2 text-center text-sm text-warning-ink">
              {refusal}
            </p>
          )}
          <Button className="w-full coarse:h-11" onClick={enter} disabled={entering}>
            {entering ? (
              <Loader2 className="size-4 animate-spin" aria-hidden="true" />
            ) : (
              <ArrowRight className="size-4" aria-hidden="true" />
            )}
            {entering ? "Ouverture du cabinet…" : "Entrer dans la démo"}
          </Button>
          <Button asChild variant="outline" className="w-full coarse:h-11">
            <a href={signupUrl} target="_blank" rel="noopener noreferrer">
              Créer mon cabinet
            </a>
          </Button>
        </CardContent>
      </Card>
    </Shell>
  )
}

function Waiting() {
  return (
    <div className="flex items-center justify-center gap-3 text-muted-foreground" role="status">
      <Loader2 className="size-5 animate-spin" aria-hidden="true" />
      <p>Chargement…</p>
    </div>
  )
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-dvh justify-start overflow-y-auto bg-background p-4 sm:p-6">
      <div className="mx-auto my-auto w-full max-w-md">{children}</div>
    </div>
  )
}
