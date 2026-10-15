"use client"

import { useId } from "react"
import { BellRing, Volume2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import { useAlertPreferences } from "@/lib/staff-chat/alerts"

/** « Son » and browser notifications for this person on this device (staff-chat AC-4.4, AC-4.6). */
export function AlertSettings() {
  const { muted, setMuted, notifications, askNotifications, shell } = useAlertPreferences()
  // In the Windows app its own notifications stand in for the browser's (EC-10 when it is too old to have them).
  const browser = shell === "none"
  const soundId = useId()

  return (
    <div className="flex shrink-0 flex-wrap items-center gap-x-4 gap-y-2 border-t border-border px-4 py-2.5">
      <div className="flex items-center gap-2">
        <Switch id={soundId} checked={!muted} onCheckedChange={(on) => setMuted(!on)} />
        <Label htmlFor={soundId} className="text-sm font-normal">
          Son des messages
        </Label>
      </div>
      {shell === "outdated" && (
        <p className="text-xs text-muted-foreground">Mettez à jour l&apos;application Windows pour recevoir ses notifications.</p>
      )}
      {browser && notifications === "default" && (
        <Button variant="outline" size="sm" className="coarse:min-h-11" onClick={() => void askNotifications()}>
          <BellRing className="h-4 w-4" aria-hidden="true" />
          Activer les notifications
        </Button>
      )}
      {browser && notifications === "granted" && <p className="text-xs text-muted-foreground">Notifications activées</p>}
      {browser && notifications === "denied" && (
        <p className="text-xs text-muted-foreground">Notifications bloquées par le navigateur</p>
      )}
    </div>
  )
}

/** Shown while the browser keeps the chat silent, until one press lets it ring (AC-4.6). */
export function AudioBlockedBar() {
  const { audioBlocked, enableAudio } = useAlertPreferences()
  if (!audioBlocked) return null
  return (
    <div className="shrink-0 border-b border-border bg-warning-wash px-4 py-2">
      <Button variant="outline" size="sm" className="w-full bg-card coarse:min-h-11" onClick={() => void enableAudio()}>
        <Volume2 className="h-4 w-4" aria-hidden="true" />
        Son bloqué — activer
      </Button>
    </div>
  )
}
