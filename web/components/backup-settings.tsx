"use client"

import { useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { DatabaseBackup, FolderDown, CheckCircle2, ShieldAlert } from "lucide-react"
import { backupApi } from "@/lib/api/backup"
import { ApiError } from "@/lib/api/client"

function formatSize(bytes: number): string {
  if (bytes <= 0) return "0 o"
  const units = ["o", "Ko", "Mo", "Go", "To"]
  const i = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1)
  return `${(bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 1)} ${units[i]}`
}

/**
 * Admin-only "Sauvegarde" card (US-8 / FR-G / AC-8.1–8.3). One-click backup of the database + files to a
 * destination folder. Reports success with the exact path and size, or a clear failure reason — never
 * silent (AC-8.2/8.3). Mounted by <see cref="ClinicSettings"/> only in Local mode for admins.
 */
export function BackupSettings() {
  const [destination, setDestination] = useState("")
  const [working, setWorking] = useState(false)
  const [lastResult, setLastResult] = useState<{ path: string; size: string; warning?: string | null } | null>(
    null,
  )

  // Guard against setState after unmount: a backup can be long-running and the operator may navigate away
  // from /settings mid-request (Finding 18 — matches the guarded-async pattern used in session.tsx).
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  const handleBackup = async () => {
    setWorking(true)
    setLastResult(null)
    try {
      const result = await backupApi.backupNow(destination)
      if (mounted.current) {
        setLastResult({
          path: result.destinationPath,
          size: formatSize(result.sizeBytes),
          warning: result.warning,
        })
      }

      // AC-14.3: the backup succeeded, but if it could not be access-restricted the admin has to know now —
      // a warning only in the server log is a warning nobody reads. Kept as a distinct, longer-lived toast
      // rather than folded into the success description, which is easy to dismiss without reading.
      if (result.warning) {
        toast.warning("Sauvegarde non protégée", { description: result.warning, duration: 12000 })
      } else {
        toast.success("Sauvegarde terminée", {
          description: `${result.destinationPath} (${formatSize(result.sizeBytes)})`,
        })
      }
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "La sauvegarde a échoué."
      toast.error("Échec de la sauvegarde", { description: message })
    } finally {
      if (mounted.current) {
        setWorking(false)
      }
    }
  }

  return (
    <Card className="border border-gray-200 dark:border-slate-800">
      <CardHeader className="pb-3">
        <div className="flex items-center gap-2">
          <div className="w-1 h-6 bg-blue-600 rounded-full" />
          <CardTitle className="text-base flex items-center gap-2">
            <DatabaseBackup className="w-4 h-4 text-blue-600" />
            Sauvegarde
          </CardTitle>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-xs text-muted-foreground">
          Sauvegarde la base de données et les fichiers de la clinique dans un dossier daté. Laissez le champ
          vide pour utiliser le dossier de sauvegarde par défaut du serveur.
        </p>

        <div className="space-y-1">
          <Label htmlFor="backup-destination" className="text-xs font-medium">
            Dossier de destination
          </Label>
          <Input
            id="backup-destination"
            placeholder="Ex. D:\\Sauvegardes clinique"
            value={destination}
            onChange={(e) => setDestination(e.target.value)}
            disabled={working}
            className="h-8 text-sm"
          />
        </div>

        <div className="flex justify-end">
          <Button
            onClick={handleBackup}
            size="sm"
            className="h-8 text-xs bg-blue-600 hover:bg-blue-700"
            disabled={working}
          >
            <FolderDown className="w-3.5 h-3.5 mr-1" />
            {working ? "Sauvegarde en cours..." : "Sauvegarder maintenant"}
          </Button>
        </div>

        {lastResult && (
          <div className="flex items-start gap-2 rounded-lg border border-green-200 dark:border-green-800 bg-green-50 dark:bg-green-950/30 p-3">
            <CheckCircle2 className="w-4 h-4 text-green-600 dark:text-green-400 mt-0.5 shrink-0" />
            <div className="space-y-0.5">
              <p className="text-xs font-medium text-green-900 dark:text-green-100">Dernière sauvegarde</p>
              <p className="text-xs text-green-700 dark:text-green-300 break-all">{lastResult.path}</p>
              <p className="text-[10px] text-green-600 dark:text-green-400">{lastResult.size}</p>
            </div>
          </div>
        )}

        {/* AC-14.3: persists next to the success panel, so the exposure stays visible after the toast is
            gone. The backup DID work — this is about where it landed, not a failure. */}
        {lastResult?.warning && (
          <div className="flex items-start gap-2 rounded-lg border border-amber-200 dark:border-amber-800 bg-amber-50 dark:bg-amber-950/30 p-3">
            <ShieldAlert className="w-4 h-4 text-amber-600 dark:text-amber-400 mt-0.5 shrink-0" />
            <div className="space-y-0.5">
              <p className="text-xs font-medium text-amber-900 dark:text-amber-100">Sauvegarde non protégée</p>
              <p className="text-xs text-amber-700 dark:text-amber-300">{lastResult.warning}</p>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
