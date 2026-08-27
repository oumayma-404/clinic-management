"use client"

import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { downloadBlob } from '@/lib/download'
import { Check, Copy, Download, Printer } from 'lucide-react'

interface RecoveryCodesPanelProps {
  codes: string[]
  /** What the acknowledgement unlocks — « Continuer », « Terminer »… */
  confirmLabel: string
  onConfirm: () => void
}

/**
 * The eight recovery codes, shown **once** (`hosted-security-hardening` FR-1.4, step 18).
 *
 * ⚠️ **Delivery goes through `lib/download.ts`.** A hand-rolled `<a download>` is silently ignored by iOS
 * Safari for a `blob:` URL — the file simply never arrives — and the `blob-delivery` check fails the gate on
 * one.
 *
 * ⚠️ **The live region announces a SUMMARY, not the codes.** Reception is often a shared desk, and a screen
 * reader reading eight recovery codes aloud is the one outcome this panel must not produce.
 *
 * ⚠️ **An explicit acknowledgement gates the way forward**, because this is the only time these exist in
 * readable form: nothing stores them and they cannot be shown again.
 */
export function RecoveryCodesPanel({ codes, confirmLabel, onConfirm }: RecoveryCodesPanelProps) {
  const [acknowledged, setAcknowledged] = useState(false)
  const [copied, setCopied] = useState(false)

  const asText = codes.join('\n')

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(asText)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    } catch {
      // The codes are on screen and selectable; a refused clipboard is not a failure of the flow.
    }
  }

  const download = async () => {
    await downloadBlob(
      new Blob([`Codes de récupération\n\n${asText}\n`], { type: 'text/plain;charset=utf-8' }),
      'codes-de-recuperation.txt'
    )
  }

  return (
    <div className="space-y-4">
      <div
        role="status"
        className="rounded-lg border border-warning/30 bg-warning-wash p-3 text-sm text-warning-ink"
      >
        {/* Announced as a count, never as the codes themselves. */}
        {codes.length} codes de récupération ont été générés. Ils ne seront plus affichés.
      </div>

      <p className="text-sm text-muted-foreground">
        Conservez-les hors de votre téléphone. Chacun ne fonctionne qu&apos;une seule fois et vous permet de vous
        connecter si vous perdez votre application d&apos;authentification.
      </p>

      <ul className="grid grid-cols-1 gap-2 rounded-lg bg-muted/40 p-3 sm:grid-cols-2">
        {codes.map((code) => (
          <li key={code} className="break-all font-mono text-sm">
            {code}
          </li>
        ))}
      </ul>

      <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
        <Button type="button" variant="outline" onClick={copy} className="min-h-11">
          {copied ? <Check aria-hidden className="size-4" /> : <Copy aria-hidden className="size-4" />}
          {copied ? 'Copiés' : 'Copier'}
        </Button>
        <Button type="button" variant="outline" onClick={download} className="min-h-11">
          <Download aria-hidden className="size-4" />
          Télécharger
        </Button>
        <Button type="button" variant="outline" onClick={() => window.print()} className="min-h-11">
          <Printer aria-hidden className="size-4" />
          Imprimer
        </Button>
      </div>

      <div className="flex items-start gap-3 rounded-lg border border-border p-3">
        <Checkbox
          id="ack-codes"
          checked={acknowledged}
          onCheckedChange={(v) => setAcknowledged(v === true)}
          className="mt-0.5"
        />
        <Label htmlFor="ack-codes" className="text-sm font-normal leading-snug">
          J&apos;ai enregistré ces codes en lieu sûr.
        </Label>
      </div>

      <Button type="button" onClick={onConfirm} disabled={!acknowledged} className="min-h-11 w-full">
        {confirmLabel}
      </Button>
    </div>
  )
}
