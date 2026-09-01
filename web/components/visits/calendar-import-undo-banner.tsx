"use client"

import { useCallback, useEffect, useState } from "react"
import { CalendarX2, Loader2, Undo2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { useSession } from "@/lib/auth/session"
import { isAdmin } from "@/lib/auth/can"
import {
  googleCalendarApi,
  type CalendarImportRevertPreview,
  type CalendarImportRunDto,
} from "@/lib/api/google-calendar"
import { getErrorMessage } from "@/lib/errors"
import { toast } from "sonner"
import { formatDateFr } from "@/lib/format"

/**
 * « Annuler cet import » — the way back from a Google Calendar import, offered where the damage is visible.
 *
 * <p><b>Why it lives on « À clôturer » and not in the settings.</b> A cabinet that regrets pressing « Importer
 * depuis Google » is looking at this page: the import's past events land here as séances demanding a présence,
 * a fiche and an encaissement, and the placeholder patients land on the tab beside it. Somebody in that state
 * does not go hunting through a settings panel for the way out. The settings list stays as the durable record;
 * this is the door.</p>
 *
 * <p><b>It withdraws itself.</b> Nothing renders unless the server names a run that still owns rows and has not
 * been undone — so it disappears once the undo lands, rather than becoming furniture on a page people read
 * every morning.</p>
 *
 * <p>⚠️ <b>The preview is the safety, and it is not optional.</b> The person pressing this is the cabinet, not
 * the vendor: nobody is holding a backup and nobody is watching row counts. So the confirmation asks the server
 * what would happen, prints both figures, and names every row that will survive with its own reason — rather
 * than saying « êtes-vous sûr ? » over a number nobody can check.</p>
 */
export function CalendarImportUndoBanner({ onReverted }: { onReverted: () => void }) {
  const { user } = useSession()
  const mayRevert = isAdmin(user?.role)

  const [run, setRun] = useState<CalendarImportRunDto | null>(null)
  const [preview, setPreview] = useState<CalendarImportRevertPreview | null>(null)
  const [open, setOpen] = useState(false)
  const [loadingPreview, setLoadingPreview] = useState(false)
  const [reverting, setReverting] = useState(false)

  const load = useCallback(async () => {
    try {
      const page = await googleCalendarApi.listImports({ latestUndoable: true })
      setRun(page.items[0] ?? null)
    } catch {
      // Silent, deliberately. This banner is an offer, not a fact the page depends on — and a clinic that has
      // never connected Google would otherwise meet an error strip on a screen about something else entirely.
      setRun(null)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  if (!run || !run.canRevert) {
    return null
  }

  const openConfirmation = async () => {
    setOpen(true)
    setLoadingPreview(true)
    setPreview(null)

    try {
      setPreview(await googleCalendarApi.previewRevert(run.id))
    } catch (err) {
      toast.error(getErrorMessage(err))
      setOpen(false)
    } finally {
      setLoadingPreview(false)
    }
  }

  const confirmRevert = async () => {
    setReverting(true)

    try {
      const result = await googleCalendarApi.revertImport(run.id)

      toast.success(
        `Import annulé — ${result.appointmentsDeleted} rendez-vous et ${result.patientsDeleted} fiche${
          result.patientsDeleted === 1 ? "" : "s"
        } supprimés.`,
        result.kept.length > 0
          ? {
              description: `${result.kept.length} ligne${
                result.kept.length === 1 ? " a été conservée" : "s ont été conservées"
              } : du travail y est enregistré.`,
            }
          : undefined,
      )

      setOpen(false)
      await load()
      onReverted()
    } catch (err) {
      toast.error(getErrorMessage(err))
    } finally {
      setReverting(false)
    }
  }

  const created = run.appointmentsCreated
  const fiches = run.patientsCreated

  return (
    <>
      {/* At 320 px the sentence wraps and the action drops beneath it full-width; `sm:` puts them side by side.
          The action is `min-h-11` — 44 px on a coarse pointer, per the device contract. */}
      <div className="flex flex-col gap-3 rounded-lg border border-amber-200 bg-amber-50 p-4 text-amber-950 sm:flex-row sm:items-center sm:justify-between dark:border-amber-900/50 dark:bg-amber-950/30 dark:text-amber-100">
        <div className="flex min-w-0 items-start gap-3">
          <CalendarX2 className="mt-0.5 size-5 shrink-0" aria-hidden />
          <p className="min-w-0 text-sm">
            <span className="font-medium">
              {created.toLocaleString("fr-TN")} rendez-vous
              {fiches > 0 ? ` et ${fiches.toLocaleString("fr-TN")} fiche${fiches === 1 ? "" : "s"} patient` : ""}
            </span>{" "}
            {created === 1 && fiches === 0 ? "a été importé" : "ont été importés"} depuis Google Agenda
            {" "}
            {run.startedAtUtc ? `le ${formatDateFr(run.startedAtUtc)}` : ""} ({run.triggeredBy.toLowerCase()}).
          </p>
        </div>

        {mayRevert ? (
          <Button
            type="button"
            variant="outline"
            className="min-h-11 w-full shrink-0 border-amber-300 bg-white/70 hover:bg-white sm:w-auto dark:border-amber-800 dark:bg-transparent"
            onClick={() => void openConfirmation()}
          >
            <Undo2 className="mr-2 size-4" aria-hidden />
            Annuler cet import
          </Button>
        ) : (
          // A secretary sees the explanation — which is the half that makes the page make sense — and is told
          // who can act, rather than meeting a button that answers 403.
          <p className="shrink-0 text-xs text-amber-900/80 dark:text-amber-200/80">
            Un administrateur peut annuler cet import.
          </p>
        )}
      </div>

      <AlertDialog open={open} onOpenChange={setOpen}>
        {/* `max-h-[85dvh]` + an inner scroller: the kept list is unbounded, and a dialog taller than the
            viewport puts its own actions off screen — which on a destructive confirmation is the worst place
            for them to be. */}
        <AlertDialogContent className="max-h-[85dvh] overflow-hidden">
          <AlertDialogHeader>
            <AlertDialogTitle>Annuler cet import ?</AlertDialogTitle>
            <AlertDialogDescription asChild>
              <div className="space-y-3 text-sm">
                {loadingPreview ? (
                  <p className="flex items-center gap-2 text-muted-foreground">
                    <Loader2 className="size-4 animate-spin" aria-hidden />
                    Vérification de ce qui peut être supprimé…
                  </p>
                ) : preview ? (
                  <>
                    <p>
                      Cette action supprimera{" "}
                      <strong className="tabular-nums">
                        {preview.appointmentsToDelete.toLocaleString("fr-TN")} rendez-vous
                      </strong>{" "}
                      et{" "}
                      <strong className="tabular-nums">
                        {preview.patientsToDelete.toLocaleString("fr-TN")} fiche
                        {preview.patientsToDelete === 1 ? "" : "s"} patient
                      </strong>{" "}
                      créés par cet import.
                    </p>

                    {/* The Google calendar is the practice's own record and the reason this feature exists —
                        stated, because « supprimer » next to « Google Agenda » reads as if it might not be. */}
                    <p className="text-muted-foreground">
                      Votre agenda Google n’est pas modifié.
                    </p>

                    {preview.kept.length > 0 ? (
                      <div className="space-y-2">
                        <p>
                          <strong className="tabular-nums">{preview.kept.length}</strong> ligne
                          {preview.kept.length === 1 ? " sera conservée" : "s seront conservées"} :
                        </p>
                        <ul className="max-h-48 space-y-1 overflow-y-auto rounded-md border bg-muted/40 p-2 text-xs">
                          {preview.kept.map((row) => (
                            <li key={row.id} className="flex flex-wrap gap-x-2">
                              <span className="font-medium">{row.label}</span>
                              {row.when ? (
                                <span className="text-muted-foreground">{formatDateFr(row.when)}</span>
                              ) : null}
                              <span className="text-muted-foreground">— {row.reason}</span>
                            </li>
                          ))}
                        </ul>
                      </div>
                    ) : null}
                  </>
                ) : null}
              </div>
            </AlertDialogDescription>
          </AlertDialogHeader>

          <AlertDialogFooter>
            <AlertDialogCancel className="min-h-11">Non, garder</AlertDialogCancel>
            {/* Destructive action second, and disabled until the preview has actually answered — confirming
                against a figure nobody has seen is the thing this dialog exists to prevent. */}
            <AlertDialogAction
              className="min-h-11 bg-destructive text-destructive-foreground hover:bg-destructive/90"
              disabled={!preview || reverting}
              onClick={(event) => {
                event.preventDefault()
                void confirmRevert()
              }}
            >
              {reverting ? (
                <>
                  <Loader2 className="mr-2 size-4 animate-spin" aria-hidden />
                  Annulation…
                </>
              ) : (
                "Oui, annuler l’import"
              )}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
