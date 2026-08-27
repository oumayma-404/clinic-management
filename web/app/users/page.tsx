"use client"

import { useRouter } from "next/navigation"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { UserManagement } from "@/components/user-management"
import { PageHeader } from "@/components/ui/page-header"
import { useSession } from "@/lib/auth/session"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Lock, ArrowLeft } from "lucide-react"

export default function UsersPage() {
  const { user, isLoading } = useSession()
  const router = useRouter()
  const isAdmin = user?.role === "admin"

  return (
    <ClinicGuard>
      {/*
        The admin view gets the shell's default width and gutter, like every other page — `width="none"
        gutter={false}` made this one of only two screens with no width limit at all, and left the route with
        no page title.

        `width="none"` survives for the refusal branch alone: that card centres itself with `min-h-full`, which
        resolves against `<main>` and collapses to `auto` the moment an auto-height wrapper is inserted.

        ⚠️ Residual, needing a file this change does not own: `components/user-management.tsx` still opens with
        `min-h-full bg-gray-50 dark:bg-slate-950` around its own `mx-auto max-w-5xl p-4`, so that grey surface
        now reads as an inset slab and its padding stacks on the shell's. Its wrapper should be deleted.
      */}
      <AppShell width={isAdmin ? "7xl" : "none"} gutter={isAdmin} contentClassName={isAdmin ? "space-y-6" : undefined}>
        {isLoading ? (
          <p className="p-8 text-center text-muted-foreground">Chargement…</p>
        ) : isAdmin ? (
          <>
            <PageHeader
              title="Utilisateurs"
              subtitle="Qui accède aux dossiers de ce cabinet, avec quel rôle — et le code d'invitation."
            />
            <UserManagement />
          </>
        ) : (
          // AC-5.4: the user-management screen is only reachable by an admin.
          <div className="flex min-h-full items-center justify-center p-6">
            <Card className="w-full max-w-md">
              <CardHeader className="space-y-3 text-center">
                {/* Tokens, not `red-*` literals — and no hand-maintained `dark:` twin, since
                    `--destructive-wash` / `--destructive` already carry both themes. */}
                <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-destructive-wash">
                  <Lock className="h-7 w-7 text-destructive" />
                </div>
                <CardTitle>Administrateurs uniquement</CardTitle>
                <CardDescription>La gestion des utilisateurs est réservée aux administrateurs du cabinet.</CardDescription>
              </CardHeader>
              <CardContent>
                <Button variant="outline" className="w-full gap-2" onClick={() => router.push("/")}>
                  <ArrowLeft className="h-4 w-4" />
                  Retour au tableau de bord
                </Button>
              </CardContent>
            </Card>
          </div>
        )}
      </AppShell>
    </ClinicGuard>
  )
}
