"use client"

import type React from "react"
import { Wifi, WifiOff, ServerOff } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import { statusToneClass } from "@/components/ui/status-tone"
import { cn } from "@/lib/utils"

/**
 * Header status affordance for Local (offline-LAN) mode. Distinguishes three states (AC-6.1, FR-D3):
 * online / no internet (core app fine) / server unreachable. Renders nothing in Cloud mode so the
 * Cloud header stays byte-for-byte unchanged.
 */
export function ConnectivityIndicator() {
  const { isLocal, serverReachable, internetReachable } = useConnectivity()

  if (!isLocal) return null

  let icon: React.ReactNode
  let label: string
  let description: string
  let className: string

  if (!serverReachable) {
    icon = <ServerOff className="h-3.5 w-3.5" />
    label = "Serveur injoignable"
    description = "Impossible de joindre le serveur de la clinique. Vérifiez la connexion au réseau local."
    /*
     * Deliberately NOT one of the six status tones: every tone is a tinted wash, and this is the one state
     * where nothing works at all. A FILLED destructive badge keeps it visibly louder than the amber
     * "no internet" case below it — the severity difference is the point of having three states.
     */
    className = "border-transparent bg-destructive text-destructive-foreground"
  } else if (!internetReachable) {
    icon = <WifiOff className="h-3.5 w-3.5" />
    label = "Hors ligne"
    description = "Pas de connexion internet. L'assistant IA et Google Agenda sont désactivés ; les autres fonctions restent disponibles."
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
