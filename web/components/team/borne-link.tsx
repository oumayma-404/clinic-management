"use client"

import Link from "next/link"
import { useEffect, useState } from "react"
import { Fingerprint } from "lucide-react"
import { Button } from "@/components/ui/button"
import { pointageApi } from "@/lib/api/pointage"
import { cn } from "@/lib/utils"

/**
 * One read per page load: the header remounts on every navigation, and whether this device is a borne does not
 * change between two clicks. A failed read means « not a borne » — the entry is a convenience, never the only door.
 */
let borneProbe: Promise<boolean> | null = null
const isThisDeviceABorne = () =>
  (borneProbe ??= pointageApi
    .status()
    .then((status) => status.isBorne && status.enabled)
    .catch(() => false))

/** Forgets the cached answer — after « Faire de cet appareil une borne » or « Retirer ». */
export function forgetBorneProbe() {
  borneProbe = null
}

/** « Pointage » (`pointage-borne` AC-1): on /login and in the top bar, only on a device that is a borne. */
export function BorneLink({ variant }: { variant: "login" | "header" }) {
  const [isBorne, setIsBorne] = useState(false)

  useEffect(() => {
    let cancelled = false
    void isThisDeviceABorne().then((answer) => !cancelled && setIsBorne(answer))
    return () => {
      cancelled = true
    }
  }, [])

  if (!isBorne) return null

  if (variant === "login") {
    return (
      <Button asChild variant="outline" className="min-h-11 w-full gap-2">
        <Link href="/pointage">
          <Fingerprint className="size-4" aria-hidden="true" />
          Pointage
        </Link>
      </Button>
    )
  }

  return (
    <Button asChild variant="ghost" size="sm" className={cn("touch-target gap-1.5 print:hidden")}>
      <Link href="/pointage" aria-label="Pointage">
        <Fingerprint className="size-4" aria-hidden="true" />
        <span className="hidden lg:inline">Pointage</span>
      </Link>
    </Button>
  )
}
