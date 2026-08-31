"use client"

import { useEffect, useState } from "react"
import { Download, Loader2 } from "lucide-react"
import { toast } from "sonner"

import { Button } from "@/components/ui/button"
import { StepUpDialog } from "@/components/security/step-up-dialog"
import { fetchExportCsv } from "@/lib/api/export"
import { securityApi } from "@/lib/api/security"
import { downloadBlob } from "@/lib/download"
import { showErrorToast } from "@/lib/errors"
import { cn } from "@/lib/utils"

interface ExportButtonProps {
  /** The export route, relative to the API base — e.g. `/patients/export`. */
  path: string
  /**
   * The **filters currently on screen**. Passed straight through as query parameters, which is what makes the
   * file match the list: the server re-sends the same query with no paging, so whatever narrows the table
   * narrows the file. Omit nothing and add nothing — a divergence here is a file that quietly disagrees with the
   * screen it was exported from.
   */
  params?: Record<string, string | number | boolean | undefined | null>
  /** What the file is, for the toast — « 128 patients exportés ». */
  label?: string
  className?: string
  /** Icon-only below `sm:`, where a toolbar has no room for a second labelled button. */
  compact?: boolean
  /**
   * When set, the export first asks the user to confirm their identity, and the resulting token is sent with
   * the request. The value must match the action the server spends — see `PatientsController.ExportStepUpAction`.
   *
   * ⚠️ **Opt-in per list, deliberately.** Eight lists share this button and most of them export operational
   * data (stock, dépenses, prothèses). The one that carries the cabinet's identified medical dataset — date de
   * naissance, adresse, identifiant CNAM, antécédents médicaux, allergies — is the patient roster, and putting
   * a password prompt in front of all eight would train people to type it without reading.
   */
  stepUpAction?: string
  /** Shown in the confirmation, so the user knows what they are authorising. Required with `stepUpAction`. */
  stepUpPurpose?: string
  /**
   * The name to save under when the server's own `Content-Disposition` cannot be read, and the word used in the
   * button's accessible name. Defaults to a CSV because most exports are one.
   *
   * ⚠️ Not cosmetic: the accessible name used to be the literal « Exporter en CSV » for every caller, so the
   * patient-dossier button — which downloads a ZIP — announced itself as a CSV to a screen reader, and the
   * fallback saved it as `export.csv`.
   */
  fallbackFilename?: string
}

/**
 * « Exporter » (L5) — the one export affordance in the product.
 *
 * <p>Shared for the reason `ui/empty-state.tsx` and `ui/access-denied-card.tsx` are: eight lists export, and
 * eight hand-rolled buttons is how one of them forgets the in-flight state, one forgets the error toast, and one
 * ends up with a `text-sm` that defeats the primitive's iOS zoom guard.</p>
 *
 * <p>Downloads through `downloadBlob`, which is not a convenience: on iOS Safari an `<a download>` on a `blob:`
 * URL is **ignored**, so a hand-rolled anchor would silently deliver nothing on the tablet a dentist holds.</p>
 */
export function ExportButton({
  path,
  params,
  label = "lignes",
  className,
  compact,
  stepUpAction,
  stepUpPurpose,
  fallbackFilename = "export.csv",
}: ExportButtonProps) {
  const [working, setWorking] = useState(false)
  const [confirming, setConfirming] = useState(false)

  // Which proof the confirmation offers first.
  //
  // ⚠️ A failed read is NOT rendered as « this account has no second factor » — `clinic-archive-card`'s rule:
  // that collapse would offer a password box to somebody whose account requires a code, and the refusal would
  // look like a wrong password. Unknown defaults to « has one », which degrades to the harder proof, not the
  // easier one.
  const [hasTotp, setHasTotp] = useState(false)
  const [totpKnown, setTotpKnown] = useState(false)

  useEffect(() => {
    if (!stepUpAction) return
    let alive = true
    securityApi
      .getTotpState()
      .then((state) => {
        if (!alive) return
        setHasTotp(state.enrolledAt !== null)
        setTotpKnown(true)
      })
      .catch(() => {
        /* left unknown on purpose — see the note above */
      })
    return () => {
      alive = false
    }
  }, [stepUpAction])

  const runExport = async (stepUpToken?: string) => {
    setWorking(true)
    try {
      const { blob, filename } = await fetchExportCsv(path, params, stepUpToken, fallbackFilename)
      await downloadBlob(blob, filename)
      toast.success("Export terminé", { description: filename })
    } catch (err) {
      // A 403 (« les exports d'argent sont réservés ») is a real answer and must arrive as its French message.
      showErrorToast(err, `L'export des ${label} a échoué.`)
    } finally {
      setWorking(false)
    }
  }

  const handleExport = async () => {
    if (stepUpAction) {
      setConfirming(true)
      return
    }
    await runExport()
  }

  const button = (
    <Button
      type="button"
      variant="outline"
      size="sm"
      onClick={handleExport}
      disabled={working}
      /*
       * ⚠️ `coarse:h-11` ON TOP of `touch-target`, because this control is NOT isolated in practice.
       *
       * At `size="sm"` the painted box is 32 px, so the 44 px overlay overhangs 6 px each side — and every screen
       * that uses this button puts it in a `gap-2` (8 px) action row beside « Nouvelle … » or « Aujourd'hui ».
       * Two overlays 8 px apart with a 6 px overhang each OVERLAP, and the later sibling paints last, so it eats
       * its neighbour's taps (§ 2). Growing the box removes the overlap and makes the MEASURED size 44 as well.
       * `touch-target` stays for the screens where it genuinely does sit alone.
       */
      // (a grown box would change the toolbar's rhythm) — and it has no `overflow-hidden` ancestor to clip it.
      className={cn("touch-target gap-1.5 coarse:h-11", className)}
      aria-label={compact ? `Exporter ${label}` : undefined}
    >
      {working ? (
        <Loader2 className="size-4 animate-spin" aria-hidden="true" />
      ) : (
        <Download className="size-4" aria-hidden="true" />
      )}
      {/* Hidden below `sm:` when compact, but the label stays in the DOM for screen readers. */}
      <span className={compact ? "sr-only sm:not-sr-only" : undefined}>
        {working ? "Export…" : "Exporter"}
      </span>
    </Button>
  )

  if (!stepUpAction) {
    return button
  }

  return (
    <>
      {button}
      <StepUpDialog
        open={confirming}
        onOpenChange={setConfirming}
        action={stepUpAction}
        purpose={stepUpPurpose ?? "Exporter cette liste"}
        hasTotp={totpKnown ? hasTotp : true}
        onConfirmed={(token) => {
          setConfirming(false)
          void runExport(token)
        }}
      />
    </>
  )
}
