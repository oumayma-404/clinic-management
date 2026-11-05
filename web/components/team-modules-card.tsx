"use client"

import { useState } from "react"
import { toast } from "sonner"
import { Users } from "lucide-react"
import { Card, CardContent } from "@/components/ui/card"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import { AttendanceSettingsCard } from "@/components/team/attendance-settings-card"
import { clinicsApi } from "@/lib/api/clinics"
import { showErrorToast } from "@/lib/errors"
import { useFetchUserStatus, useUserStatus } from "@/lib/hooks/use-user-status"
import { ZONES, zoneChipClass } from "@/lib/zones"

type TeamModule = "leaveRequests" | "attendance"

const SAVED: Record<TeamModule, [on: string, off: string]> = {
  leaveRequests: ["Demandes de congé activées.", "Demandes de congé désactivées."],
  attendance: ["Pointage activé.", "Pointage désactivé."],
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
  const [saving, setSaving] = useState<TeamModule | null>(null)

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
    <Card>
      <CardContent className="space-y-3 p-4">
        <div className="flex items-center gap-2.5">
          <span aria-hidden="true" className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}>
            <Users className="size-4" strokeWidth={1.75} />
          </span>
          <p className="text-sm font-medium">Équipe</p>
        </div>
        <div className="flex items-center justify-between gap-3 border-t pt-3">
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
        <div className="flex items-center justify-between gap-3 border-t pt-3">
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
            <AttendanceSettingsCard />
          </div>
        )}
      </CardContent>
    </Card>
  )
}
