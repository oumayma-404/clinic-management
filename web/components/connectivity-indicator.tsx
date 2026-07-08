"use client"

import type React from "react"
import { Wifi, WifiOff, ServerOff } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"
import { useConnectivity } from "@/lib/connectivity/connectivity"
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
    className = "border-transparent bg-destructive text-white"
  } else if (!internetReachable) {
    icon = <WifiOff className="h-3.5 w-3.5" />
    label = "Hors ligne"
    description = "Pas de connexion internet. L'assistant IA et Google Agenda sont désactivés ; les autres fonctions restent disponibles."
    className = "border-transparent bg-amber-100 text-amber-800 dark:bg-amber-500/20 dark:text-amber-400"
  } else {
    icon = <Wifi className="h-3.5 w-3.5" />
    label = "En ligne"
    description = "Le serveur et internet sont accessibles. Toutes les fonctions sont disponibles."
    className = "border-transparent bg-emerald-100 text-emerald-800 dark:bg-emerald-500/20 dark:text-emerald-400"
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
