"use client"

import type React from "react"
import { Wifi, WifiOff, ServerOff } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import { statusToneClass } from "@/components/ui/status-tone"
import { cn } from "@/lib/utils"

/**
 * Header status affordance. Distinguishes three states (AC-6.1, FR-D3): online / no internet (core app fine) /
 * server unreachable.
 *
 * ⚠️ **The gate is what this deployment can actually say, not which auth mode it runs** (AC-62/AC-63). It used to
 * return null unless `isLocal`, a flag derived from `AUTH_MODE` — which reads `local` on the hosted multi-tenant
 * backend too, so the badge claimed three states there while the probe behind them 404s. Now: an unreachable
 * server is surfaced on **every** deployment, the two egress states only where the server publishes a reading,
 * and a deployment with no probe and a healthy server renders **nothing** — which keeps the Cloud header
 * byte-for-byte unchanged in the happy path, as before.
 */
export function ConnectivityIndicator() {
  const { serverReachable, internetReachable, egressSignalAvailable } = useConnectivity()

  // Nothing to say: the server answers and this deployment publishes no egress signal to report on.
  if (serverReachable && !egressSignalAvailable) return null

  let icon: React.ReactNode
  let label: string
  let description: string
  let className: string

  if (!serverReachable) {
    icon = <ServerOff className="h-3.5 w-3.5" />
    label = "Serveur injoignable"
    // Never « réseau local » (AC-64) — the same server is reached over a LAN, Wi-Fi or a mobile network.
    description = "Impossible de joindre le serveur. Vérifiez votre connexion, puis réessayez."
    /*
     * Deliberately NOT one of the six status tones: every tone is a tinted wash, and this is the one state
     * where nothing works at all. A FILLED destructive badge keeps it visibly louder than the amber
     * "no internet" case below it — the severity difference is the point of having three states.
     */
    className = "border-transparent bg-destructive text-destructive-foreground"
  } else if (!internetReachable) {
    icon = <WifiOff className="h-3.5 w-3.5" />
    label = "Hors ligne"
    description = "Le serveur n'a pas accès à internet. L'assistant IA et Google Agenda sont désactivés ; les autres fonctions restent disponibles."
    // `active` = the amber wash + `--warning-ink`. The literal pair it replaces used `text-amber-800` on
    // `bg-amber-100`, i.e. exactly the pairing `--warning-ink` exists for.
    className = statusToneClass("active")
  } else {
    icon = <Wifi className="h-3.5 w-3.5" />
    label = "En ligne"
    description = "Le serveur et internet sont accessibles. Toutes les fonctions sont disponibles."
    className = statusToneClass("positive")
  }

  return (
    <TooltipProvider>
      <Tooltip>
        <TooltipTrigger asChild>
          <Badge className={cn("gap-1 cursor-default", className)}>
            {icon}
            {label}
          </Badge>
        </TooltipTrigger>
        <TooltipContent className="max-w-xs">{description}</TooltipContent>
      </Tooltip>
    </TooltipProvider>
  )
}
