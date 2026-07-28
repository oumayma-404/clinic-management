"use client"

import { useCallback, useEffect, useState } from "react"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { BellRing, CheckCircle2, Clock, Loader2, Send, Settings } from "lucide-react"
import { recallsApi } from "@/lib/api/recalls"
import type { RecallDto, RecallSettingsDto } from "@/lib/api/types"
import { useSession } from "@/lib/auth/session"

// fr-TN-style short date; falls back to a placeholder for null/undefined ISO strings.
function formatDate(iso: string | null | undefined, fallback = "—"): string {
  if (!iso) return fallback
  try {
    return format(parseISO(iso), "d MMM yyyy", { locale: fr })
  } catch {
    return fallback
  }
}

const SNOOZE_DAYS = 30

// Inline settings dialog: view + change the recall interval (in months).
function RecallSettingsDialog({
  settings,
  onSaved,
}: {
  settings: RecallSettingsDto | null
  onSaved: (next: RecallSettingsDto) => void
}) {
  const [open, setOpen] = useState(false)
  const [value, setValue] = useState("")
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Reset the input to the current interval whenever the dialog opens.
  useEffect(() => {
    if (open) {
      setValue(settings != null ? String(settings.intervalMonths) : "")
      setError(null)
    }
  }, [open, settings])

  const handleSave = async () => {
    const months = Number(value)
    if (value.trim() === "" || !Number.isFinite(months) || months < 1) {
      setError("Entrez un nombre de mois supérieur ou égal à 1")
      return
    }
    try {
      setSaving(true)
      setError(null)
      const next = await recallsApi.setSettings(Math.trunc(months))
      toast.success("Intervalle de relance mis à jour")
      onSaved(next)
      setOpen(false)
    } catch (err) {
      showErrorToast(err, "Échec de l'enregistrement des paramètres")
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="outline" className="gap-2">
          <Settings className="h-4 w-4" />
          Intervalle
          {settings != null && (
            <Badge variant="secondary" className="ml-1">
              {settings.intervalMonths} mois
            </Badge>
          )}
        </Button>
      </DialogTrigger>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Intervalle de relance</DialogTitle>
          <DialogDescription>
            Un patient est considéré à relancer lorsque sa dernière visite dépasse cet intervalle.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-2">
          <Label htmlFor="intervalMonths">
            Intervalle (mois) <span className="text-destructive">*</span>
          </Label>
          <Input
            id="intervalMonths"
            type="number"
            min="1"
            step="1"
            placeholder="6"
            value={value}
            onChange={(e) => setValue(e.target.value)}
          />
          {error && <p className="text-xs text-destructive">{error}</p>}
        </div>

        <DialogFooter className="gap-2">
          <Button type="button" variant="outline" onClick={() => setOpen(false)} disabled={saving}>
            Annuler
          </Button>
          <Button type="button" onClick={handleSave} disabled={saving}>
            {saving ? "Enregistrement..." : "Enregistrer"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export default function RecallsPage() {
  const { user } = useSession()
  const isAdmin = user?.role === "admin"

  const [recalls, setRecalls] = useState<RecallDto[]>([])
  const [settings, setSettings] = useState<RecallSettingsDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  // The patient whose row action is in flight — disables that row's buttons.
  const [busyPatientId, setBusyPatientId] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const [list, currentSettings] = await Promise.all([recallsApi.list(), recallsApi.getSettings()])
      setRecalls(list)
      setSettings(currentSettings)
    } catch (err) {
      const message = getErrorMessage(err, "Échec du chargement des relances")
      setError(message)
      toast.error(message)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  // Run a per-row action, then refetch the list. Shared by the three action buttons.
  //
  // AC-P3.3 — the failure path is the point here. « Relancer » used to be incapable of failing: the command
  // returned success even when no channel was configured, so this toasted « Rappel envoyé à … » over a send
  // that never happened. The server's refusal is now a French sentence naming what to do next, so it is shown
  // for long enough to read and act on rather than the default couple of seconds.
  const runAction = useCallback(
    async (patientId: string, action: () => Promise<void>, successMessage: string, fallbackMessage: string) => {
      try {
        setBusyPatientId(patientId)
        await action()
        toast.success(successMessage)
        await load()
      } catch (err) {
        toast.error(getErrorMessage(err, fallbackMessage), { duration: 8000 })
        // Refetch anyway: a refusal leaves the patient on the list, and the row's own state (« dernier
        // contact ») may have been changed by a peer in the meantime.
        await load()
      } finally {
        setBusyPatientId(null)
      }
    },
    [load],
  )

  const handleMarkContacted = (recall: RecallDto) =>
    runAction(
      recall.patientId,
      () => recallsApi.markContacted(recall.patientId, recall.reason),
      `${recall.patientName} marqué comme contacté`,
      "Échec de l'enregistrement du contact",
    )

  const handleSnooze = (recall: RecallDto) =>
    runAction(
      recall.patientId,
      () => recallsApi.snooze(recall.patientId, SNOOZE_DAYS, recall.reason),
      `Relance reportée de ${SNOOZE_DAYS} jours`,
      "Échec du report de la relance",
    )

  const handleSend = (recall: RecallDto) =>
    runAction(
      recall.patientId,
      () => recallsApi.send(recall.patientId, recall.reason),
      `Rappel envoyé à ${recall.patientName}`,
      "Échec de l'envoi du rappel",
    )

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto p-4 md:p-6">
            <div className="mx-auto max-w-7xl space-y-6">
              {/* Page Header */}
              <div className="flex items-center justify-between">
                <div>
                  <h1 className="text-3xl font-semibold text-foreground">Patients à relancer</h1>
                  <p className="mt-1 text-sm text-muted-foreground">
                    Patients dont la dernière visite dépasse l&apos;intervalle de relance
                  </p>
                </div>

                {/* The recall interval is clinic-wide configuration and became admin-only
                    (security-hardening AC-7.3). The recall LIST and the per-patient actions below stay
                    available to all staff — those are day-to-day work. */}
                {isAdmin && <RecallSettingsDialog settings={settings} onSaved={setSettings} />}
              </div>

              {/* Recalls Table */}
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2">
                    <BellRing className="h-5 w-5" />
                    Relances dues
                    {!loading && !error && (
                      <Badge variant="secondary" className="ml-2">
                        {recalls.length} patient{recalls.length > 1 ? "s" : ""}
                      </Badge>
                    )}
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  {loading ? (
                    <div className="flex items-center justify-center py-12 text-muted-foreground">
                      <Loader2 className="h-5 w-5 animate-spin" />
                    </div>
                  ) : error ? (
                    <p className="py-12 text-center text-sm text-destructive">{error}</p>
                  ) : (
                    <div className="overflow-x-auto">
                      <Table>
                        <TableHeader>
                          <TableRow>
                            <TableHead>Patient</TableHead>
                            <TableHead>Téléphone</TableHead>
                            <TableHead>Dernière visite</TableHead>
                            <TableHead>Échéance</TableHead>
                            <TableHead>Retard</TableHead>
                            <TableHead>Motif</TableHead>
                            <TableHead className="text-right">Actions</TableHead>
                          </TableRow>
                        </TableHeader>
                        <TableBody>
                          {recalls.length === 0 ? (
                            <TableRow>
                              <TableCell colSpan={7} className="h-24 text-center">
                                <p className="text-muted-foreground">Aucun patient à relancer</p>
                              </TableCell>
                            </TableRow>
                          ) : (
                            recalls.map((recall) => {
                              const busy = busyPatientId === recall.patientId
                              return (
                                <TableRow key={recall.patientId}>
                                  <TableCell className="font-medium text-foreground">{recall.patientName}</TableCell>
                                  <TableCell className="text-muted-foreground">
                                    {recall.phoneNumber ?? (
                                      <span className="text-amber-700 dark:text-amber-400">Aucun numéro</span>
                                    )}
                                  </TableCell>
                                  <TableCell className="text-muted-foreground">
                                    {formatDate(recall.lastVisitDate, "Jamais")}
                                  </TableCell>
                                  <TableCell className="text-muted-foreground">{formatDate(recall.dueDate)}</TableCell>
                                  <TableCell>
                                    <Badge variant={recall.daysOverdue > 0 ? "destructive" : "secondary"}>
                                      {recall.daysOverdue} jour{recall.daysOverdue > 1 ? "s" : ""}
                                    </Badge>
                                  </TableCell>
                                  <TableCell className="text-muted-foreground">{recall.reason ?? "—"}</TableCell>
                                  <TableCell className="text-right">
                                    <div className="flex justify-end gap-2">
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        onClick={() => handleMarkContacted(recall)}
                                        disabled={busy}
                                        className="h-8 gap-1"
                                      >
                                        <CheckCircle2 className="h-3 w-3" />
                                        Marquer contacté
                                      </Button>
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        onClick={() => handleSnooze(recall)}
                                        disabled={busy}
                                        className="h-8 gap-1"
                                      >
                                        <Clock className="h-3 w-3" />
                                        Reporter
                                      </Button>
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        onClick={() => handleSend(recall)}
                                        // Disabled rather than hidden: the secretary needs to see that the
                                        // relance exists and why it can't go out, so they can call instead.
                                        // The API refuses these too — and no longer snoozes them 30 days.
                                        disabled={busy || !recall.phoneNumber}
                                        title={
                                          recall.phoneNumber
                                            ? undefined
                                            : "Ce patient n'a pas de numéro de téléphone : contactez-le autrement, puis « Marquer comme contacté »."
                                        }
                                        className="h-8 gap-1"
                                      >
                                        {busy ? (
                                          <Loader2 className="h-3 w-3 animate-spin" />
                                        ) : (
                                          <Send className="h-3 w-3" />
                                        )}
                                        Envoyer SMS/WhatsApp
                                      </Button>
                                    </div>
                                  </TableCell>
                                </TableRow>
                              )
                            })
                          )}
                        </TableBody>
                      </Table>
                    </div>
                  )}
                </CardContent>
              </Card>
            </div>
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}
