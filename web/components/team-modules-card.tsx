"use client"

import { useEffect, useState } from "react"
import Link from "next/link"
import { toast } from "sonner"
import { Users } from "lucide-react"
import { useGuide } from "@/components/guide/guide-provider"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Label } from "@/components/ui/label"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { STATUS_TONE_CLASS } from "@/components/ui/status-tone"
import { Switch } from "@/components/ui/switch"
import { AttendanceSettingsCard } from "@/components/team/attendance-settings-card"
import { TEAM_SETTINGS_ANCHOR } from "@/components/team/team-today-list"
import { clinicsApi } from "@/lib/api/clinics"
import type { TeamLeaveMemberDto } from "@/lib/api/team"
import { showErrorToast } from "@/lib/errors"
import { todayLocalIso } from "@/lib/format"
import { useTeamLeaveOverview } from "@/lib/hooks/use-staff-leave"
import { useFetchUserStatus, useUserStatus } from "@/lib/hooks/use-user-status"
import { STAFF_LEAVE_ACCRUAL_LABEL, firstOfLabel, staffDaysShort } from "@/lib/staff-leave"
import { cn } from "@/lib/utils"
import { ZONES, zoneChipClass } from "@/lib/zones"

type TeamModule = "leaveRequests" | "attendance" | "attendanceLock"

const SAVED: Record<TeamModule, [on: string, off: string]> = {
  leaveRequests: ["Demandes de congé activées.", "Demandes de congé désactivées."],
  attendance: ["Pointage activé.", "Pointage désactivé."],
  attendanceLock: ["Blocage de l'app activé.", "Blocage de l'app désactivé."],
}

/**
 * Paramètres → « Équipe »: the secretary congés and the pointage, each off until an admin turns it on. Admins only
 * (the caller gates). `onSaved` hands back the clinic's new version — the switch writes the same row Paramètres'
 * sections save.
 */
export function TeamModulesCard({ onSaved }: { onSaved?: (version: number) => void } = {}) {
  const { data: status } = useUserStatus()
  const fetchUserStatus = useFetchUserStatus()
  const clinic = status?.clinic
  const leaveOn = clinic?.leaveRequestsEnabled === true
  const attendanceOn = clinic?.attendanceEnabled === true
  // On by default once the pointage is on; a cabinet that never answered reads « on », as the server does.
  const lockOn = clinic?.attendanceLockEnabled !== false
  const [saving, setSaving] = useState<TeamModule | null>(null)

  // Arriving on `#equipe` (or sent there from another screen): bring the card into view once the page has rendered it.
  useEffect(() => {
    const reveal = () => {
      if (window.location.hash !== `#${TEAM_SETTINGS_ANCHOR}`) return
      requestAnimationFrame(() =>
        document.getElementById(TEAM_SETTINGS_ANCHOR)?.scrollIntoView({ behavior: "smooth", block: "start" }),
      )
    }
    reveal()
    window.addEventListener("hashchange", reveal)
    return () => window.removeEventListener("hashchange", reveal)
  }, [])

  const toggle = async (module: TeamModule, next: boolean) => {
    if (!clinic || saving) return
    setSaving(module)
    try {
      // No version: one idempotent switch, where the last press is the intent (« Mode discret »'s shape).
      const saved = await clinicsApi.setTeamModules({ [module]: next })
      onSaved?.(saved.version)
      toast.success(SAVED[module][next ? 0 : 1])
    } catch (err) {
      showErrorToast(err, "Le réglage n'a pas pu être enregistré.")
    } finally {
      await fetchUserStatus().catch(() => undefined)
      setSaving(null)
    }
  }

  return (
    <Card id={TEAM_SETTINGS_ANCHOR} className="scroll-mt-20">
      <CardContent className="space-y-3 p-4">
        <div className="flex items-center gap-2.5">
          <span aria-hidden="true" className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}>
            <Users className="size-4" strokeWidth={1.75} />
          </span>
          <p className="text-sm font-medium">Équipe</p>
        </div>
        <div className="flex items-center justify-between gap-3 border-t pt-3" data-guide="conges-switch">
          <Label htmlFor="team-leave-requests" className="font-normal">
            Demandes de congé des secrétaires
          </Label>
          <Switch
            id="team-leave-requests"
            checked={leaveOn}
            disabled={!clinic || saving !== null}
            onCheckedChange={(next) => void toggle("leaveRequests", next)}
          />
        </div>
        {leaveOn && <LeaveRights />}
        <div className="flex items-center justify-between gap-3 border-t pt-3" data-guide="pointage-switch">
          <Label htmlFor="team-attendance" className="font-normal">
            Pointage des arrivées et des départs
          </Label>
          <Switch
            id="team-attendance"
            checked={attendanceOn}
            disabled={!clinic || saving !== null}
            onCheckedChange={(next) => void toggle("attendance", next)}
          />
        </div>
        {attendanceOn && (
          <div className="border-t pt-4">
            <AttendanceSettingsCard
              lockOn={lockOn}
              lockSwitch={
                <Switch
                  id="team-attendance-lock"
                  checked={lockOn}
                  disabled={!clinic || saving !== null}
                  onCheckedChange={(next) => void toggle("attendanceLock", next)}
                />
              }
            />
          </div>
        )}
      </CardContent>
    </Card>
  )
}

/** « 24 j par an », « 2 j par mois à partir du 1er décembre » — her rule in a few words; null when none is set. */
function ruleWords(member: TeamLeaveMemberDto, todayKey: string): string | null {
  const rule = member.balance.rule ?? member.balance.nextRule
  if (!rule) return null
  const words = `${staffDaysShort(rule.days)} ${STAFF_LEAVE_ACCRUAL_LABEL[rule.accrual]}`
  return rule.since > todayKey ? `${words} à partir du ${firstOfLabel(rule.since)}` : words
}

/**
 * Under the congés switch: each active secretary and her « Droit à congé » — the rule, or « Droit à définir » with
 * the way to her file — and the « Congés » tour.
 */
function LeaveRights() {
  const team = useTeamLeaveOverview(true)
  const { start } = useGuide()
  const todayKey = todayLocalIso()

  if (team.failed && !team.data) {
    return (
      <LoadFailureNotice variant="inline" message="Les droits à congé n'ont pas pu être chargés." onRetry={team.refetch} />
    )
  }
  if (!team.data) return <div className="h-11 animate-pulse rounded-md bg-muted/50" aria-hidden="true" />
  const members = team.data.members

  return (
    <section aria-labelledby="team-leave-rights" className="space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id="team-leave-rights" className="text-sm font-medium">
          Droit à congé
        </h3>
        {members.length > 0 && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="min-h-9 coarse:min-h-11"
            onClick={() => start("conges")}
            aria-label="Me guider : congés"
          >
            Me guider
          </Button>
        )}
      </div>
      {members.length === 0 ? (
        <p className="text-xs text-muted-foreground">Aucune secrétaire active</p>
      ) : (
        <ul className="divide-y rounded-md border" data-guide="conges-people">
          {members.map((member) => {
            const href = `/users/${encodeURIComponent(member.userId)}`
            const words = ruleWords(member, todayKey)
            return (
              <li key={member.userId} className="flex flex-wrap items-center gap-x-2 gap-y-1 px-3 py-1.5 text-sm">
                <Link
                  href={href}
                  className="inline-flex min-h-9 min-w-0 items-center font-medium underline-offset-2 [overflow-wrap:anywhere] hover:underline coarse:min-h-11"
                >
                  {member.fullName}
                </Link>
                {words ? (
                  <span className="text-muted-foreground">— {words}</span>
                ) : (
                  <>
                    <span className={cn("rounded-full px-2 py-0.5 text-xs font-medium", STATUS_TONE_CLASS.active)}>
                      {member.balance.hasAccount ? "Aucune règle" : "Droit à définir"}
                    </span>
                    <Button asChild variant="outline" size="sm" className="ms-auto coarse:h-11">
                      <Link href={href} aria-label={`Définir le droit à congé de ${member.fullName}`}>
                        Définir
                      </Link>
                    </Button>
                  </>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
