"use client"

import { useEffect, useState } from "react"
import { useRouter } from "next/navigation"
import { useGuide } from "@/components/guide/guide-provider"
import { StaffLeaveAdminPanel } from "@/components/staff-leave/staff-leave-admin-panel"
import { AttendancePanel } from "@/components/team/attendance-panel"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { usePendingLeaveCount } from "@/lib/hooks/use-staff-leave"
import { useUrlFilters, useUrlFilterSeed } from "@/lib/hooks/use-url-filters"
import { useUserStatus } from "@/lib/hooks/use-user-status"
import { USERS_ATTENDANCE_TAB, USERS_LEAVE_TAB, USERS_LEAVE_TAB_EVENT } from "@/lib/staff-leave"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { UserManagement } from "@/components/user-management"
import { PageHeader } from "@/components/ui/page-header"
import { AppLoader } from "@/components/ui/app-loader"
import { useSession } from "@/lib/auth/session"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Lock, ArrowLeft, CalendarClock, CalendarOff, type LucideIcon } from "lucide-react"
import Link from "next/link"
import { EmptyState } from "@/components/ui/empty-state"
import { TEAM_SETTINGS_HREF } from "@/components/team/team-today-list"

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
          <AppLoader />
        ) : isAdmin ? (
          <>
            <PageHeader
              title="Utilisateurs"
              subtitle="Qui accède aux dossiers de ce cabinet, avec quel rôle — et le code d'invitation."
            />
            <UsersTabs />
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


/** « Comptes · Congés · Présences » — for an admin always; a module still off shows the way to switch it on. */
function UsersTabs() {
  const { data: status } = useUserStatus()
  const leaveEnabled = status?.clinic?.leaveRequestsEnabled === true
  const attendanceEnabled = status?.clinic?.attendanceEnabled === true
  const pendingCount = usePendingLeaveCount(leaveEnabled)
  const seed = useUrlFilterSeed()
  const seeded = seed.get("onglet")
  const [tab, setTab] = useState(
    seeded === USERS_LEAVE_TAB || seeded === USERS_ATTENDANCE_TAB ? seeded : "comptes",
  )
  useUrlFilters({ onglet: tab === "comptes" ? null : tab }, Boolean(status))

  useEffect(() => {
    const open = () => setTab(USERS_LEAVE_TAB)
    window.addEventListener(USERS_LEAVE_TAB_EVENT, open)
    return () => window.removeEventListener(USERS_LEAVE_TAB_EVENT, open)
  }, [])

  // « Votre secrétaire » tours « Comptes » and « Congés » ends on « À décider »: each in its own tab, never a hidden one.
  const { running } = useGuide()
  useEffect(() => {
    if (running === "secretary") setTab("comptes")
    else if (running === "conges") setTab(USERS_LEAVE_TAB)
  }, [running])

  return (
    <Tabs value={tab} onValueChange={setTab}>
      <TabsList className="coarse:h-auto">
        <TabsTrigger value="comptes" className="coarse:min-h-11">
          Comptes
        </TabsTrigger>
        <TabsTrigger value={USERS_LEAVE_TAB} className="gap-1.5 coarse:min-h-11">
          Congés
          {leaveEnabled && pendingCount > 0 && (
            <span className="rounded-full bg-primary px-1.5 text-2xs font-semibold text-primary-foreground tabular-nums">
              {pendingCount}
              <span className="sr-only"> en attente</span>
            </span>
          )}
        </TabsTrigger>
        <TabsTrigger value={USERS_ATTENDANCE_TAB} className="coarse:min-h-11">
          Présences
        </TabsTrigger>
      </TabsList>
      <TabsContent value="comptes" className="mt-6">
        <UserManagement />
      </TabsContent>
      <TabsContent value={USERS_LEAVE_TAB} className="mt-6">
        {leaveEnabled ? (
          <StaffLeaveAdminPanel />
        ) : (
          <ModuleOff icon={CalendarOff} title="Les demandes de congé ne sont pas activées" ready={Boolean(status)} />
        )}
      </TabsContent>
      <TabsContent value={USERS_ATTENDANCE_TAB} className="mt-6">
        {attendanceEnabled ? (
          <AttendancePanel scope={{ kind: "all" }} />
        ) : (
          <ModuleOff icon={CalendarClock} title="Le pointage n'est pas activé" ready={Boolean(status)} />
        )}
      </TabsContent>
    </Tabs>
  )
}

/** A team module still off: said, with the way to the switch. Nothing is claimed before the clinic's read lands. */
function ModuleOff({ icon, title, ready }: { icon: LucideIcon; title: string; ready: boolean }) {
  if (!ready) return <AppLoader />
  return (
    <EmptyState
      icon={icon}
      title={title}
      action={
        <Button asChild className="coarse:h-11">
          <Link href={TEAM_SETTINGS_HREF}>Activer dans Paramètres</Link>
        </Button>
      }
    />
  )
}
