"use client"

import { useEffect } from "react"
import { usePathname, useRouter } from "next/navigation"
import { toast } from "sonner"
import { useSession } from "@/lib/auth/session"
import {
  cameraShell,
  clearDetected,
  installShellCamera,
  useShellCamera,
} from "@/lib/photo-import/shell-camera"

const IMPORT_PATH = "/photos/import"

const importUrl = (sourceKey: string | null) =>
  sourceKey ? `${IMPORT_PATH}?appareil=${encodeURIComponent(sourceKey)}` : IMPORT_PATH

/**
 * The page's end of the Windows app's camera link (`features/camera-photo-import`, phase 2), mounted once for the
 * whole app: it listens for what the shell posts and says so wherever the dentist is.
 *
 * - « Appareil photo détecté » — a card or a camera was plugged in (the switch is on in Paramètres): « Importer »
 *   opens the import screen on that device, « Ignorer cet appareil » stops the notices for it on this PC.
 * - Photos that arrive while another screen is open wait, with a toast pointing at them.
 *
 * ⚠️ **Signed-in only.** Recording a kept original is an authenticated write; on the sign-in screen the shell's
 * messages wait in its queue until there is somebody to send them as.
 *
 * ⚠️ Renders nothing, and does nothing at all outside the Windows app 1.8.
 */
export function ShellCameraBridge() {
  const { user } = useSession()
  const router = useRouter()
  const pathname = usePathname()
  const { detected, batch } = useShellCamera()
  const signedIn = Boolean(user?.email)

  useEffect(() => {
    if (!signedIn) return
    return installShellCamera((sourceKey) => router.push(importUrl(sourceKey)))
  }, [signedIn, router])

  useEffect(() => {
    if (!detected) return
    toast("Appareil photo détecté", {
      id: "camera-detected",
      description: detected.label,
      duration: 60_000,
      action: {
        label: "Importer",
        onClick: () => {
          clearDetected()
          router.push(importUrl(detected.sourceKey))
        },
      },
      cancel: {
        label: "Ignorer cet appareil",
        onClick: () => {
          clearDetected()
          cameraShell()?.ignoreCamera(detected.sourceKey)
        },
      },
    })
  }, [detected, router])

  useEffect(() => {
    if (!batch || pathname === IMPORT_PATH) return
    const count = batch.photos.length
    toast("Photos de l'appareil prêtes", {
      id: "camera-batch",
      description: count === 1 ? "1 photo à ranger" : `${count} photos à ranger`,
      duration: 60_000,
      action: { label: "Ranger", onClick: () => router.push(IMPORT_PATH) },
    })
  }, [batch, pathname, router])

  return null
}
